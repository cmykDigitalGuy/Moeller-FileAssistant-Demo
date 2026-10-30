using System.Text.Json;
using System.Xml.Linq;

namespace Moeller.Core;

internal record SubmittedArtwork(Artwork File,string ItemNumber,string Description,int OrderedQuantity,int AllocatedQuantity);
internal record SubmissionFacts(Job Job,string QuantityWarnings,string IncompleteComponents,List<SubmittedArtwork> Artwork);

public sealed partial class Engine
{
    public const string AdvancedDestination="Moeller XML + samples";
    public const string PrintDestination="PrintShare";
    public string Completeness(int jobId,IEnumerable<string>? selected=null)
    {
        lock(gate)
        {
            var ids=selected?.ToHashSet();var files=Artworks(jobId).Where(f=>ids==null||ids.Contains(f.Id)).ToList();var missing=new List<string>();
            foreach(var item in Items(jobId))foreach(var version in Designs(item.Id))
            {
                var roles=files.Where(f=>f.ItemId==item.Id&&f.Version==version.Number&&f.Ready&&f.Integrity=="Verified").SelectMany(f=>Roles(f.Component)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach(string expected in Roles(item.Components))if(!roles.Contains(expected))missing.Add($"Item {item.Number} V{version.Number:00}: {expected} is missing or not Ready.");
            }
            return string.Join(Environment.NewLine,missing);
        }
    }
    public Submission Submit(int jobId,IEnumerable<string> selected,bool complete=false,bool acknowledgeQuantity=false,bool replaceCollisions=false)
    {
        lock(gate)
        {
            var files=selected.Distinct().Select(Artwork).ToList();
            if(files.Count==0)throw new InvalidOperationException("Select at least one Ready production file.");
            if(files.Any(f=>f.JobId!=jobId))throw new InvalidOperationException("Different jobs must be submitted separately.");
            foreach(var file in files)if(!file.Active||!file.Ready||Verify(file.Id)!="Verified")throw new InvalidOperationException($"{file.Filename} is not an intact Ready active revision.");
            string quantityWarnings=QuantityWarnings(jobId),incomplete=Completeness(jobId,files.Select(f=>f.Id));
            if(!acknowledgeQuantity&&quantityWarnings!="")throw new InvalidOperationException("Review and acknowledge quantity allocation differences before submission.");
            if(complete&&incomplete!="")throw new InvalidOperationException("Complete-job submission is not ready:\n"+incomplete);
            var job=Job(jobId);string id=Guid.NewGuid().ToString("N"),now=DateTimeOffset.UtcNow.ToString("O");
            string relative=Path.Combine(job.Folder,"submissions",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+id[..8]);
            string directory=Full(relative);Directory.CreateDirectory(Path.Combine(directory,"files"));
            File.WriteAllText(Path.Combine(directory,"INCOMPLETE.txt"),"This submission snapshot is being prepared. It is not a successful delivery.");
            foreach(var file in files)
            {
                string source=Full(file.Path),target=Full(Path.Combine(relative,"files",file.Filename));CopyExact(source,target,file.Hash);
                if(Path.GetExtension(source)==".zip")File.WriteAllText(target+".identity.json",Safety.Json(new{file.Id,file.LogicalId,file.Hash,SampleOnly=true,RouteNotValidated=true}));
            }
            var facts=new SubmissionFacts(job,quantityWarnings,incomplete,files.Select(file=>
            {
                var item=Item(file.ItemId);var design=Designs(item.Id).Single(v=>v.Number==file.Version);
                return new SubmittedArtwork(file,item.Number,item.Description,item.Quantity,design.Quantity);
            }).ToList());
            string specifications=Full(Path.Combine(relative,"specifications.json"));File.WriteAllText(specifications,Safety.Json(facts));string specificationsHash=Safety.Hash(specifications);
            var submission=new Submission(id,jobId,now,complete?"Complete job":"Selected artwork",relative);
            db.Atomic(()=>
            {
                db.Execute("INSERT INTO submissions VALUES($0,$1,$2,$3,$4)",id,jobId,now,submission.Scope,relative);
                foreach(var file in files)db.Execute("INSERT INTO submission_files VALUES($0,$1)",id,file.Id);
                foreach(string dest in new[]{AdvancedDestination,PrintDestination})db.Execute("INSERT INTO deliveries VALUES($0,$1,'Pending','Not attempted')",id,dest);
                Set("submission-specifications:"+id,specificationsHash);
                Log(jobId,"Submission created",$"{id}; {submission.Scope}; {files.Count} physical file(s). Files, quantities and names are frozen for retries. This does not complete unrelated items.");
            });
            File.Delete(Path.Combine(directory,"INCOMPLETE.txt"));File.WriteAllText(Path.Combine(directory,"submission.json"),Safety.Json(submission));
            Deliver(submission,replaceCollisions);return submission;
        }
    }
    public void Retry(string submissionId,bool replaceCollisions=false)
    {
        lock(gate)
        {
            var sub=db.Query("SELECT id,job_id,created,scope,directory FROM submissions WHERE id=$0",r=>new Submission(r.GetString(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetString(4)),submissionId).Single();Deliver(sub,replaceCollisions);
        }
    }
    private SubmissionFacts ReadSubmissionFacts(Submission sub)
    {
        string path=Full(Path.Combine(sub.Directory,"specifications.json")),expected=Get("submission-specifications:"+sub.Id);
        if(expected==""||!File.Exists(path)||new FileInfo(path).Length>8_000_000||Safety.Hash(path)!=expected)throw new IOException("The frozen submission specifications are missing or changed. Current job settings were not substituted.");
        var facts=JsonSerializer.Deserialize<SubmissionFacts>(File.ReadAllText(path))??throw new IOException("Submission specifications are unreadable.");
        if(facts.Job.Id!=sub.JobId||facts.Artwork.Count==0)throw new IOException("Submission specifications do not match this job.");
        return facts;
    }
    private void Deliver(Submission sub,bool replaceCollisions)
    {
        foreach(var delivery in Deliveries(sub.Id).Where(d=>d.Status!="Successful"&&d.Status!="Not applicable"))
        {
            string status="Successful",detail="";
            try
            {
                if(FailNextDestination==delivery.Destination){FailNextDestination=null;throw new IOException("Intentional demo failure. Retry this destination to continue.");}
                var facts=ReadSubmissionFacts(sub);var job=facts.Job;var files=facts.Artwork.Select(a=>a.File).ToList();
                foreach(var file in files)
                {
                    string snapshot=Full(Path.Combine(sub.Directory,"files",file.Filename));
                    if(!File.Exists(snapshot)||Safety.Hash(snapshot)!=file.Hash)throw new IOException("The immutable submission snapshot is missing or changed. It was not delivered.");
                }
                if(delivery.Destination==AdvancedDestination)
                {
                    var moeller=new XDocument(new XElement("MoellerJobPackage",new XAttribute("DemoOnly","true"),new XAttribute("SchemaVersion","1"),
                        new XElement("Job",new XAttribute("Number",job.Number),new XElement("Customer",job.Customer),new XElement("Title",job.Title)),
                        new XElement("Submission",new XAttribute("ID",sub.Id),new XAttribute("Timestamp",sub.Time),new XAttribute("Scope",sub.Scope),new XAttribute("ProductionAcceptance","NOT_TESTED"),new XAttribute("CompleteScope",facts.IncompleteComponents==""?"true":"false")),
                        new XElement("Warnings",facts.QuantityWarnings),new XElement("UnfinishedComponents",facts.IncompleteComponents),
                        new XElement("Artwork",facts.Artwork.Select(a=>
                        {
                            var file=a.File;
                            return new XElement("File",new XAttribute("ID",file.Id),new XAttribute("LogicalID",file.LogicalId),new XAttribute("Item",a.ItemNumber),new XAttribute("Version",file.Version),new XAttribute("Revision",file.Revision),
                                new XElement("Description",a.Description),new XElement("Components",file.Component),new XElement("OrderedQuantity",a.OrderedQuantity),new XElement("AllocatedQuantity",a.AllocatedQuantity),new XElement("Filename",file.Filename),new XElement("Path","files/"+file.Filename),new XElement("SHA256",file.Hash),new XElement("SourceSHA256",file.SourceHash));
                        }))));
                    var apo=new XDocument(new XElement("apoXML_Demonstration",new XAttribute("SampleOnly","true"),new XElement("Notice","SIMULATED OUTPUT - NOT VALIDATED FOR ECO3 APOGEE"),new XElement("SubmissionID",sub.Id),new XElement("MoellerXML","Moeller.xml")));
                    XNamespace jdf="http://www.CIP4.org/JDFSchema_1_1";
                    var sample=new XDocument(new XElement(jdf+"JDF",new XAttribute("ID","DEMO_"+sub.Id),new XAttribute("Type","Product"),new XAttribute("Version","1.5"),new XAttribute("Status","Waiting"),new XElement(jdf+"Comment","UNVALIDATED DEMONSTRATION ONLY. No Apogee import or acceptance has occurred.")));
                    string output=Full(Path.Combine("Simulated Outputs","Apogee Samples",job.Number+"-"+sub.Id[..8]));Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"files"));
                    foreach(var pair in new[]{("Moeller.xml",moeller),("SAMPLE-ONLY-apoXML.xml",apo),("SAMPLE-ONLY.jdf",sample)})
                    {
                        WriteDocument(Full(Path.Combine(sub.Directory,pair.Item1)),pair.Item2);
                        CopyExact(Full(Path.Combine(sub.Directory,pair.Item1)),Path.Combine(output,pair.Item1),Safety.Hash(Full(Path.Combine(sub.Directory,pair.Item1))));
                    }
                    foreach(var file in files)CopyExact(Full(Path.Combine(sub.Directory,"files",file.Filename)),Path.Combine(output,"files",file.Filename),file.Hash);
                    File.WriteAllText(Path.Combine(output,"READ-ME.txt"),"LOCAL SIMULATION ONLY. Moeller XML is structured application output. apoXML and JDF are unvalidated examples, not accepted production jobs. PPML routing is not validated.");
                    detail=$"Created local XML/sample package: {Relative(output)}. No production system was contacted.";
                }
                else
                {
                    int count=0;var collisions=new List<string>();
                    foreach(var file in files.Where(f=>Path.GetExtension(f.Path).Equals(".pdf",StringComparison.OrdinalIgnoreCase)))
                    {
                        string target=Safety.Within(Root,Path.Combine(PrintShare,file.Filename));
                        if(File.Exists(target)&&Safety.Hash(target)!=file.Hash)
                        {
                            if(!replaceCollisions){collisions.Add(file.Filename);continue;}
                            string backup=Full(Path.Combine("Simulated Outputs","PrintShare","history",DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+"-"+Guid.NewGuid().ToString("N")[..6]+"-"+file.Filename));
                            string previousHash=Safety.Hash(target);CopyExact(target,backup,previousHash);
                            string staged=Full(Path.Combine(".staging",Guid.NewGuid().ToString("N")+".pdf"));CopyExact(Full(Path.Combine(sub.Directory,"files",file.Filename)),staged,file.Hash);File.Move(staged,target,true);
                            if(Safety.Hash(target)!=file.Hash)throw new IOException("PrintShare replacement verification failed. Prior copy was preserved in history.");
                            Log(job.Id,"PrintShare collision replaced",$"{file.Filename}; outgoing SHA256 {previousHash}; archive {Relative(backup)}");
                        }
                        else CopyExact(Full(Path.Combine(sub.Directory,"files",file.Filename)),target,file.Hash);
                        count++;
                    }
                    if(collisions.Count>0){status="Needs attention";detail="Filename collision. Nothing was silently overwritten: "+string.Join(", ",collisions);}
                    else if(count==0){status="Not applicable";detail="PrintShare accepts PDFs in this demo. PPML ZIP was preserved in the submission package; its production route is unvalidated.";}
                    else detail=$"{count} verified PDF(s) in the common local PrintShare incoming folder.";
                }
            }
            catch(Exception e){status="Failed";detail=e.Message;}
            db.Execute("UPDATE deliveries SET status=$0,detail=$1 WHERE submission_id=$2 AND destination=$3",status,detail,sub.Id,delivery.Destination);
            Log(sub.JobId,"Destination "+status,sub.Id+" / "+delivery.Destination+" / "+detail);
        }
        File.WriteAllText(Full(Path.Combine(sub.Directory,"delivery-results.json")),Safety.Json(Deliveries(sub.Id)));
    }
    private void CopyExact(string source,string target,string expected)
    {
        source=Safety.Within(Root,source);target=Safety.Within(Root,target);
        if(File.Exists(target)){if(Safety.Hash(target)==expected)return;throw new IOException("An existing destination differs: "+Path.GetFileName(target));}
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);string stage=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var output=new FileStream(stage,FileMode.CreateNew,FileAccess.Write,FileShare.None)){input.CopyTo(output);output.Flush(true);}
            if(Safety.Hash(stage)!=expected)throw new IOException("The file changed while copying.");File.Move(stage,target,false);
        }
        finally{if(File.Exists(stage))File.Delete(stage);}
    }
    private void WriteDocument(string target,XDocument document)
    {
        target=Safety.Within(Root,target);string temp=target+".tmp";document.Save(temp);XDocument.Load(temp);
        try{if(File.Exists(target)){if(Safety.Hash(target)!=Safety.Hash(temp))throw new IOException("Existing XML output differs; it was not overwritten.");}else File.Move(temp,target,false);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}

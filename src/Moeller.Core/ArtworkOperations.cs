using System.IO.Compression;
using System.Text.Json;

namespace Moeller.Core;

public sealed partial class Engine
{
    private static string[] Roles(string value) => value.Split('+',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    private void ValidateSource(string path, Item item)
    {
        path=Safety.Within(Root,path);
        if(!File.Exists(path)) throw new FileNotFoundException("Selected demo artwork does not exist.",path);
        if(JobForPath(path)?.Id!=item.JobId) throw new InvalidOperationException("The selected artwork belongs to a different job or an unsupported folder depth.");
        if(new FileInfo(path).Length>250L*1024*1024) throw new InvalidOperationException("The offline demo limits artwork to 250 MB per file.");
        string extension=Path.GetExtension(path).ToLowerInvariant();
        if(extension==".zip")
        {
            using var zip=ZipFile.OpenRead(path);
            if(!zip.Entries.Any(e=>e.FullName.EndsWith(".ppml",StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("This ZIP is not the supported PPML demonstration package. Nothing was extracted.");
        }
        else if(extension!=".pdf") throw new InvalidDataException("Select a PDF or the supplied PPML demonstration ZIP.");
    }
    public string ProposedName(Assignment input)
    {
        var item=Item(input.ItemId); var job=Job(item.JobId);
        return Names.Production(job.Number,item.Number,input.Version,1,item.Description,input.Component,Path.GetExtension(input.Source));
    }
    public Artwork Assign(Assignment input)
    {
        lock(gate)
        {
            var item=Item(input.ItemId); var job=Job(item.JobId); ValidateSource(input.Source,item);
            if(input.Version<1 || input.Version>9999 || input.Quantity<1 || input.Quantity>100000000) throw new ArgumentException("Version and quantity must be positive.");
            var roles=Roles(input.Component); var allowed=Roles(item.Components);
            if(roles.Length==0 || roles.Except(allowed,StringComparer.OrdinalIgnoreCase).Any()) throw new ArgumentException("Select the appropriate component for this item.");
            if(Artworks(job.Id).Any(f=>f.ItemId==item.Id && f.Version==input.Version && Roles(f.Component).Intersect(roles,StringComparer.OrdinalIgnoreCase).Any()))
                throw new InvalidOperationException("That item, design and component already has artwork. Use Replace Artwork, or choose another version.");
            var design=Designs(item.Id).SingleOrDefault(v=>v.Number==input.Version);
            if(design!=null && design.Quantity!=input.Quantity) throw new InvalidOperationException($"V{input.Version:00} is allocated {design.Quantity:N0}. Use Split / quantity to explicitly change that allocation first.");
            return CreateRevision(item,input.Source,Guid.NewGuid().ToString("N"),input.Version,1,input.Component,input.Quantity,input.Ready,null,"Artwork assigned");
        }
    }
    private Artwork CreateRevision(Item item,string source,string logical,int version,int revision,string component,int quantity,bool ready,Artwork? old,string action)
    {
        var job=Job(item.JobId); string id=Guid.NewGuid().ToString("N"),opid=Guid.NewGuid().ToString("N"),extension=Path.GetExtension(source).ToLowerInvariant();
        string target=Full(Path.Combine(job.Folder,"production",Names.Production(job.Number,item.Number,version,revision,item.Description,component,extension)));
        if(File.Exists(target)) throw new IOException("The production filename already exists. No file was overwritten.");
        string stage=Full(Path.Combine(".staging",opid+extension)); string hash=Safety.Hash(source);
        var op=new PendingOperation(opid,Relative(target),Relative(stage),id,action);
        db.Execute("INSERT INTO operations VALUES($0,$1,'Pending','Preparing working copy')",opid,Safety.Json(op));
        string? archive=null;
        bool committed=false;
        try
        {
            var identity=new Identity(logical,id,job.Number,item.Number,version,revision,component,hash);
            if(extension==".pdf") PdfTools.Stamp(source,stage,identity); else File.Copy(source,stage,false);
            if(Safety.Hash(source)!=hash) throw new IOException("The source changed during preparation. Retry when editing has finished.");
            string finalHash=Safety.Hash(stage);
            if(old!=null && old.Active)
            {
                string oldPath=Full(old.Path);
                if(!File.Exists(oldPath)) throw new IOException("The previous active file is missing. Relink it before replacing artwork.");
                archive=Full(Path.Combine(job.Folder,"previous versions",old.Filename));
                if(File.Exists(archive)) archive=Full(Path.Combine(job.Folder,"previous versions",Path.GetFileNameWithoutExtension(old.Filename)+"-"+opid[..8]+Path.GetExtension(old.Filename)));
                File.Copy(oldPath,archive,false);
                if(Safety.Hash(archive)!=Safety.Hash(oldPath)) throw new IOException("The prior revision archive did not verify.");
            }
            File.Move(stage,target,false);
            if(extension==".zip") File.WriteAllText(target+".identity.json",Safety.Json(identity));
            db.Atomic(()=>
            {
                db.Execute("INSERT INTO designs(item_id,number,quantity) VALUES($0,$1,$2) ON CONFLICT(item_id,number) DO NOTHING",item.Id,version,quantity);
                if(old!=null && old.Active) db.Execute("UPDATE files SET active=0,ready=0,path=$0 WHERE id=$1",Relative(archive!),old.Id);
                db.Execute("INSERT INTO files VALUES($0,$1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,'Verified',1)",id,logical,job.Id,item.Id,version,revision,component,Relative(source),Relative(target),hash,finalHash,ready?1:0);
                db.Execute("UPDATE operations SET state='Complete',detail='File, metadata, hash and database committed' WHERE id=$0",opid);
                Log(job.Id,action,$"{Path.GetFileName(target)}; quantity {quantity:N0}; logical identity {logical}; source SHA256 {hash}; final SHA256 {finalHash}"+(old!=null?$"; previous revision {old.Id}; previously submitted {WasSubmitted(old.Id)}":""));
            });
            committed=true;
            if(old!=null && old.Active)
            {
                try { File.Delete(Full(old.Path)); if(File.Exists(Full(old.Path)+".identity.json")) File.Delete(Full(old.Path)+".identity.json"); }
                catch(Exception e) { Log(job.Id,"Old working copy needs cleanup",e.Message+". The archived and new revision are safe."); }
            }
            if(old!=null) WriteReplacementLog(job.Id);
            return Artwork(id);
        }
        catch
        {
            if(!committed)
            {
                try { if(File.Exists(stage)) File.Delete(stage); if(File.Exists(target)) File.Delete(target); if(File.Exists(target+".identity.json")) File.Delete(target+".identity.json"); } catch { }
                db.Execute("UPDATE operations SET state='Failed',detail='Operation did not commit; source and prior active revision retained' WHERE id=$0",opid);
            }
            throw;
        }
    }
    public Artwork Replace(string id,string replacement,bool acknowledgeModified=false)
    {
        lock(gate)
        {
            var old=Artwork(id); if(!old.Active) throw new InvalidOperationException("Select the current active revision.");
            var item=Item(old.ItemId); ValidateSource(replacement,item);
            if(!acknowledgeModified && Verify(id)!="Verified") throw new InvalidOperationException("The active artwork has changed. Use Review modified to explicitly approve it as a new revision first.");
            if(!Path.GetExtension(replacement).Equals(Path.GetExtension(old.Path),StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Replacement must have the same file type.");
            int next=Artworks(old.JobId,true).Where(f=>f.LogicalId==old.LogicalId).Max(f=>f.Revision)+1;
            int qty=Designs(item.Id).Single(v=>v.Number==old.Version).Quantity;
            return CreateRevision(item,replacement,old.LogicalId,old.Version,next,old.Component,qty,false,old,"Artwork replaced");
        }
    }
    public Artwork Restore(string historicalId)
    {
        lock(gate)
        {
            var prior=Artwork(historicalId); string source=Full(prior.Path);
            if(!File.Exists(source) || Safety.Hash(source)!=prior.Hash) throw new IOException("The archived revision is missing or has changed. It was not restored.");
            var all=Artworks(prior.JobId,true).Where(f=>f.LogicalId==prior.LogicalId).ToList(); var current=all.SingleOrDefault(f=>f.Active);
            if(current?.Id==prior.Id) throw new InvalidOperationException("This revision is already active.");
            if(current!=null && Verify(current.Id)!="Verified") throw new InvalidOperationException("The current revision needs review before it can be archived.");
            int next=all.Max(f=>f.Revision)+1; int qty=Designs(prior.ItemId).Single(v=>v.Number==prior.Version).Quantity;
            return CreateRevision(Item(prior.ItemId),source,prior.LogicalId,prior.Version,next,prior.Component,qty,false,current??prior,$"Historical revision restored from R{prior.Revision:00}");
        }
    }
    public void UndoAssignment(string id)
    {
        lock(gate)
        {
            var f=Artwork(id); if(!f.Active) throw new InvalidOperationException("Artwork is already inactive.");
            if(Verify(id)!="Verified") throw new InvalidOperationException("Review or relink the changed file first.");
            string archive=Full(Path.Combine(Job(f.JobId).Folder,"previous versions",f.Id[..8]+"-unassigned-"+f.Filename));
            File.Copy(Full(f.Path),archive,false);
            db.Atomic(()=> { db.Execute("UPDATE files SET active=0,ready=0,path=$0 WHERE id=$1",Relative(archive),id); Log(f.JobId,"Assignment undone",f.Filename+". File and submission history retained."); });
            try { File.Delete(Full(f.Path)); } catch(Exception e) { Log(f.JobId,"Cleanup warning",e.Message); }
        }
    }
    public void Relink(string id,string selectedPath)
    {
        lock(gate)
        {
            var f=Artwork(id); ValidateSource(selectedPath,Item(f.ItemId));
            if(Safety.Hash(selectedPath)!=f.Hash) throw new InvalidOperationException("The chosen file does not match the expected fingerprint. Use Replace Artwork for new content.");
            if(Path.GetExtension(selectedPath).Equals(".pdf",StringComparison.OrdinalIgnoreCase) && PdfTools.ReadIdentity(selectedPath)?.PhysicalRevisionID!=id)
                throw new InvalidOperationException("The selected PDF does not contain this revision identity.");
            db.Execute("UPDATE files SET path=$0,integrity='Verified',ready=0 WHERE id=$1",Relative(selectedPath),id);
            Log(f.JobId,"Artwork relinked",$"{f.Path} -> {Relative(selectedPath)}. No other copy was deleted. Readiness cleared.");
        }
    }
    private void WriteReplacementLog(int jobId)
    {
        try
        {
            var records=History(jobId).Where(a=>a.Action.Contains("replaced")||a.Action.Contains("restored")).Reverse();
            File.WriteAllText(Full(Path.Combine(Job(jobId).Folder,"previous versions","replacement-log.txt")),string.Join(Environment.NewLine+Environment.NewLine,records.Select(a=>$"{a.Time} | {Environment.UserName} | {a.Action}\n{a.Detail}")));
        }
        catch(Exception e) { Log(jobId,"Replacement text log needs repair",e.Message+". Authoritative history remains in the database."); }
    }
    public void SimulateModification(string id)
    {
        lock(gate)
        {
            var f=Artwork(id); if(!f.Active || Path.GetExtension(f.Path)!=".pdf") throw new InvalidOperationException("Select an active demonstration PDF.");
            File.AppendAllText(Full(f.Path),"\n% Explicit demo modification "+DateTimeOffset.UtcNow.ToString("O")+"\n"); Verify(id);
        }
    }
}

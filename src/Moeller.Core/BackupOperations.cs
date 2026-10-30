using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Moeller.Core;

public sealed partial class Engine
{
    public string ExportDemo(string? destination=null)
    {
        lock(gate)
        {
            string exports=Path.Combine(Home,"Exports"); Directory.CreateDirectory(exports);
            destination??=Path.Combine(exports,"Moeller-Demo-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]+".zip");
            destination=Path.GetFullPath(destination);
            if(destination.StartsWith(Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Save the export outside the active workspace.");
            if(File.Exists(destination)) throw new IOException("That export file already exists. Choose a new name.");
            string stage=Path.Combine(Home,"Export-Staging-"+Guid.NewGuid().ToString("N")); Safety.Within(Home,stage); Directory.CreateDirectory(stage);
            try
            {
                Directory.CreateDirectory(Path.Combine(stage,"Database"));
                db.Backup(Path.Combine(stage,"Database","demo.sqlite3"));
                foreach(string file in Safety.Files(Root))
                {
                    string relative=Path.GetRelativePath(Root,file);
                    if(relative.StartsWith("Database"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || relative.StartsWith(".staging"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) continue;
                    string target=Safety.Within(stage,Path.Combine(stage,relative)); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    string expected=Safety.Hash(file);
                    using(var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))
                    using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)) input.CopyTo(output);
                    if(Safety.Hash(target)!=expected) throw new IOException("Artwork changed during export. The incomplete ZIP was not delivered.");
                }
                File.WriteAllText(Path.Combine(stage,"EXPORT-README.txt"),"MOELLER FILE ASSISTANT / OFFLINE DEMO EXPORT\nThis ZIP contains a consistent SQLite backup, fictional artwork, production revisions, history, and simulated output folders. Paths in the database are relative to Workspace. This is a state export, not an installer. Do not merge it over a running database. No Moeller production systems were accessed.\nVersion: "+Version);
                var manifest=Safety.Files(stage).ToDictionary(f=>Path.GetRelativePath(stage,f).Replace('\\','/'),Safety.Hash);
                File.WriteAllText(Path.Combine(stage,"EXPORT-SHA256.json"),Safety.Json(manifest));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                string pending=destination+".partial";
                if(File.Exists(pending)) throw new IOException("An incomplete export already uses this name.");
                ZipFile.CreateFromDirectory(stage,pending,CompressionLevel.Optimal,false);
                using(var zip=ZipFile.OpenRead(pending))
                {
                    if(zip.Entries.Count!=manifest.Count+1 || zip.GetEntry("Database/demo.sqlite3")==null) throw new IOException("Export ZIP verification failed.");
                    foreach(var entry in zip.Entries.Where(e=>e.Name!="EXPORT-SHA256.json"))
                    {
                        using var stream=entry.Open(); string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
                        if(!manifest.TryGetValue(entry.FullName,out string? expected)||hash!=expected) throw new IOException("Export entry failed its checksum: "+entry.FullName);
                    }
                }
                File.Move(pending,destination,false); Log(null,"Demo ZIP exported",$"Verified ZIP with {manifest.Count} files and a consistent database snapshot.");
                return destination;
            }
            finally { if(Directory.Exists(stage)) Directory.Delete(stage,true); }
        }
    }
    public string ExportDiagnostics(string? destination=null)
    {
        lock(gate)
        {
            string exports=Path.Combine(Home,"Exports"); Directory.CreateDirectory(exports);
            destination??=Path.Combine(exports,"Moeller-Diagnostics-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip");
            if(File.Exists(destination)) throw new IOException("A diagnostic ZIP with this name already exists.");
            string stage=Path.Combine(Home,"Diagnostics-Staging-"+Guid.NewGuid().ToString("N")); Safety.Within(Home,stage); Directory.CreateDirectory(stage);
            try
            {
                string Clean(string text)=>text.Replace(Home,"%DEMO_HOME%",StringComparison.OrdinalIgnoreCase).Replace(Environment.UserName,"%USER%",StringComparison.OrdinalIgnoreCase);
                File.WriteAllText(Path.Combine(stage,"diagnostics.json"),Clean(Safety.Json(new { Version,OS=Environment.OSVersion.ToString(),Architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),Checks=Diagnostics(),History=Jobs().SelectMany(j=>History(j.Id).Where(a=>a.Action.Contains("error",StringComparison.OrdinalIgnoreCase)||a.Action.Contains("Failed"))).Take(200).ToList() })));
                File.WriteAllText(Path.Combine(stage,"README.txt"),"Sanitized diagnostics only. Artwork and the working database are not included.");
                ZipFile.CreateFromDirectory(stage,destination); return destination;
            }
            finally { Directory.Delete(stage,true); }
        }
    }
    public string ResetDemo()
    {
        lock(gate)
        {
            string export=ExportDemo();
            string staging=Path.Combine(Home,"Reset-Staging-"+Guid.NewGuid().ToString("N"));
            using(var baseline=new Engine(staging)) { if(baseline.Jobs().Count!=16||baseline.Diagnostics().Any(c=>!c.Passed)) throw new IOException("New baseline did not validate. Current demo was not reset."); }
            string archive=Path.Combine(Home,"Archived Resets",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);
            Safety.Within(Home,archive); Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
            db.Dispose(); bool moved=false;
            try
            {
                Directory.Move(Root,archive); moved=true; Directory.Move(staging,Root); db=new Store(DatabasePath);
                Log(null,"Demo reset","Prior state is retained in an archive folder and a checksum-verified ZIP. "+Path.GetFileName(export));
            }
            catch
            {
                if(moved&&!Directory.Exists(Root)) Directory.Move(archive,Root);
                db=new Store(DatabasePath); throw;
            }
            return export;
        }
    }
}

using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Moeller.Core;

public sealed partial class Engine : IDisposable
{
    public const string Version = "0.2.0";
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Moeller", "FileAssistantDemo", "Workspace");
    public string Root { get; }
    public string Home => Directory.GetParent(Root)!.FullName;
    public string DatabasePath => Path.Combine(Root, "Database", "demo.sqlite3");
    public string PrintShare => Full(Path.Combine("Simulated Outputs", "PrintShare", "incoming"));
    public string ApogeeSamples => Full(Path.Combine("Simulated Outputs", "Apogee Samples"));
    public string? FailNextDestination { get; set; }
    private readonly object gate = new();
    private Store db;
    private bool disposed;

    public Engine(string? root = null)
    {
        Root = Path.GetFullPath(root ?? DefaultRoot);
        Safety.Within(Root, Root, true);
        bool newDatabase = !File.Exists(DatabasePath);
        if (newDatabase && Directory.Exists(Root) && Directory.EnumerateFileSystemEntries(Root).Any())
            throw new IOException("A workspace exists but its database is missing. It has not been recreated or overwritten. Preserve the folder and use a separate clean demo workspace.");
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        db = new Store(DatabasePath);
        try
        {
            if (newDatabase)
            {
                File.WriteAllText(Full("DEMO-WORKSPACE.json"), Safety.Json(new { Product = "MoellerFileAssistantDemo", Schema = 2, Version, Offline = true }));
                foreach (string relative in new[] { "Logs", "Recovery", ".staging", "Simulated Outputs/PrintShare/incoming", "Simulated Outputs/PrintShare/history", "Simulated Outputs/Apogee Samples" }) Directory.CreateDirectory(Full(relative.Replace('/', Path.DirectorySeparatorChar)));
                Seed(); Set("baseline", "2"); Set("app-version", Version);
            }
            if (Get("baseline") != "2") throw new IOException("Demo initialization is incomplete. Existing files and database were preserved for diagnosis.");
            RecoverPending();
            if (Get("app-version") != Version) ResetDemo();
        }
        catch { db.Dispose(); throw; }
    }
    public string Full(string relative) => Safety.Within(Root, Path.Combine(Root, relative));
    public string Relative(string absolute) => Path.GetRelativePath(Root, Safety.Within(Root, absolute));
    public string Get(string key, string fallback = "") { lock (gate) { string s = db.Scalar("SELECT value FROM settings WHERE key=$0", key); return s == "" ? fallback : s; } }
    public void Set(string key, string value) { lock (gate) db.Execute("INSERT INTO settings VALUES($0,$1) ON CONFLICT(key) DO UPDATE SET value=excluded.value", key, value); }
    public List<Job> Jobs() { lock (gate) return db.Query("SELECT id,number,customer,title,scenario,folder FROM jobs ORDER BY id", r => new Job(r.GetInt32(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5))); }
    public Job Job(int id) => Jobs().Single(j => j.Id == id);
    public List<Item> Items(int jobId) { lock (gate) return db.Query("SELECT id,job_id,number,description,quantity,components FROM items WHERE job_id=$0 ORDER BY number", r => new Item(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetInt32(4),r.GetString(5)),jobId); }
    public Item Item(int id) { lock (gate) return db.Query("SELECT id,job_id,number,description,quantity,components FROM items WHERE id=$0", r => new Item(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetInt32(4),r.GetString(5)),id).Single(); }
    public List<Design> Designs(int itemId) { lock (gate) return db.Query("SELECT id,item_id,number,quantity FROM designs WHERE item_id=$0 ORDER BY number", r => new Design(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2),r.GetInt32(3)),itemId); }
    private static Artwork ReadFile(SqliteDataReader r) => new(r.GetString(0),r.GetString(1),r.GetInt32(2),r.GetInt32(3),r.GetInt32(4),r.GetInt32(5),r.GetString(6),r.GetString(7),r.GetString(8),r.GetString(9),r.GetString(10),r.GetBoolean(11),r.GetString(12),r.GetBoolean(13));
    private const string FileColumns = "id,logical_id,job_id,item_id,version,revision,component,source,path,source_hash,hash,ready,integrity,active";
    public List<Artwork> Artworks(int jobId, bool history = false) { lock (gate) return db.Query($"SELECT {FileColumns} FROM files WHERE job_id=$0 " + (history ? "" : "AND active=1 ") + "ORDER BY item_id,version,component,revision", ReadFile,jobId); }
    public Artwork Artwork(string id) { lock (gate) return db.Query($"SELECT {FileColumns} FROM files WHERE id=$0", ReadFile,id).Single(); }
    public List<Activity> History(int jobId) { lock (gate) return db.Query("SELECT created,action,detail FROM activity WHERE job_id=$0 OR job_id IS NULL ORDER BY created DESC LIMIT 1000", r => new Activity(r.GetString(0),r.GetString(1),r.GetString(2)),jobId); }
    public List<Submission> Submissions(int jobId) { lock (gate) return db.Query("SELECT id,job_id,created,scope,directory FROM submissions WHERE job_id=$0 ORDER BY created DESC", r => new Submission(r.GetString(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetString(4)),jobId); }
    public List<Delivery> Deliveries(string id) { lock (gate) return db.Query("SELECT submission_id,destination,status,detail FROM deliveries WHERE submission_id=$0 ORDER BY destination", r=>new Delivery(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3)),id); }
    public bool WasSubmitted(string fileId) { lock (gate) return db.Scalar("SELECT count(*) FROM submission_files WHERE file_id=$0",fileId) != "0"; }
    public List<string> Sources(int jobId)
    {
        lock (gate)
        {
            string folder = Full(Job(jobId).Folder); var result = new List<string>();
            foreach (string d in new[] { folder, Path.Combine(folder,"provided") })
                if (Directory.Exists(d)) foreach (var p in Directory.EnumerateFiles(d).Where(p => new[]{".pdf",".zip"}.Contains(Path.GetExtension(p).ToLowerInvariant()))) { Safety.Within(Root,p); result.Add(p); }
            return result.OrderBy(p=>Path.GetFileName(p)).ToList();
        }
    }
    public Job? JobForPath(string path)
    {
        string p = Safety.Within(Root,path);
        return Jobs().FirstOrDefault(j =>
        {
            string f = Full(j.Folder); if (!p.StartsWith(f + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) return false;
            return Path.GetRelativePath(f,p).Split(Path.DirectorySeparatorChar).Length <= 2;
        });
    }
    public void SaveDesign(int itemId, int number, int quantity)
    {
        lock (gate)
        {
            if (number<1 || number>9999 || quantity<1 || quantity>100000000) throw new ArgumentException("Enter a positive version number and quantity.");
            Item item=Item(itemId); db.Execute("INSERT INTO designs(item_id,number,quantity) VALUES($0,$1,$2) ON CONFLICT(item_id,number) DO UPDATE SET quantity=excluded.quantity",itemId,number,quantity);
            db.Execute("UPDATE files SET ready=0 WHERE item_id=$0 AND version=$1 AND active=1",itemId,number);
            Log(item.JobId,"Quantity allocation",$"Item {item.Number} V{number:00}: {quantity:N0}. Ordered quantity remains {item.Quantity:N0}. Readiness cleared for this design.");
        }
    }
    public string QuantityWarnings(int jobId)
    {
        lock (gate) return string.Join(Environment.NewLine,Items(jobId).Select(i => new { Item=i, Allocated=Designs(i.Id).Sum(v => (long)v.Quantity) }).Where(x=>x.Allocated!=x.Item.Quantity)
            .Select(x=>$"Item {x.Item.Number}: ordered {x.Item.Quantity:N0}; allocated {x.Allocated:N0}; difference {x.Allocated-x.Item.Quantity:+#,##0;-#,##0;0}."));
    }
    public string Verify(string id)
    {
        lock (gate)
        {
            var file=Artwork(id); string state;
            try
            {
                string path=Full(file.Path);
                state=!File.Exists(path)?"Missing":Safety.Hash(path)!=file.Hash?"Modified":"Verified";
                if(state=="Verified" && Path.GetExtension(path).Equals(".pdf",StringComparison.OrdinalIgnoreCase))
                {
                    var identity=PdfTools.ReadIdentity(path);
                    if(identity?.PhysicalRevisionID!=file.Id || identity.FileID!=file.LogicalId) state="Identity missing";
                }
            }
            catch(Exception e) { state="Needs review"; Log(file.JobId,"Verification error",e.Message); }
            if(state!=file.Integrity)
            {
                db.Execute("UPDATE files SET integrity=$0,ready=CASE WHEN $0='Verified' THEN ready ELSE 0 END WHERE id=$1",state,id);
                Log(file.JobId,"Integrity changed",$"{file.Filename}: {state}. Expected fingerprint was not changed.");
            }
            return state;
        }
    }
    public void VerifyJob(int jobId) { lock(gate) foreach(var f in Artworks(jobId)) Verify(f.Id); }
    public void MarkReady(IEnumerable<string> ids, bool ready=true)
    {
        lock(gate)
        {
            var files=ids.Distinct().Select(Artwork).ToList();
            foreach(var f in files) if(!f.Active || (ready && Verify(f.Id)!="Verified")) throw new InvalidOperationException($"{f.Filename} is not an intact active revision.");
            db.Atomic(()=> { foreach(var f in files) { db.Execute("UPDATE files SET ready=$0 WHERE id=$1",ready?1:0,f.Id); Log(f.JobId,ready?"Marked Ready":"Approval cleared",f.Filename); } });
        }
    }
    public List<Diagnostic> Diagnostics()
    {
        lock(gate)
        {
            var result=new List<Diagnostic>{new("Database integrity",db.Scalar("PRAGMA integrity_check")=="ok",DatabasePath),new("Fictional jobs",Jobs().Count==16,$"{Jobs().Count} of 16"),new("Offline routing",true,"Only local workspace destinations are implemented. No network clients or credentials."),new("PDF identity library",true,typeof(PdfTools).Assembly.GetName().Version?.ToString()??Version)};
            foreach(var p in new[]{PrintShare,ApogeeSamples,Full("archive"),Full("Logs")})
            {
                bool ok=false; string text=p;
                try { Safety.Within(Root,p); ok=Directory.Exists(p); } catch(Exception e){text=e.Message;}
                result.Add(new Diagnostic("Workspace folder",ok,text));
            }
            result.Add(new Diagnostic("Interrupted operations",db.Scalar("SELECT count(*) FROM operations WHERE state='Pending'")=="0","Incomplete operations are quarantined, not silently accepted."));
            return result;
        }
    }
    private void Log(int? jobId,string action,string detail)
    {
        string now=DateTimeOffset.UtcNow.ToString("O"),id=Guid.NewGuid().ToString("N");
        db.Execute("INSERT INTO activity(id,created,job_id,action,detail) VALUES($0,$1,$2,$3,$4)",id,now,jobId,action,detail);
    }
    private void RecoverPending()
    {
        foreach(var text in db.Query("SELECT payload FROM operations WHERE state='Pending'",r=>r.GetString(0)))
        {
            var op=JsonSerializer.Deserialize<PendingOperation>(text)!;
            if(db.Scalar("SELECT count(*) FROM files WHERE id=$0",op.FileId)=="1")
            {
                Verify(op.FileId); db.Execute("UPDATE operations SET state='Recovered',detail='Database revision recovered and verified' WHERE id=$0",op.Id); continue;
            }
            foreach(string relative in new[]{op.Target,op.Stage})
            {
                string p=Full(relative);
                if(File.Exists(p)) File.Move(p,Full(Path.Combine("Recovery",op.Id+"-"+Path.GetFileName(p))),false);
            }
            db.Execute("UPDATE operations SET state='Quarantined',detail='Uncommitted files preserved under Recovery' WHERE id=$0",op.Id);
            Log(null,"Interrupted operation recovered",op.Kind+": uncommitted files were preserved under Recovery.");
        }
    }
    public void Dispose() { lock(gate) { if(!disposed) { db.Dispose(); disposed=true; } } }
}

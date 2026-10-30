using Microsoft.Data.Sqlite;

namespace Moeller.Core;

internal sealed class Store : IDisposable
{
    private readonly SqliteConnection connection;
    private SqliteTransaction? transaction;
    public Store(string path)
    {
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true, DefaultTimeout = 15 }.ToString());
        connection.Open();
        Execute("PRAGMA journal_mode=WAL;");
        Execute("PRAGMA synchronous=FULL;");
        if (Scalar("PRAGMA integrity_check;") != "ok") throw new IOException("The demo database failed its integrity check. Existing data was preserved.");
        Execute("""
        CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS jobs(id INTEGER PRIMARY KEY, number TEXT NOT NULL UNIQUE, customer TEXT NOT NULL, title TEXT NOT NULL, scenario TEXT NOT NULL, folder TEXT NOT NULL UNIQUE);
        CREATE TABLE IF NOT EXISTS items(id INTEGER PRIMARY KEY, job_id INTEGER NOT NULL REFERENCES jobs(id), number TEXT NOT NULL, description TEXT NOT NULL, quantity INTEGER NOT NULL CHECK(quantity>0), components TEXT NOT NULL, UNIQUE(job_id,number));
        CREATE TABLE IF NOT EXISTS designs(id INTEGER PRIMARY KEY, item_id INTEGER NOT NULL REFERENCES items(id), number INTEGER NOT NULL CHECK(number>0), quantity INTEGER NOT NULL CHECK(quantity>0), UNIQUE(item_id,number));
        CREATE TABLE IF NOT EXISTS files(id TEXT PRIMARY KEY, logical_id TEXT NOT NULL, job_id INTEGER NOT NULL REFERENCES jobs(id), item_id INTEGER NOT NULL REFERENCES items(id), version INTEGER NOT NULL, revision INTEGER NOT NULL, component TEXT NOT NULL, source TEXT NOT NULL, path TEXT NOT NULL UNIQUE, source_hash TEXT NOT NULL, hash TEXT NOT NULL, ready INTEGER NOT NULL DEFAULT 0, integrity TEXT NOT NULL, active INTEGER NOT NULL DEFAULT 1);
        CREATE UNIQUE INDEX IF NOT EXISTS one_active_revision ON files(logical_id) WHERE active=1;
        CREATE INDEX IF NOT EXISTS files_job ON files(job_id,active);
        CREATE TABLE IF NOT EXISTS submissions(id TEXT PRIMARY KEY, job_id INTEGER NOT NULL REFERENCES jobs(id), created TEXT NOT NULL, scope TEXT NOT NULL, directory TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS submission_files(submission_id TEXT NOT NULL REFERENCES submissions(id), file_id TEXT NOT NULL REFERENCES files(id), PRIMARY KEY(submission_id,file_id));
        CREATE TABLE IF NOT EXISTS deliveries(submission_id TEXT NOT NULL REFERENCES submissions(id), destination TEXT NOT NULL, status TEXT NOT NULL, detail TEXT NOT NULL, PRIMARY KEY(submission_id,destination));
        CREATE TABLE IF NOT EXISTS activity(id TEXT PRIMARY KEY, created TEXT NOT NULL, job_id INTEGER, action TEXT NOT NULL, detail TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS activity_job ON activity(job_id,created);
        CREATE TABLE IF NOT EXISTS operations(id TEXT PRIMARY KEY, payload TEXT NOT NULL, state TEXT NOT NULL, detail TEXT NOT NULL);
        """);
    }
    private SqliteCommand Command(string sql, object?[] values)
    {
        var cmd = connection.CreateCommand(); cmd.CommandText = sql; cmd.Transaction = transaction;
        for (int i = 0; i < values.Length; i++) cmd.Parameters.AddWithValue("$" + i, values[i] ?? DBNull.Value);
        return cmd;
    }
    public int Execute(string sql, params object?[] values) { using var c = Command(sql, values); return c.ExecuteNonQuery(); }
    public string Scalar(string sql, params object?[] values) { using var c = Command(sql, values); return Convert.ToString(c.ExecuteScalar()) ?? ""; }
    public List<T> Query<T>(string sql, Func<SqliteDataReader,T> map, params object?[] values)
    {
        using var c = Command(sql, values); using var r = c.ExecuteReader(); var result = new List<T>();
        while (r.Read()) result.Add(map(r)); return result;
    }
    public void Atomic(Action action)
    {
        if (transaction != null) { action(); return; }
        using var tx = connection.BeginTransaction(); transaction = tx;
        try { action(); tx.Commit(); } catch { tx.Rollback(); throw; } finally { transaction = null; }
    }
    public void Backup(string destination)
    {
        if (File.Exists(destination)) throw new IOException("Backup destination already exists.");
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
        target.Open(); connection.BackupDatabase(target);
        using var check = target.CreateCommand(); check.CommandText = "PRAGMA integrity_check;";
        if (Convert.ToString(check.ExecuteScalar()) != "ok") throw new IOException("Database backup did not pass its integrity check.");
    }
    public void Dispose() => connection.Dispose();
}

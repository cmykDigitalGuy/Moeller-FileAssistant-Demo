using System.Security.Cryptography;
using System.Text.Json;

namespace Moeller.Core;

public record Job(int Id, string Number, string Customer, string Title, string Scenario, string Folder)
{
    public string Display => $"{Number}   {Title}";
    public override string ToString() => Display;
}
public record Item(int Id, int JobId, string Number, string Description, int Quantity, string Components)
{
    public string Display => $"{Number} - {Description}  /  {Quantity:N0}";
    public override string ToString() => Display;
}
public record Design(int Id, int ItemId, int Number, int Quantity);
public record Artwork(string Id, string LogicalId, int JobId, int ItemId, int Version,
    int Revision, string Component, string Source, string Path, string SourceHash,
    string Hash, bool Ready, string Integrity, bool Active)
{
    public string Filename => System.IO.Path.GetFileName(Path);
    public string VersionText => $"V{Version:00} / R{Revision:00}";
    public string State => Integrity != "Verified" ? Integrity : Ready ? "Ready" : "Needs approval";
}
public record Activity(string Time, string Action, string Detail);
public record Submission(string Id, int JobId, string Time, string Scope, string Directory);
public record Delivery(string SubmissionId, string Destination, string Status, string Detail);
public record Assignment(string Source, int ItemId, int Version, int Quantity, string Component, bool Ready);
public record Identity(string FileID, string PhysicalRevisionID, string JobNumber, string ItemNumber,
    int ArtworkVersion, int Revision, string Component, string SourceSHA256);
public record PendingOperation(string Id, string Target, string Stage, string FileId, string Kind);
public record Diagnostic(string Name, bool Passed, string Detail);

public static class Names
{
    public static string Safe(string value)
    {
        const string invalid = "<>:\"/\\|?*";
        var text = new string(value.Select(c => char.IsControl(c) || invalid.Contains(c) ? '-' : c).ToArray()).Trim().Trim('.');
        if (text.Length > 65) text = text[..65].Trim().Trim('.');
        if (string.IsNullOrWhiteSpace(text)) text = "Artwork";
        string stem = text.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem)) text = "_" + text;
        return text;
    }
    public static string Production(string job, string item, int version, int revision, string description, string component, string extension)
    {
        if (version < 1 || revision < 1 || version > 9999 || revision > 9999) throw new ArgumentOutOfRangeException(nameof(version));
        string role = component == "Artwork" ? "" : "-" + Safe(component);
        return $"{Safe(job)}-{Safe(item)}-V{version:00}-R{revision:00}-{Safe(description)}{role}{extension.ToLowerInvariant()}";
    }
}

public static class Safety
{
    // The application only operates in its own local workspace. Existing junctions,
    // symlinks and remote roots are refused. This is not a sandbox for hostile users
    // able to concurrently rewrite another process's filesystem namespace.
    public static string Within(string root, string path, bool allowRoot = false)
    {
        string r = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        string p = System.IO.Path.GetFullPath(path);
        if (!(allowRoot && p.Equals(r, StringComparison.OrdinalIgnoreCase)) &&
            !p.StartsWith(r + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("This file is outside the Moeller demonstration workspace. No changes were made.");
        if (OperatingSystem.IsWindows())
        {
            if (p.StartsWith(@"\\")) throw new UnauthorizedAccessException("Network paths are not permitted in this offline demo.");
            var drive = new DriveInfo(System.IO.Path.GetPathRoot(p)!);
            if (drive.DriveType == DriveType.Network) throw new UnauthorizedAccessException("Mapped network drives are not permitted.");
        }
        string? check = p;
        while (check != null)
        {
            if ((File.Exists(check) || Directory.Exists(check)) && (File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Links and junctions are not allowed in the demo workspace.");
            check = System.IO.Directory.GetParent(check)?.FullName;
        }
        return p;
    }
    public static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    public static IEnumerable<string> Files(string root)
    {
        var queue = new Stack<string>(); queue.Push(root);
        while (queue.Count > 0)
        {
            string folder = queue.Pop(); Within(root, folder, true);
            foreach (var f in Directory.EnumerateFiles(folder)) { Within(root, f); yield return f; }
            foreach (var d in Directory.EnumerateDirectories(folder)) { Within(root, d); queue.Push(d); }
        }
    }
    public static string Json<T>(T value) => JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
}

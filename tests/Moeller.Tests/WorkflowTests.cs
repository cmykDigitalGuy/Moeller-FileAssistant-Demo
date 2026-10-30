using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Moeller.Core;
using PdfSharp.Pdf.IO;
using Xunit;

namespace Moeller.Tests;

public sealed class WorkflowTests : IDisposable
{
    private readonly string home=Path.Combine(Path.GetTempPath(),"Moeller-Automated-Test-"+Guid.NewGuid().ToString("N"));
    private Engine engine;
    public WorkflowTests() { engine=new Engine(Path.Combine(home,"Workspace")); }
    public void Dispose() { engine.Dispose(); if(Directory.Exists(home)) Directory.Delete(home,true); }
    private Artwork Card(bool ready=false)=>engine.Assign(new Assignment(engine.Sources(1).Single(),engine.Items(1)[0].Id,1,500,"Artwork",ready));

    [Fact] public void BaselineHasSixteenRealJobsAndRequiredFolders()
    {
        Assert.Equal(16,engine.Jobs().Count); Assert.Equal(4,engine.Items(16).Count);
        foreach(var job in engine.Jobs()) foreach(string folder in new[]{"provided","production","previous versions","submissions"}) Assert.True(Directory.Exists(engine.Full(Path.Combine(job.Folder,folder))));
        Assert.Empty(engine.Sources(9)); Assert.All(engine.Diagnostics(),d=>Assert.True(d.Passed,d.Detail));
    }
    [Fact] public void AllBaselinePdfsAreReadableWithPages()
    {
        foreach(var file in engine.Jobs().SelectMany(j=>engine.Sources(j.Id)).Where(p=>Path.GetExtension(p)==".pdf")) { using var doc=PdfReader.Open(file,PdfDocumentOpenMode.ReadOnly); Assert.True(doc.PageCount>0,file); }
    }
    [Fact] public void AssignmentPreservesOriginalAndStampsReadableIdentity()
    {
        var source=engine.Sources(1).Single(); string hash=Safety.Hash(source); var f=Card();
        Assert.Equal(hash,Safety.Hash(source)); Assert.Equal(hash,f.SourceHash); Assert.Equal(f.Hash,Safety.Hash(engine.Full(f.Path)));
        var identity=PdfTools.ReadIdentity(engine.Full(f.Path)); Assert.NotNull(identity); Assert.Equal(f.Id,identity.PhysicalRevisionID); Assert.Equal(f.LogicalId,identity.FileID); Assert.False(f.Ready);
    }
    [Fact] public void ReadyIsExplicitAndPersists() { var f=Card(); engine.MarkReady([f.Id]); Assert.True(engine.Artwork(f.Id).Ready); }
    [Fact] public void UnapprovedArtworkCannotSubmit() { var f=Card(); Assert.Throws<InvalidOperationException>(()=>engine.Submit(1,[f.Id])); }
    [Fact] public void DuplicateAssignmentNeverOverwrites()
    {
        var f=Card(); string hash=Safety.Hash(engine.Full(f.Path)); Assert.Throws<InvalidOperationException>(()=>Card()); Assert.Equal(hash,Safety.Hash(engine.Full(f.Path))); Assert.Single(engine.Artworks(1));
    }
    [Fact] public void CrossJobSelectionIsRejected()
    {
        Assert.Throws<InvalidOperationException>(()=>engine.Assign(new Assignment(engine.Sources(1).Single(),engine.Items(2)[0].Id,1,100,"Artwork",false)));
    }
    [Fact] public void ExternalFilesAndPrefixSiblingAreRejected()
    {
        string outside=Path.Combine(home,"external.pdf"); File.WriteAllText(outside,"not demo");
        Assert.Throws<UnauthorizedAccessException>(()=>engine.Assign(new Assignment(outside,engine.Items(1)[0].Id,1,500,"Artwork",false)));
        Assert.Throws<UnauthorizedAccessException>(()=>Safety.Within(engine.Root,engine.Root+"-other/file.pdf"));
    }
    [Fact] public void JunctionEscapeIsRejected()
    {
        if(!OperatingSystem.IsWindows()) return;
        string outside=Path.Combine(home,"Outside"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside,"file.pdf"),"not demo");
        string link=engine.Full("JunctionTest");
        var p=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",$"/c mklink /J \"{link}\" \"{outside}\""){UseShellExecute=false,CreateNoWindow=true}); p!.WaitForExit(); Assert.Equal(0,p.ExitCode);
        try { Assert.Throws<UnauthorizedAccessException>(()=>Safety.Within(engine.Root,Path.Combine(link,"file.pdf"))); } finally { Directory.Delete(link); }
    }
    [Fact] public void FiveDesignsRemainDistinctVersions()
    {
        var item=engine.Items(2)[0]; int v=1;
        foreach(var source in engine.Sources(2)) engine.Assign(new Assignment(source,item.Id,v++,100,"Artwork",true));
        Assert.Equal(5,engine.Artworks(2).Count); Assert.Equal(5,engine.Artworks(2).Select(f=>f.Version).Distinct().Count()); Assert.Equal("",engine.Completeness(2));
    }
    [Fact] public void CombinedBookletUsesOnePhysicalFileForTwoComponents()
    {
        var f=engine.Assign(new Assignment(engine.Sources(6).Single(),engine.Items(6)[0].Id,1,400,"Cover + Text",true)); Assert.Single(engine.Artworks(6)); Assert.Equal("",engine.Completeness(6)); Assert.Equal("Cover + Text",PdfTools.ReadIdentity(engine.Full(f.Path))!.Component);
    }
    [Fact] public void PartialBookletCannotClaimComplete()
    {
        var f=engine.Assign(new Assignment(engine.Sources(5).First(p=>p.Contains("Cover")),engine.Items(5)[0].Id,1,300,"Cover",true));
        Assert.Contains("Text",engine.Completeness(5)); Assert.Throws<InvalidOperationException>(()=>engine.Submit(5,[f.Id],true));
        var sub=engine.Submit(5,[f.Id]); Assert.Equal("Selected artwork",sub.Scope);
    }
    [Fact] public void QuantityMismatchRequiresAcknowledgmentAndDoesNotChangeOrder()
    {
        var item=engine.Items(1)[0]; engine.SaveDesign(item.Id,1,400);
        var f=engine.Assign(new Assignment(engine.Sources(1).Single(),item.Id,1,400,"Artwork",true));
        Assert.Throws<InvalidOperationException>(()=>engine.Submit(1,[f.Id])); engine.Submit(1,[f.Id],false,true); Assert.Equal(500,engine.Items(1)[0].Quantity);
    }
    [Fact] public void ChangingAllocationClearsApproval() { var f=Card(true); engine.SaveDesign(f.ItemId,1,400); Assert.False(engine.Artwork(f.Id).Ready); }
    [Fact] public void ModificationClearsReadyWithoutReplacingExpectedHash()
    {
        var f=Card(true); engine.SimulateModification(f.Id); var now=engine.Artwork(f.Id); Assert.False(now.Ready); Assert.Equal("Modified",now.Integrity); Assert.Equal(f.Hash,now.Hash); Assert.Throws<InvalidOperationException>(()=>engine.MarkReady([f.Id]));
    }
    [Fact] public void MissingFileDoesNotReappearOnRefresh()
    {
        var f=Card(true); File.Delete(engine.Full(f.Path)); engine.VerifyJob(1); Assert.Equal("Missing",engine.Artwork(f.Id).Integrity); Assert.False(File.Exists(engine.Full(f.Path)));
    }
    [Fact] public void ReplacementArchivesAndClearsApproval()
    {
        var old=engine.Artworks(14).Single(); var original=engine.Sources(14).First(p=>p.Contains("Replacement")); string hash=Safety.Hash(original);
        var newer=engine.Replace(old.Id,original); Assert.Equal(2,newer.Revision); Assert.False(newer.Ready); Assert.Equal(old.LogicalId,newer.LogicalId); Assert.NotEqual(old.Id,newer.Id);
        Assert.False(engine.Artwork(old.Id).Active); Assert.True(File.Exists(engine.Full(engine.Artwork(old.Id).Path))); Assert.Equal(hash,Safety.Hash(original));
        Assert.True(File.Exists(engine.Full(Path.Combine(engine.Job(14).Folder,"previous versions","replacement-log.txt"))));
    }
    [Fact] public void RestoringPriorRevisionCreatesNewChronologicalRevision()
    {
        var old=engine.Artworks(14).Single(); engine.Replace(old.Id,engine.Sources(14).First(p=>p.Contains("Replacement")));
        var restored=engine.Restore(old.Id); Assert.Equal(3,restored.Revision); Assert.False(restored.Ready); Assert.Equal(3,engine.Artworks(14,true).Count); Assert.Single(engine.Artworks(14));
    }
    [Fact] public void ReviewModifiedContentCreatesNewRevision()
    {
        var old=engine.Artworks(10).Single(); Assert.Equal("Modified",old.Integrity);
        var reviewed=engine.Replace(old.Id,engine.Full(old.Path),true); Assert.Equal(2,reviewed.Revision); Assert.Equal("Verified",reviewed.Integrity); Assert.False(reviewed.Ready);
    }
    [Fact] public void UndoKeepsHistoryAndFiles()
    {
        var f=Card(); engine.UndoAssignment(f.Id); Assert.Empty(engine.Artworks(1)); Assert.True(File.Exists(engine.Full(engine.Artwork(f.Id).Path))); Assert.Contains(engine.History(1),a=>a.Action=="Assignment undone");
    }
    [Fact] public void RenameCanBeRelinkedAfterIdentityVerification()
    {
        var f=Card(true); string changed=engine.Full(Path.Combine(engine.Job(1).Folder,"production","renamed.pdf")); File.Move(engine.Full(f.Path),changed);
        engine.Relink(f.Id,changed); Assert.Equal("Verified",engine.Verify(f.Id)); Assert.False(engine.Artwork(f.Id).Ready);
    }
    [Fact] public void BrokenPdfCannotBecomeAnActiveAssignment()
    {
        string bad=engine.Full(Path.Combine(engine.Job(1).Folder,"provided","broken.pdf")); File.WriteAllText(bad,"not a PDF");
        Assert.ThrowsAny<Exception>(()=>engine.Assign(new Assignment(bad,engine.Items(1)[0].Id,1,500,"Artwork",true))); Assert.Empty(engine.Artworks(1)); Assert.True(File.Exists(bad));
    }
    [Fact] public void SubmissionCreatesRealFilesInBothSimulatedDestinations()
    {
        var f=Card(true); var sub=engine.Submit(1,[f.Id],true); Assert.All(engine.Deliveries(sub.Id),d=>Assert.Equal("Successful",d.Status));
        Assert.Equal(f.Hash,Safety.Hash(Path.Combine(engine.PrintShare,f.Filename))); Assert.True(File.Exists(engine.Full(Path.Combine(sub.Directory,"Moeller.xml"))));
        Assert.Contains("UNVALIDATED",File.ReadAllText(engine.Full(Path.Combine(sub.Directory,"SAMPLE-ONLY.jdf"))));
    }
    [Fact] public void RetryPreservesSuccessfulDestination()
    {
        var f=Card(true); engine.FailNextDestination=Engine.PrintDestination; var sub=engine.Submit(1,[f.Id]);
        Assert.Equal("Failed",engine.Deliveries(sub.Id).Single(d=>d.Destination==Engine.PrintDestination).Status);
        string xml=engine.Full(Path.Combine(sub.Directory,"Moeller.xml")); var time=File.GetLastWriteTimeUtc(xml); string hash=Safety.Hash(xml);
        engine.Retry(sub.Id); Assert.All(engine.Deliveries(sub.Id),d=>Assert.Equal("Successful",d.Status)); Assert.Equal(time,File.GetLastWriteTimeUtc(xml)); Assert.Equal(hash,Safety.Hash(xml));
    }
    [Fact] public void CollisionNeedsConsentAndPreservesOutgoingCopy()
    {
        var f=Card(true); string target=Path.Combine(engine.PrintShare,f.Filename); File.Copy(engine.Sources(1).Single(),target); string before=Safety.Hash(target);
        var sub=engine.Submit(1,[f.Id]); Assert.Equal("Needs attention",engine.Deliveries(sub.Id).Single(d=>d.Destination==Engine.PrintDestination).Status); Assert.Equal(before,Safety.Hash(target));
        engine.Retry(sub.Id,true); Assert.Equal(f.Hash,Safety.Hash(target)); Assert.Contains(Directory.EnumerateFiles(engine.Full(Path.Combine("Simulated Outputs","PrintShare","history"))),p=>Safety.Hash(p)==before);
    }
    [Fact] public void RetryOfHistoricalSubmissionUsesItsSnapshot()
    {
        var old=engine.Artworks(14).Single(); engine.MarkReady([old.Id]); engine.FailNextDestination=Engine.PrintDestination;
        var sub=engine.Submit(14,[old.Id]); engine.Replace(old.Id,engine.Sources(14).First(p=>p.Contains("Replacement"))); engine.Retry(sub.Id);
        Assert.Equal(old.Hash,Safety.Hash(Path.Combine(engine.PrintShare,old.Filename))); Assert.False(engine.Artworks(14).Single().Ready);
    }
    [Fact] public void PpmlIsNotStampedOrFalselyDeliveredAsPdf()
    {
        string source=engine.Sources(11).Single(); string hash=Safety.Hash(source);
        var f=engine.Assign(new Assignment(source,engine.Items(11)[0].Id,1,1000,"Artwork",true)); Assert.Equal(hash,f.Hash); Assert.True(File.Exists(engine.Full(f.Path)+".identity.json"));
        var sub=engine.Submit(11,[f.Id]); Assert.Equal("Not applicable",engine.Deliveries(sub.Id).Single(d=>d.Destination==Engine.PrintDestination).Status); Assert.Equal(hash,Safety.Hash(source));
    }
    [Fact] public void SeededPartialJobRemainsPartial()
    {
        Assert.Single(engine.Submissions(13)); Assert.Contains("02",engine.Completeness(13)); Assert.Single(engine.Artworks(13));
    }
    [Fact] public void ExportContainsVerifiedDatabaseAndActualArtwork()
    {
        var f=Card(); string zipPath=engine.ExportDemo(); string extract=Path.Combine(home,"ExportCheck"); ZipFile.ExtractToDirectory(zipPath,extract);
        Assert.True(File.Exists(Path.Combine(extract,f.Path))); Assert.True(File.Exists(Path.Combine(extract,"EXPORT-SHA256.json")));
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(extract,"Database","demo.sqlite3"),Pooling=false}.ToString()); db.Open(); using var cmd=db.CreateCommand(); cmd.CommandText="PRAGMA integrity_check;"; Assert.Equal("ok",cmd.ExecuteScalar());
    }
    [Fact] public void ResetArchivesExistingStateBeforeNewBaseline()
    {
        var f=Card(); string zip=engine.ResetDemo(); Assert.True(File.Exists(zip)); Assert.Empty(engine.Artworks(1)); Assert.Equal(16,engine.Jobs().Count); Assert.NotEmpty(Directory.EnumerateDirectories(Path.Combine(home,"Archived Resets")));
    }
    [Fact] public void NormalRestartPreservesWorkingState()
    {
        var f=Card(); string root=engine.Root; engine.Dispose(); engine=new Engine(root); Assert.Equal(f.Id,engine.Artworks(1).Single().Id);
    }
    [Fact] public void FilenamesAreStableAndWindowsSafe()
    {
        Assert.Equal("41038-02-V01-R03-Postcard.pdf",Names.Production("41038","02",1,3,"Postcard","Artwork",".pdf")); Assert.StartsWith("_",Names.Safe("CON")); Assert.DoesNotContain(":",Names.Safe("a:b"));
    }
}

using System.Xml.Linq;
using Moeller.Core;
using Xunit;

namespace Moeller.Tests;

public sealed class SubmissionSnapshotTests : IDisposable
{
    private readonly string home=Path.Combine(Path.GetTempPath(),"Moeller-Snapshot-Test-"+Guid.NewGuid().ToString("N"));
    private readonly Engine engine;
    public SubmissionSnapshotTests(){engine=new Engine(Path.Combine(home,"Workspace"));}
    public void Dispose(){engine.Dispose();if(Directory.Exists(home))Directory.Delete(home,true);}
    private Artwork Card()=>engine.Assign(new Assignment(engine.Sources(1).Single(),engine.Items(1).Single().Id,1,500,"Artwork",true));

    [Fact] public void RetryUsesTheQuantityApprovedAtSubmission()
    {
        var file=Card();engine.FailNextDestination=Engine.AdvancedDestination;
        var submission=engine.Submit(1,[file.Id]);
        engine.SaveDesign(file.ItemId,1,400);
        engine.Retry(submission.Id);
        var xml=XDocument.Load(engine.Full(Path.Combine(submission.Directory,"Moeller.xml")));
        Assert.Equal("500",xml.Descendants("AllocatedQuantity").Single().Value);
        Assert.Equal("",xml.Descendants("Warnings").Single().Value);
        Assert.False(engine.Artwork(file.Id).Ready);
        Assert.All(engine.Deliveries(submission.Id),d=>Assert.Equal("Successful",d.Status));
    }
    [Fact] public void UndoDoesNotRenameTheHistoricalSubmissionSnapshot()
    {
        var file=Card();engine.FailNextDestination=Engine.PrintDestination;
        var submission=engine.Submit(1,[file.Id]);engine.UndoAssignment(file.Id);
        Assert.NotEqual(file.Filename,engine.Artwork(file.Id).Filename);
        engine.Retry(submission.Id);
        Assert.Equal(file.Hash,Safety.Hash(Path.Combine(engine.PrintShare,file.Filename)));
        Assert.All(engine.Deliveries(submission.Id),d=>Assert.Equal("Successful",d.Status));
    }
    [Fact] public void AlteredSpecificationsAreNotUsedOnRetry()
    {
        var file=Card();engine.FailNextDestination=Engine.PrintDestination;
        var submission=engine.Submit(1,[file.Id]);
        string specs=engine.Full(Path.Combine(submission.Directory,"specifications.json"));
        File.AppendAllText(specs,"\n ");engine.Retry(submission.Id);
        var print=engine.Deliveries(submission.Id).Single(d=>d.Destination==Engine.PrintDestination);
        Assert.Equal("Failed",print.Status);Assert.Contains("specifications",print.Detail);
        Assert.False(File.Exists(Path.Combine(engine.PrintShare,file.Filename)));
        Assert.Equal("Successful",engine.Deliveries(submission.Id).Single(d=>d.Destination==Engine.AdvancedDestination).Status);
    }
}

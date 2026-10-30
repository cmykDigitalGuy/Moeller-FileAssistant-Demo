namespace Moeller.FileAssistant;

internal record ContextFile(string Path,string Filename,string State,string? Id);

public sealed class SelectionWindow : WorkWindow
{
    private readonly HashSet<string> paths=new(StringComparer.OrdinalIgnoreCase);
    private readonly ComboBox group=new(){Margin=new Thickness(0,4,0,10)};
    private readonly DataGrid files=Ui.Table(("Selected artwork",nameof(ContextFile.Filename),0),("State",nameof(ContextFile.State),160));
    private readonly TextBlock description=Ui.Text(""),notice=Ui.Notice("");
    public int ReceivedPathCount=>paths.Count;
    public SelectionWindow(Engine engine,IEnumerable<string> incoming) : base(engine)
    {
        Title="Moeller File Assistant - selected artwork";Width=790;Height=540;MinWidth=620;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new DockPanel{Margin=new Thickness(20)};var header=new StackPanel();header.Children.Add(Ui.Text("MOELLER / SELECTED ARTWORK",22,true));header.Children.Add(description);header.Children.Add(group);header.Children.Add(notice);DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var bottom=new StackPanel{Margin=new Thickness(0,10,0,0)};var actions=new WrapPanel();actions.Children.Add(Ui.AsyncButton("Assign artwork",AssignAsync,"Accent"));actions.Children.Add(Ui.AsyncButton("Send to production",SendAsync,"Accent"));actions.Children.Add(Ui.AsyncButton("Readiness / history",OpenJobAsync));actions.Children.Add(Ui.Button("Close",Close));bottom.Children.Add(actions);bottom.Children.Add(Status);DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);root.Children.Add(files);Content=root;
        group.SelectionChanged+=(_,_)=>BindFiles();AddPaths(incoming);
    }
    public void AddPaths(IEnumerable<string> incoming)
    {
        var rejected=new List<string>();
        foreach(string path in incoming)
        {
            try
            {
                if(!File.Exists(path)||Engine.JobForPath(path)==null)throw new IOException("not inside a recognized demonstration job");
                paths.Add(Path.GetFullPath(path));
            }
            catch(Exception e){rejected.Add(Path.GetFileName(path)+": "+e.Message);}
        }
        int? selected=(group.SelectedItem as Job)?.Id;
        var choices=paths.Select(p=>Engine.JobForPath(p)!).GroupBy(j=>j.Id).Select(g=>g.First()).OrderBy(j=>j.Id).ToList();
        group.ItemsSource=choices;group.SelectedItem=choices.FirstOrDefault(j=>j.Id==selected)??choices.FirstOrDefault();
        description.Text=$"{paths.Count} selected file(s) / {choices.Count} recognized demo job(s)";
        notice.Text=choices.Count>1?"Files are grouped by job. Confirm one job at a time; files from different jobs are never combined.":"Choose the production purpose, confirm the working copy, then approve and send when ready.";
        if(rejected.Count>0)Status.Text="Not accepted; no changes made: "+string.Join(" / ",rejected);
        BindFiles();
    }
    private void BindFiles()
    {
        if(group.SelectedItem is not Job job){files.ItemsSource=null;return;}
        var active=Engine.Artworks(job.Id);var rows=paths.Where(p=>Engine.JobForPath(p)?.Id==job.Id).Select(p=>
        {
            var match=active.FirstOrDefault(f=>Engine.Full(f.Path).Equals(p,StringComparison.OrdinalIgnoreCase));
            return new ContextFile(p,Path.GetFileName(p),match?.State??"Unassigned / source",match?.Id);
        }).ToList();files.ItemsSource=rows;files.SelectAll();
    }
    private List<ContextFile> Selected()=>files.SelectedItems.Cast<ContextFile>().ToList();
    private async Task AssignAsync()
    {
        if(group.SelectedItem is not Job job)throw new InvalidOperationException("No recognized demo job is selected.");
        var selected=Selected();if(selected.Count==0)throw new InvalidOperationException("Select artwork first.");
        if(selected.Any(f=>f.Id!=null))throw new InvalidOperationException("Selected production artwork is already assigned. Use Readiness / history to approve, replace, or change its assignment.");
        var dialog=new AssignmentWindow(Engine,job,selected.Select(f=>f.Path)){Owner=this};dialog.ShowDialog();
        if(dialog.Changed){Status.Text="Working copies were created under production. Open Readiness / history to approve and send them.";await ((App)Application.Current).RefreshMainAsync();}
        BindFiles();
    }
    private async Task SendAsync()
    {
        if(group.SelectedItem is not Job job)throw new InvalidOperationException("Choose a demo job.");
        var selected=Selected();if(selected.Count==0||selected.Any(f=>f.Id==null))throw new InvalidOperationException("Send the assigned working PDF from production, not the supplied original. Use Readiness / history to find and approve it.");
        string warnings=Engine.QuantityWarnings(job.Id);
        if(warnings!=""&&!Ui.Confirm(this,warnings+"\n\nAcknowledge these quantity differences?"))return;
        if(!Ui.Confirm(this,$"Send {selected.Count} selected Ready revision(s) to the two LOCAL simulated destinations?\nOther job components remain unfinished."))return;
        await Work(()=>{var s=Engine.Submit(job.Id,selected.Select(f=>f.Id!),false,true);return string.Join(" / ",Engine.Deliveries(s.Id).Select(d=>d.Destination+": "+d.Status));});
        BindFiles();await ((App)Application.Current).RefreshMainAsync();
    }
    private async Task OpenJobAsync()
    {
        if(group.SelectedItem is not Job job)throw new InvalidOperationException("Choose a recognized demo job.");
        await ((App)Application.Current).ShowMainAsync(job.Id);Close();
    }
}

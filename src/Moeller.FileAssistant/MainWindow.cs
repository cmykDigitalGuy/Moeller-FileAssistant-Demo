namespace Moeller.FileAssistant;

internal record FileRow(string Kind,string Filename,string Item,string Version,string Quantity,string State,string Path,string? Id);
internal record SubmissionRow(string Id,string Time,string Scope,string Destination,string Status,string Detail,string Directory);
internal record RevisionRow(string Id,string Filename,string Version,string Component,string Active,string State,string Path);
internal record JobView(Job Job,List<FileRow> Files,List<RevisionRow> Revisions,List<SubmissionRow> Submissions,string History,string Warning,string Readiness);

public sealed class MainWindow : WorkWindow
{
    private readonly ListBox jobs=new(){BorderThickness=new Thickness(0),Background=Brushes.White};
    private readonly TextBox search=new(){Margin=new Thickness(0,0,0,10),ToolTip="Search job number, customer or job name"};
    private readonly TextBlock title=Ui.Text("Choose a fictional job",23,true),subtitle=Ui.Text(""),scenario=Ui.Text(""),summary=Ui.Text(""),warning=Ui.Text("");
    private readonly DataGrid files=Ui.Table(("File",nameof(FileRow.Filename),0),("Location",nameof(FileRow.Kind),90),("Item / role",nameof(FileRow.Item),160),("Version",nameof(FileRow.Version),95),("Qty",nameof(FileRow.Quantity),65),("Status",nameof(FileRow.State),120));
    private readonly DataGrid revisions=Ui.Table(("Revision file",nameof(RevisionRow.Filename),0),("Version",nameof(RevisionRow.Version),100),("Component",nameof(RevisionRow.Component),135),("Active",nameof(RevisionRow.Active),65),("Status",nameof(RevisionRow.State),110));
    private readonly DataGrid submissions=Ui.Table(("Created (UTC)",nameof(SubmissionRow.Time),160),("Scope",nameof(SubmissionRow.Scope),115),("Destination",nameof(SubmissionRow.Destination),160),("Status",nameof(SubmissionRow.Status),110),("Details",nameof(SubmissionRow.Detail),0));
    private readonly TextBox history=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new FontFamily("Consolas"),FontSize=12,Background=Brushes.White};
    private readonly TabControl tabs=new();
    private List<Job> allJobs=[];
    private int refreshNumber;
    public Job? SelectedJob=>jobs.SelectedItem as Job;

    public MainWindow(Engine engine) : base(engine)
    {
        Title="Moeller File Assistant - Offline Demo";Width=1240;Height=800;MinWidth=1000;MinHeight=650;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new DockPanel();
        var header=new Grid{Background=Ui.Navy,Margin=new Thickness(0,0,0,0)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var brand=new StackPanel{Margin=new Thickness(22,15,20,15)};var name=Ui.Text("MOELLER  /  FILE ASSISTANT",22,true);name.Foreground=Brushes.White;brand.Children.Add(name);
        var description=Ui.Text("Assign artwork. Approve a revision. Send to simulated production.");description.Foreground=new SolidColorBrush(Color.FromRgb(188,215,229));brand.Children.Add(description);header.Children.Add(brand);
        var badge=Ui.Text("OFFLINE DEMO\n16 fictional jobs",13,true);badge.Foreground=Brushes.White;badge.Margin=new Thickness(18);badge.TextAlignment=TextAlignment.Right;Grid.SetColumn(badge,1);header.Children.Add(badge);DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var footer=new Border{Background=Ui.Navy,Padding=new Thickness(20,9,20,9)};Status.Foreground=Brushes.White;Status.Margin=new Thickness(0);Status.Text="v"+Engine.Version+" / Local demo workspace / No production connections";footer.Child=Status;DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var body=new Grid{Margin=new Thickness(16)};body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(275)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(14)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        var left=new DockPanel();var leftTop=new StackPanel();leftTop.Children.Add(Ui.Text("DEMO JOBS",13,true));leftTop.Children.Add(search);DockPanel.SetDock(leftTop,Dock.Top);left.Children.Add(leftTop);
        var leftBottom=new StackPanel{Margin=new Thickness(0,12,0,0)};
        leftBottom.Children.Add(Ui.Button("Open demo workspace",()=>Ui.Open(Engine,Engine.Root)));
        leftBottom.Children.Add(Ui.AsyncButton("Export demo ZIP",ExportAsync,"Accent"));
        leftBottom.Children.Add(Ui.Button("Settings / diagnostics",ShowSettings));
        leftBottom.Children.Add(Ui.Notice("No credentials, mapped drives, server or configuration needed."));DockPanel.SetDock(leftBottom,Dock.Bottom);left.Children.Add(leftBottom);
        var template=new DataTemplate(typeof(Job));var stack=new FrameworkElementFactory(typeof(StackPanel));
        var first=new FrameworkElementFactory(typeof(TextBlock));first.SetBinding(TextBlock.TextProperty,new Binding("Display"));first.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);first.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);stack.AppendChild(first);
        var second=new FrameworkElementFactory(typeof(TextBlock));second.SetBinding(TextBlock.TextProperty,new Binding("Customer"));second.SetValue(TextBlock.FontSizeProperty,11.0);second.SetValue(FrameworkElement.MarginProperty,new Thickness(0,4,0,0));stack.AppendChild(second);template.VisualTree=stack;jobs.ItemTemplate=template;
        left.Children.Add(jobs);body.Children.Add(Ui.Card(left,12));
        var right=new DockPanel();Grid.SetColumn(right,2);body.Children.Add(right);
        var jobHead=new StackPanel();jobHead.Children.Add(title);jobHead.Children.Add(subtitle);scenario.Foreground=Ui.Teal;scenario.FontWeight=FontWeights.SemiBold;jobHead.Children.Add(scenario);summary.Foreground=Ui.Muted;jobHead.Children.Add(summary);
        var folderButtons=new WrapPanel();folderButtons.Children.Add(Ui.Button("Open job folder",()=>OpenJobFolder("")));folderButtons.Children.Add(Ui.Button("Provided files",()=>OpenJobFolder("provided")));folderButtons.Children.Add(Ui.Button("Production files",()=>OpenJobFolder("production")));folderButtons.Children.Add(Ui.Button("PrintShare",()=>Ui.Open(Engine,Engine.PrintShare)));folderButtons.Children.Add(Ui.Button("Apogee samples",()=>Ui.Open(Engine,Engine.ApogeeSamples)));
        jobHead.Children.Add(folderButtons);warning.Foreground=new SolidColorBrush(Color.FromRgb(140,61,8));jobHead.Children.Add(warning);DockPanel.SetDock(jobHead,Dock.Top);right.Children.Add(jobHead);
        BuildTabs();right.Children.Add(tabs);root.Children.Add(body);Content=root;
        allJobs=Engine.Jobs();jobs.ItemsSource=allJobs;
        search.TextChanged+=(_,_)=>FilterJobs();jobs.SelectionChanged+=async(_,_)=>{try{await RefreshAsync();}catch(Exception e){Ui.Error(this,e);}};
        files.MouseDoubleClick+=(_,_)=>{if(files.SelectedItem is FileRow row){try{Ui.Open(Engine,row.Path);}catch(Exception e){Ui.Error(this,e);}}};
        Closing+=(_,e)=>{if(!((App)Application.Current).Exiting&&!Busy&&Engine.Get("close-to-tray","on")=="on"){e.Cancel=true;Hide();((App)Application.Current).Notify("File Assistant is in the tray. Right-click artwork still works.");}};
        jobs.SelectedIndex=0;
    }
    private void BuildTabs()
    {
        var artwork=new DockPanel{Margin=new Thickness(12)};
        var actions=new WrapPanel();actions.Children.Add(Ui.AsyncButton("Assign artwork",AssignSelectedAsync,"Accent"));actions.Children.Add(Ui.AsyncButton("Mark Ready",MarkReadyAsync));actions.Children.Add(Ui.AsyncButton("Send selected",()=>SendSelectedAsync(false),"Accent"));
        var more=Ui.Button("More",()=>{});var menu=new ContextMenu();
        Add(menu,"Replace artwork",ReplaceAsync);Add(menu,"Review modified artwork",ReviewModifiedAsync);Add(menu,"Create version / split quantity",()=>{ShowQuantity();return Task.CompletedTask;});
        Add(menu,"Undo assignment",UndoAsync);Add(menu,"Relink renamed file",RelinkAsync);Add(menu,"Show embedded identity",()=>{ShowIdentity();return Task.CompletedTask;});
        Add(menu,"Send complete job",()=>SendSelectedAsync(true));Add(menu,"Clear Ready",ClearReadyAsync);Add(menu,"Simulate PDF modification",SimulateAsync);
        Add(menu,"Smart suggestions: on for this job",()=>{SetSmart("on");return Task.CompletedTask;});Add(menu,"Smart suggestions: off for this job",()=>{SetSmart("off");return Task.CompletedTask;});Add(menu,"Smart suggestions: use global setting",()=>{SetSmart("inherit");return Task.CompletedTask;});
        more.Click+=(_,_)=>{menu.PlacementTarget=more;menu.IsOpen=true;};actions.Children.Add(more);actions.Children.Add(Ui.AsyncButton("Refresh / verify",RefreshAsync));
        DockPanel.SetDock(actions,Dock.Top);artwork.Children.Add(actions);
        var hint=Ui.Notice("Select provided PDFs to assign. Select production rows to approve or send. Ctrl-click selects multiple rows. Double-click opens the file.");DockPanel.SetDock(hint,Dock.Top);artwork.Children.Add(hint);artwork.Children.Add(files);tabs.Items.Add(new TabItem{Header="Artwork / readiness",Content=artwork});
        var revisionPanel=new DockPanel{Margin=new Thickness(12)};var revisionActions=new WrapPanel();revisionActions.Children.Add(Ui.AsyncButton("Restore as new revision",RestoreAsync));revisionActions.Children.Add(Ui.Button("Open selected revision",()=>{if(revisions.SelectedItem is RevisionRow row)Ui.Open(Engine,Engine.Full(row.Path));}));revisionActions.Children.Add(Ui.Button("Open revision archive",()=>OpenJobFolder("previous versions")));DockPanel.SetDock(revisionActions,Dock.Top);revisionPanel.Children.Add(revisionActions);
        var revNotice=Ui.Notice("Restoring R01 after R03 creates R04. Prior revisions and submission records are retained. Restored artwork must be approved again.");DockPanel.SetDock(revNotice,Dock.Top);revisionPanel.Children.Add(revNotice);revisionPanel.Children.Add(revisions);tabs.Items.Add(new TabItem{Header="Revision history",Content=revisionPanel});
        var submissionPanel=new DockPanel{Margin=new Thickness(12)};var submissionActions=new WrapPanel();submissionActions.Children.Add(Ui.AsyncButton("Retry selected destination(s)",RetryAsync));submissionActions.Children.Add(Ui.Button("Open submission package",()=>{if(submissions.SelectedItem is SubmissionRow row)Ui.Open(Engine,Engine.Full(row.Directory));}));submissionActions.Children.Add(Ui.Button("Show result details",()=>{if(submissions.SelectedItem is SubmissionRow row)Ui.ShowText(this,"Submission result",row.Detail);}));DockPanel.SetDock(submissionActions,Dock.Top);submissionPanel.Children.Add(submissionActions);
        var subNotice=Ui.Notice("Each destination has its own outcome. Retry preserves successful deliveries. Sample apoXML / JDF is not validated for Apogee.");DockPanel.SetDock(subNotice,Dock.Top);submissionPanel.Children.Add(subNotice);submissionPanel.Children.Add(submissions);tabs.Items.Add(new TabItem{Header="Submissions",Content=submissionPanel});
        tabs.Items.Add(new TabItem{Header="Activity",Content=new Border{Padding=new Thickness(12),Child=history}});
        var help=new StackPanel{Margin=new Thickness(22)};help.Children.Add(Ui.Text("A real, isolated workflow test",22,true));
        foreach(var text in new[]{"1. Choose job 41023 and open Provided files.","2. Right-click its PDF, choose Show more options if needed, then Moeller File Assistant.","3. Assign it to item 01. A renamed, XMP-stamped working PDF appears under production.","4. Mark the working file Ready, then Send selected.","5. Open PrintShare and Apogee samples to inspect the copied files and XML.","6. Use job 41036 for replacement and restore; job 41032 starts with modified artwork.","Export demo ZIP creates a consistent database snapshot together with fictional artwork, revisions, history and outputs. It is not an installer.","Reset Demo asks for confirmation and archives the current state before rebuilding all 16 jobs.","All paths are under this application's local workspace. Supplied originals are not edited. There are no Moeller API connections, real hotfolders, live PPLAN records, or network destinations."})help.Children.Add(Ui.Notice(text));
        tabs.Items.Add(new TabItem{Header="Start here",Content=new ScrollViewer{Content=help,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
    }
    private void Add(ContextMenu menu,string title,Func<Task> action){var m=new MenuItem{Header=title};m.Click+=async(_,_)=>{try{await action();}catch(Exception e){Ui.Error(this,e);}};menu.Items.Add(m);}
    private void FilterJobs(){int? current=SelectedJob?.Id;string text=search.Text.Trim();jobs.ItemsSource=allJobs.Where(j=>(j.Number+" "+j.Customer+" "+j.Title).Contains(text,StringComparison.OrdinalIgnoreCase)).ToList();if(current!=null)jobs.SelectedItem=((IEnumerable<Job>)jobs.ItemsSource).FirstOrDefault(j=>j.Id==current);if(jobs.SelectedIndex<0&&jobs.Items.Count>0)jobs.SelectedIndex=0;}
    public async Task SelectJobAsync(int id){search.Text="";jobs.SelectedItem=allJobs.Single(j=>j.Id==id);await RefreshAsync();Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();}
    public async Task RefreshAsync()
    {
        if(SelectedJob is not Job job)return;int request=++refreshNumber;
        var selected=files.SelectedItems.Cast<FileRow>().Select(r=>r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        JobView view=await Task.Run(()=>
        {
            Engine.VerifyJob(job.Id);var items=Engine.Items(job.Id).ToDictionary(i=>i.Id);var active=Engine.Artworks(job.Id);
            var rows=Engine.Sources(job.Id).Select(p=>new FileRow("Provided",Path.GetFileName(p),"Not assigned here","","","Original",p,null)).ToList();
            rows.AddRange(active.Select(f=>new FileRow("Production",f.Filename,items[f.ItemId].Number+" / "+f.Component,f.VersionText,Engine.Designs(f.ItemId).Single(v=>v.Number==f.Version).Quantity.ToString("N0"),f.State,Engine.Full(f.Path),f.Id)));
            var rev=Engine.Artworks(job.Id,true).Select(f=>new RevisionRow(f.Id,f.Filename,f.VersionText,f.Component,f.Active?"Yes":"No",f.State,f.Path)).ToList();
            var deliveries=Engine.Submissions(job.Id).SelectMany(s=>Engine.Deliveries(s.Id).Select(d=>new SubmissionRow(s.Id,s.Time[..19].Replace('T',' '),s.Scope,d.Destination,d.Status,d.Detail,s.Directory))).ToList();
            string log=string.Join(Environment.NewLine+Environment.NewLine,Engine.History(job.Id).Select(a=>$"{a.Time[..19].Replace('T',' ')} UTC | {a.Action}\n{a.Detail}"));
            return new JobView(job,rows,rev,deliveries,log,Engine.QuantityWarnings(job.Id),$"{items.Count} PPLAN item(s) / {active.Count} working file(s) / {active.Count(f=>f.Ready&&f.Integrity=="Verified")} Ready");
        });
        if(request!=refreshNumber||SelectedJob?.Id!=job.Id)return;
        title.Text=$"{job.Number} / {job.Title}";subtitle.Text=job.Customer;scenario.Text=job.Scenario;summary.Text=view.Readiness;warning.Text=view.Warning;
        warning.Visibility=view.Warning==""?Visibility.Collapsed:Visibility.Visible;
        files.ItemsSource=view.Files;revisions.ItemsSource=view.Revisions;submissions.ItemsSource=view.Submissions;history.Text=view.History;
        foreach(var row in view.Files.Where(r=>selected.Contains(r.Path)))files.SelectedItems.Add(row);
    }
    private Job RequireJob()=>SelectedJob??throw new InvalidOperationException("Choose a fictional job first.");
    private List<string> SelectedIds()=>files.SelectedItems.Cast<FileRow>().Where(r=>r.Id!=null).Select(r=>r.Id!).ToList();
    private Artwork OneArtwork(){var ids=SelectedIds();if(ids.Count!=1)throw new InvalidOperationException("Select one production row for this action.");return Engine.Artwork(ids[0]);}
    private void OpenJobFolder(string child){var job=RequireJob();Ui.Open(Engine,Engine.Full(Path.Combine(job.Folder,child)));}
    private async Task AssignSelectedAsync()
    {
        var job=RequireJob();var paths=files.SelectedItems.Cast<FileRow>().Where(r=>r.Id==null).Select(r=>r.Path).ToList();
        if(paths.Count==0)
        {
            var picker=new Microsoft.Win32.OpenFileDialog{Title="Choose artwork inside the selected demo job",InitialDirectory=Engine.Full(Path.Combine(job.Folder,"provided")),Filter="Demo artwork|*.pdf;*.zip",Multiselect=true};
            if(picker.ShowDialog(this)!=true)return;paths=picker.FileNames.ToList();
        }
        foreach(var path in paths)if(Engine.JobForPath(path)?.Id!=job.Id)throw new InvalidOperationException("Choose files from this demo job's folder. Files outside the demo are not modified.");
        var dialog=new AssignmentWindow(Engine,job,paths){Owner=this};dialog.ShowDialog();if(dialog.Changed)await RefreshAsync();
    }
    private async Task MarkReadyAsync(){var ids=SelectedIds();if(ids.Count==0)throw new InvalidOperationException("Select one or more production rows to approve.");await Work(()=>{Engine.MarkReady(ids);return $"{ids.Count} file(s) marked Ready.";},RefreshAsync);}
    private async Task ClearReadyAsync(){var ids=SelectedIds();if(ids.Count==0)throw new InvalidOperationException("Select a production row.");await Work(()=>{Engine.MarkReady(ids,false);return "Approval cleared.";},RefreshAsync);}
    private async Task SendSelectedAsync(bool complete)
    {
        var job=RequireJob();var ids=complete?Engine.Artworks(job.Id).Select(f=>f.Id).ToList():SelectedIds();if(ids.Count==0)throw new InvalidOperationException("Select the Ready production files to send.");
        string warnings=Engine.QuantityWarnings(job.Id);if(warnings!=""&&!Ui.Confirm(this,warnings+"\n\nAcknowledge these allocation differences and continue?","Quantity warning"))return;
        if(!Ui.Confirm(this,$"Send {ids.Count} selected working file(s) to the LOCAL simulated PrintShare and XML/sample destinations?\n\n"+(complete?"The complete job will be checked for all required components.":"Only selected artwork is submitted. Other job items are not completed.")))return;
        await Work(()=>{var sub=Engine.Submit(job.Id,ids,complete,true);var states=Engine.Deliveries(sub.Id);return "Submission "+sub.Id[..8]+": "+string.Join(" / ",states.Select(d=>d.Destination+" "+d.Status));},RefreshAsync);tabs.SelectedIndex=2;
    }
    private async Task ReplaceAsync()
    {
        var old=OneArtwork();var job=Engine.Job(old.JobId);var picker=new Microsoft.Win32.OpenFileDialog{Title="Choose replacement from this demo job",InitialDirectory=Engine.Full(Path.Combine(job.Folder,"provided")),Filter="Demo artwork|*.pdf;*.zip"};
        if(picker.ShowDialog(this)!=true)return;
        string note=Engine.WasSubmitted(old.Id)?"\n\nThis revision has a submission record. Replacing it does NOT update or recall that submission.":"";
        if(!Ui.Confirm(this,$"Replace {old.Filename}?\nThe previous working file will be archived. The new revision will not be Ready."+note))return;
        await Work(()=>{var f=Engine.Replace(old.Id,picker.FileName);return "Created "+f.Filename+". Approve before submitting.";},RefreshAsync);
    }
    private async Task ReviewModifiedAsync(){var f=OneArtwork();if(!Ui.Confirm(this,"Accept the current working file's content as a NEW revision?\nIts former expected fingerprint remains in history. The new revision will not be Ready."))return;await Work(()=>{var newer=Engine.Replace(f.Id,Engine.Full(f.Path),true);return "Reviewed content saved as "+newer.VersionText;},RefreshAsync);}
    private async Task UndoAsync(){var f=OneArtwork();if(!Ui.Confirm(this,"Undo this active assignment?\nThe file is archived and all prior history is retained."))return;await Work(()=>{Engine.UndoAssignment(f.Id);return "Assignment undone; file archived.";},RefreshAsync);}
    private async Task RelinkAsync(){var f=OneArtwork();var picker=new Microsoft.Win32.OpenFileDialog{Title="Find this revision inside the same demo job",InitialDirectory=Engine.Full(Engine.Job(f.JobId).Folder),Filter="Demo artwork|*.pdf;*.zip"};if(picker.ShowDialog(this)!=true)return;if(!Ui.Confirm(this,"Relink to this file only after its identity and hash match?\nNo duplicate file will be deleted. Readiness will be cleared."))return;await Work(()=>{Engine.Relink(f.Id,picker.FileName);return "File relationship recovered; reapprove before sending.";},RefreshAsync);}
    private async Task RestoreAsync(){if(revisions.SelectedItem is not RevisionRow selected)throw new InvalidOperationException("Select a historical revision.");if(!Ui.Confirm(this,"Restore the selected historical artwork as the next chronological revision?\nThe current file will be archived and Ready will be cleared."))return;await Work(()=>{var f=Engine.Restore(selected.Id);return "Restored as "+f.VersionText;},RefreshAsync);}
    private async Task SimulateAsync(){var f=OneArtwork();if(!Ui.Confirm(this,"Intentionally modify this demo working PDF to test integrity detection?\nThe customer-provided original will not change."))return;await Work(()=>{Engine.SimulateModification(f.Id);return "Demo modification detected; Ready cleared.";},RefreshAsync);}
    private async Task RetryAsync()
    {
        if(submissions.SelectedItem is not SubmissionRow row)throw new InvalidOperationException("Select a submission result.");
        bool replace=false;if(Engine.Deliveries(row.Id).Any(d=>d.Status=="Needs attention"))replace=Ui.Confirm(this,"This submission has a PrintShare filename collision.\nReplace conflicting incoming copies after archiving them?\n\nChoose No to leave those copies unchanged.","PrintShare collision");
        await Work(()=>{Engine.Retry(row.Id,replace);return string.Join(" / ",Engine.Deliveries(row.Id).Select(d=>d.Destination+": "+d.Status));},RefreshAsync);
    }
    private void ShowIdentity(){var f=OneArtwork();object identity=Path.GetExtension(f.Path).Equals(".pdf",StringComparison.OrdinalIgnoreCase)?PdfTools.ReadIdentity(Engine.Full(f.Path))??(object)"No embedded Moeller identity":"PPML ZIP identity is tracked externally; the ZIP is unchanged.";Ui.ShowText(this,"File identity and fingerprints",Safety.Json(new{Metadata=identity,Database=f}));}
    private void ShowQuantity(){var job=RequireJob();new QuantityWindow(Engine,job){Owner=this}.ShowDialog();_ = RefreshAsync();}
    private void SetSmart(string value){var job=RequireJob();Engine.Set("smart-"+job.Id,value=="inherit"?Engine.Get("smart-global","off"):value);Status.Text="Smart suggestions for this job: "+value+". Assignments still require confirmation.";}
    public void ShowSettings(){new SettingsWindow(Engine){Owner=this}.ShowDialog();allJobs=Engine.Jobs();FilterJobs();_ = RefreshAsync();}
    private async Task ExportAsync()
    {
        var dialog=new Microsoft.Win32.SaveFileDialog{Title="Export complete demo state",Filter="ZIP archive|*.zip",FileName="Moeller-Demo-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip",InitialDirectory=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),OverwritePrompt=true};
        if(dialog.ShowDialog(this)!=true)return;
        if(File.Exists(dialog.FileName))throw new IOException("Choose a new export filename; existing ZIPs are preserved.");
        string? result=null;if(await Work(()=>{result=Engine.ExportDemo(dialog.FileName);return "Verified demo ZIP exported: "+Path.GetFileName(result);}))Ui.RevealExport(result!);
    }
}

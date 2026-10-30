namespace Moeller.FileAssistant;

internal sealed class AllocationRow
{
    public int Version {get;set;}
    public int Quantity {get;set;}
}

public sealed class QuantityWindow : WorkWindow
{
    private readonly ComboBox itemBox=new(){Margin=new Thickness(0,4,0,12)};
    private readonly DataGrid table=Ui.Table(("Artwork version",nameof(AllocationRow.Version),160),("Allocated quantity",nameof(AllocationRow.Quantity),200));
    private ObservableCollection<AllocationRow> allocations=[];
    public QuantityWindow(Engine engine,Job job) : base(engine)
    {
        Title="Version / quantity allocation - "+job.Number;Width=640;Height=540;MinWidth=520;MinHeight=430;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new Thickness(20)};var head=new StackPanel();head.Children.Add(Ui.Text("Create versions / split quantity",22,true));
        head.Children.Add(Ui.Notice("PPLAN ordered quantity stays unchanged. A changed allocation clears Ready for that design. Under- and over-allocation are permitted with a warning."));
        itemBox.ItemsSource=engine.Items(job.Id);head.Children.Add(itemBox);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
        var bottom=new StackPanel();var actions=new WrapPanel();actions.Children.Add(Ui.Button("Add artwork version",()=>allocations.Add(new AllocationRow{Version=allocations.Count==0?1:allocations.Max(x=>x.Version)+1,Quantity=1})));
        actions.Children.Add(Ui.AsyncButton("Save allocation",SaveAsync,"Accent"));actions.Children.Add(Ui.Button("Close",Close));bottom.Children.Add(actions);bottom.Children.Add(Status);DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        table.IsReadOnly=false;root.Children.Add(table);Content=root;
        itemBox.SelectionChanged+=(_,_)=>
        {
            if(itemBox.SelectedItem is not Item item)return;
            allocations=new ObservableCollection<AllocationRow>(Engine.Designs(item.Id).Select(d=>new AllocationRow{Version=d.Number,Quantity=d.Quantity}));table.ItemsSource=allocations;
            Status.Text=$"Ordered quantity: {item.Quantity:N0}. Existing designs are not removed by this dialog.";
        };
        itemBox.SelectedIndex=0;
    }
    private async Task SaveAsync()
    {
        table.CommitEdit(DataGridEditingUnit.Cell,true);table.CommitEdit(DataGridEditingUnit.Row,true);
        if(itemBox.SelectedItem is not Item item)return;var changes=allocations.ToList();
        if(changes.Any(v=>v.Version<1||v.Quantity<1)||changes.Select(v=>v.Version).Distinct().Count()!=changes.Count)throw new InvalidOperationException("Use positive quantities and unique positive version numbers.");
        long total=changes.Sum(v=>(long)v.Quantity);if(total!=item.Quantity&&!Ui.Confirm(this,$"Ordered: {item.Quantity:N0}\nAllocated: {total:N0}\n\nKeep this allocation difference?"))return;
        bool ok=await Work(()=>
        {
            var original=Engine.Designs(item.Id).ToDictionary(d=>d.Number,d=>d.Quantity);
            foreach(var change in changes)if(!original.TryGetValue(change.Version,out int prior)||prior!=change.Quantity)Engine.SaveDesign(item.Id,change.Version,change.Quantity);
            return "Allocations saved. Ordered quantity was not changed.";
        });
        if(ok)Close();
    }
}

public sealed class SettingsWindow : WorkWindow
{
    private readonly TextBox report=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=120,MaxHeight=220,FontSize=12};
    public SettingsWindow(Engine engine) : base(engine)
    {
        Title="File Assistant - Settings and diagnostics";Width=780;Height=760;MinWidth=600;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var outer=new DockPanel{Margin=new Thickness(20)};var footer=new StackPanel();footer.Children.Add(Status);footer.Children.Add(Ui.Button("Close",Close));DockPanel.SetDock(footer,Dock.Bottom);outer.Children.Add(footer);
        var stack=new StackPanel();stack.Children.Add(Ui.Text("Settings / diagnostics",24,true));
        stack.Children.Add(Ui.Text("One local workspace. No production connections.",14,true));stack.Children.Add(Ui.Notice(engine.Root));
        var folders=new WrapPanel();folders.Children.Add(Ui.Button("Open workspace",()=>Ui.Open(engine,engine.Root)));folders.Children.Add(Ui.Button("Open PrintShare",()=>Ui.Open(engine,engine.PrintShare)));folders.Children.Add(Ui.Button("Open XML samples",()=>Ui.Open(engine,engine.ApogeeSamples)));stack.Children.Add(folders);
        stack.Children.Add(Check("Keep assistant in system tray when its main window closes","close-to-tray","on"));
        stack.Children.Add(Check("Suggest an item only when the filename clearly matches (manual confirmation still required)","smart-global","off"));
        var startup=new CheckBox{Content="Start quietly in the tray when I sign in",IsChecked=ShellIntegration.StartupEnabled};startup.Click+=(_,_)=>{try{ShellIntegration.SetStartup(startup.IsChecked==true);}catch(Exception e){Ui.Error(this,e);startup.IsChecked=ShellIntegration.StartupEnabled;}};stack.Children.Add(startup);
        stack.Children.Add(Ui.Text("Windows Explorer integration",16,true));
        stack.Children.Add(Ui.Notice("The installer adds Moeller File Assistant to PDF and ZIP right-click menus. On Windows 11, use Show more options. Send to > Moeller File Assistant is also installed for multi-file selections."));
        stack.Children.Add(Ui.Button("Repair right-click registration",()=>{ShellIntegration.Register();RefreshReport();Status.Text="Per-user Explorer registration repaired. Reopen the context menu.";}));
        stack.Children.Add(Ui.Text("Health checks",16,true));stack.Children.Add(report);
        var checks=new WrapPanel();checks.Children.Add(Ui.Button("Run diagnostics",RefreshReport));checks.Children.Add(Ui.AsyncButton("Export diagnostic ZIP",ExportDiagnosticsAsync));stack.Children.Add(checks);
        stack.Children.Add(Ui.Text("Controlled failure tests",16,true));var failure=new WrapPanel();failure.Children.Add(Ui.Button("Fail next PrintShare attempt",()=>{engine.FailNextDestination=Moeller.Core.Engine.PrintDestination;Status.Text="Next PrintShare delivery will fail intentionally. Retry will then work normally.";}));failure.Children.Add(Ui.Button("Fail next XML/sample attempt",()=>{engine.FailNextDestination=Moeller.Core.Engine.AdvancedDestination;Status.Text="Next XML/sample delivery will fail intentionally. Other destination is independent.";}));stack.Children.Add(failure);
        stack.Children.Add(Ui.Text("Restore the fictional baseline",16,true));stack.Children.Add(Ui.Notice("Reset archives the current database, artwork and output state before creating all 16 jobs again. Repairing the installation does not reset a same-version workspace."));stack.Children.Add(Ui.AsyncButton("Reset all 16 demo jobs",ResetAsync,"Danger"));
        stack.Children.Add(Ui.Notice("This is an unsigned local demonstration. The installer does not change Smart App Control, antivirus rules, firewall rules, or system-wide security policy."));
        outer.Children.Add(new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=outer;RefreshReport();
    }
    private CheckBox Check(string title,string key,string fallback)
    {
        var c=new CheckBox{Content=new TextBlock{Text=title,TextWrapping=TextWrapping.Wrap},IsChecked=Engine.Get(key,fallback)=="on"};c.Click+=(_,_)=>Engine.Set(key,c.IsChecked==true?"on":"off");return c;
    }
    private void RefreshReport()
    {
        var checks=Engine.Diagnostics();checks.Add(new Diagnostic("Explorer command",ShellIntegration.IsRegistered,ShellIntegration.IsRegistered?"Registered for current user":"Missing; use Repair right-click registration"));
        checks.Add(new Diagnostic("Bundled metadata tool",File.Exists(PdfTools.ExifToolPath),Path.GetFileName(PdfTools.ExifToolPath)));
        report.Text="Version "+Moeller.Core.Engine.Version+" / "+Environment.OSVersion+Environment.NewLine+Environment.NewLine+string.Join(Environment.NewLine,checks.Select(c=>(c.Passed?"PASS  ":"ATTENTION  ")+c.Name+" - "+c.Detail));
    }
    private async Task ExportDiagnosticsAsync()
    {
        string? destination=null;if(await Work(()=>{destination=Engine.ExportDiagnostics();return "Sanitized diagnostic ZIP created. Artwork and database are not included.";}))Ui.RevealExport(destination!);
    }
    private async Task ResetAsync()
    {
        if(!Ui.Confirm(this,"Reset every fictional job, working assignment, and simulated output?\n\nYour current demo state will first be saved in a verified ZIP and an archive folder. Real customer files and production systems are not involved.","Reset demo"))return;
        await Work(()=>"Demo reset. Verified prior-state backup: "+Path.GetFileName(Engine.ResetDemo()));RefreshReport();
    }
}

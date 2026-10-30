namespace Moeller.FileAssistant;

/// <summary>
/// Task-first shell panel. All operations remain inside the fictional local workspace.
/// The legacy full dashboard is only accessible deliberately under More.
/// </summary>
internal sealed record DisplayFile(string Path)
{
    public string Filename=>System.IO.Path.GetFileName(Path);
    public override string ToString()=>Filename;
}

public sealed class CompactActionWindow : WorkWindow
{
    private readonly string mode;
    private readonly HashSet<string> selectedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ComboBox fileBox = new(){MinHeight=35};
    private readonly ComboBox itemBox = new(){MinHeight=35};
    private readonly ComboBox componentBox = new(){MinHeight=35};
    private readonly ComboBox existingBox = new(){MinHeight=35};
    private readonly TextBox versionBox = new(){Text="1"};
    private readonly TextBox quantityBox = new(){Text="1"};
    private readonly TextBlock summary = Ui.Notice("");
    private readonly TextBlock outcome = Ui.Notice("");
    private readonly StackPanel actionForm = new();
    private readonly StackPanel replacementForm = new();
    private readonly StackPanel moreForm = new();
    private Job? job;
    private readonly Button go;
    private bool changed;

    public CompactActionWindow(Engine engine, IEnumerable<string> paths, string mode) : base(engine)
    {
        this.mode = new[]{"assign","version","component","replace","more"}.Contains(mode)?mode:"more";
        Title="Moeller Hub - "+Label(this.mode);
        Width=465;MinWidth=420;Height=650;MinHeight=490;
        MaxWidth=520;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        ResizeMode=ResizeMode.CanResize;
        var outer=new DockPanel{Margin=new Thickness(18)};
        var brand=new Border{Background=Ui.Navy,Padding=new Thickness(15),CornerRadius=new CornerRadius(5),Margin=new Thickness(0,0,0,12)};
        var text=new StackPanel();
        var heading=Ui.Text("MOELLER HUB",21,true);heading.Foreground=Brushes.White;text.Children.Add(heading);
        var sub=Ui.Text(Label(this.mode),15,true);sub.Foreground=Brushes.White;text.Children.Add(sub);
        brand.Child=text;DockPanel.SetDock(brand,Dock.Top);outer.Children.Add(brand);

        var bottom=new StackPanel{Margin=new Thickness(0,10,0,0)};
        go=Ui.AsyncButton(Primary(this.mode),ExecuteAsync,"Accent");
        var buttons=new WrapPanel();
        if(this.mode!="more")buttons.Children.Add(go);
        buttons.Children.Add(Ui.Button("Close",Close));
        bottom.Children.Add(buttons);bottom.Children.Add(Status);
        DockPanel.SetDock(bottom,Dock.Bottom);outer.Children.Add(bottom);

        var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var body=new StackPanel();
        summary.Text="Only fictional local jobs. Originals are never overwritten.";
        body.Children.Add(summary);
        body.Children.Add(Section("SELECTED FILE"));
        body.Children.Add(fileBox);
        body.Children.Add(actionForm);
        body.Children.Add(replacementForm);
        body.Children.Add(moreForm);
        body.Children.Add(outcome);
        scroll.Content=body;outer.Children.Add(scroll);Content=outer;
        fileBox.SelectionChanged+=(_,_)=>ChooseFile();
        itemBox.SelectionChanged+=(_,_)=>ChooseItem();

        BuildForms();
        AddPaths(paths);
    }

    public void AddPaths(IEnumerable<string> incoming)
    {
        foreach(var p in incoming)
        {
            try
            {
                var full=Path.GetFullPath(p);
                if(File.Exists(full) && Engine.JobForPath(full)!=null &&
                  new[]{".pdf",".zip"}.Contains(Path.GetExtension(full),StringComparer.OrdinalIgnoreCase))
                    selectedPaths.Add(full);
            }
            catch(Exception e){Status.Text="Ignored unsupported path: "+e.Message;}
        }
        fileBox.ItemsSource=selectedPaths.OrderBy(Path.GetFileName).Select(p=>new DisplayFile(p)).ToArray();
        if(fileBox.SelectedIndex<0 && selectedPaths.Count>0)fileBox.SelectedIndex=0;
        if(selectedPaths.Count==0)
        {
            summary.Text="No demonstration artwork selected. Open one of the 16 fictional job folders and right-click a PDF.";
            go.IsEnabled=false;
        }
        else summary.Text=selectedPaths.Count+" selected file(s). Choose the file and confirm the simple action below.";
    }

    private static string Label(string action)=>action switch
    {
        "assign"=>"Assign Artwork",
        "version"=>"Create Version / Split Quantity",
        "component"=>"Assign Component",
        "replace"=>"Replace Artwork",
        _=>"More Options"
    };
    private static string Primary(string action)=>action switch
    {
        "assign"=>"Assign Artwork",
        "version"=>"Save Version and Assign",
        "component"=>"Assign Component",
        "replace"=>"Replace With This File",
        _=>"Open"
    };
    private static TextBlock Section(string text)
    {
        var t=Ui.Text(text,12,true);t.Margin=new Thickness(0,12,0,5);return t;
    }
    private void BuildForms()
    {
        if(mode is "assign" or "component" or "version")
        {
            actionForm.Children.Add(Section("PPLAN JOB ITEM"));
            actionForm.Children.Add(itemBox);
            actionForm.Children.Add(Section("ARTWORK VERSION"));
            actionForm.Children.Add(versionBox);
            actionForm.Children.Add(Section("QUANTITY FOR THIS VERSION"));
            actionForm.Children.Add(quantityBox);
            actionForm.Children.Add(Section("FILE COMPONENT"));
            actionForm.Children.Add(componentBox);
            actionForm.Children.Add(Ui.Notice(mode=="version"
                ?"Creates or updates a version allocation, then assigns this selected artwork. The original is preserved."
                :"Creates an XMP-stamped production working copy. Nothing is sent until separately approved."));
        }
        if(mode=="replace")
        {
            replacementForm.Children.Add(Section("REPLACE WHICH ACTIVE WORKING FILE?"));
            replacementForm.Children.Add(existingBox);
            replacementForm.Children.Add(Ui.Notice("The selected file is the new source. Choose an existing active revision above. The previous revision is archived; Ready is cleared."));
        }
        if(mode=="more")
        {
            moreForm.Children.Add(Section("ADDITIONAL TOOLS"));
            moreForm.Children.Add(Ui.Button("View Job / Readiness / History",()=>OpenJob()));
            moreForm.Children.Add(Ui.Button("Open Demo Job Folder",()=>{if(job!=null)Ui.Open(Engine,Engine.Full(job.Folder));}));
            moreForm.Children.Add(Ui.Button("Open Simulated PrintShare",()=>Ui.Open(Engine,Engine.PrintShare)));
            moreForm.Children.Add(Ui.Button("Open XML / JDF Examples",()=>Ui.Open(Engine,Engine.ApogeeSamples)));
            moreForm.Children.Add(Ui.Button("Open Full Dashboard",()=>_ = ((App)Application.Current).ShowMainAsync(job?.Id)));
        }
    }

    private void ChooseFile()
    {
        if(fileBox.SelectedItem is not DisplayFile choice){job=null;return;}
        string selected=choice.Path;
        fileBox.ToolTip=selected;
        job=Engine.JobForPath(selected);
        if(job==null){Status.Text="This file is not in a demonstration job.";return;}
        var items=Engine.Items(job.Id);
        itemBox.ItemsSource=items;
        if(items.Count>0)itemBox.SelectedIndex=0;
        existingBox.ItemsSource=Engine.Artworks(job.Id);
        existingBox.DisplayMemberPath=nameof(Artwork.Filename);
        var matching=Engine.Artworks(job.Id).FirstOrDefault(f=>Engine.Full(f.Path).Equals(selected,StringComparison.OrdinalIgnoreCase));
        if(matching!=null)existingBox.SelectedItem=matching;
        else if(existingBox.Items.Count>0)existingBox.SelectedIndex=0;
        Status.Text=job.Number+" / "+job.Customer;
    }
    private void ChooseItem()
    {
        if(itemBox.SelectedItem is not Item item)return;
        var designs=Engine.Designs(item.Id);
        var version=designs.FirstOrDefault();
        versionBox.Text=(version?.Number??1).ToString();
        quantityBox.Text=(version?.Quantity??item.Quantity).ToString();
        var choices=item.Components.Split('+',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToList();
        if(choices.Count>1)choices.Add(item.Components);
        componentBox.ItemsSource=choices;
        if(choices.Count>0)componentBox.SelectedIndex=0;
    }
    private async Task ExecuteAsync()
    {
        if(fileBox.SelectedItem is not DisplayFile fileChoice||job==null)
            throw new InvalidOperationException("Choose a PDF inside a fictional demo job first.");
        string path=fileChoice.Path;
        if(mode=="replace")
        {
            if(existingBox.SelectedItem is not Artwork current)
                throw new InvalidOperationException("Choose the working revision to replace.");
            if(!Ui.Confirm(this,"Archive "+current.Filename+" and create a new revision from "+Path.GetFileName(path)+"?\nReady will be cleared. Previously sent submissions are not recalled."))return;
            if(await Work(()=>Engine.Replace(current.Id,path).Filename+" created. Check Readiness before sending."))
                changed=true;
            return;
        }
        if(itemBox.SelectedItem is not Item item)throw new InvalidOperationException("Choose the job item.");
        if(!int.TryParse(versionBox.Text,out int v)||v<1||!int.TryParse(quantityBox.Text,out int q)||q<1)
            throw new InvalidOperationException("Version and quantity must be positive whole numbers.");
        if(componentBox.SelectedItem is not string component||string.IsNullOrWhiteSpace(component))
            throw new InvalidOperationException("Select a component.");
        if(!Ui.Confirm(this,$"Create the working copy?\n\nJob: {job.Number}\nItem: {item.Number}\nVersion: V{v:00}\nQuantity: {q:N0}\nComponent: {component}\n\nOriginal file remains untouched."))return;
        if(await Work(()=>
        {
            var current=Engine.Designs(item.Id).FirstOrDefault(d=>d.Number==v);
            var occupied=Engine.Artworks(job.Id).Any(f=>f.ItemId==item.Id && f.Version==v &&
                f.Component.Split('+').Intersect(component.Split('+'),StringComparer.OrdinalIgnoreCase).Any());
            if(mode=="version" && occupied)
            {
                if(current==null || current.Quantity!=q)Engine.SaveDesign(item.Id,v,q);
                return "Version allocation saved. Existing working file kept; Ready cleared if quantity changed.";
            }
            if(occupied)throw new InvalidOperationException("This version already has that component. Use Replace Artwork or choose another version.");
            if(mode=="version")
            {
                if(current==null||current.Quantity!=q)Engine.SaveDesign(item.Id,v,q);
            }
            var assigned=Engine.Assign(new Assignment(path,item.Id,v,q,component,false));
            return assigned.Filename+" created in production. Not Ready yet.";
        }))changed=true;
    }
    private void OpenJob()
    {
        if(job==null)throw new InvalidOperationException("Choose a demo job.");
        _ = ((App)Application.Current).ShowMainAsync(job.Id);
        Close();
    }
}

namespace Moeller.FileAssistant;

internal sealed class AssignmentRow : INotifyPropertyChanged
{
    private readonly Engine engine;
    private Item? item;
    private int version=1,quantity;
    private string component="Artwork";
    private bool ready;
    public string Source {get;}
    public string Filename=>Path.GetFileName(Source);
    public List<Item> Choices {get;}
    public List<string> ComponentChoices {get;private set;}=["Artwork"];
    public Item? Item
    {
        get=>item;
        set
        {
            item=value;
            if(item!=null)
            {
                var allocations=engine.Designs(item.Id);var chosen=allocations.FirstOrDefault(d=>d.Number==Version)??allocations.First();
                version=chosen.Number;quantity=chosen.Quantity;
                ComponentChoices=item.Components.Split('+',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToList();
                if(ComponentChoices.Count>1)ComponentChoices.Add(item.Components);
                component=ComponentChoices.Count==1?ComponentChoices[0]:"";
            }
            Changed("");
        }
    }
    public int Version {get=>version;set{version=value;if(item!=null){var d=engine.Designs(item.Id).FirstOrDefault(x=>x.Number==value);if(d!=null)quantity=d.Quantity;}Changed("");}}
    public int Quantity {get=>quantity;set{quantity=value;Changed("");}}
    public string Component {get=>component;set{component=value;Changed("");}}
    public bool Ready {get=>ready;set{ready=value;Changed(nameof(Ready));}}
    public string Proposed
    {
        get
        {
            if(item==null)return "Select a PPLAN item";
            if(string.IsNullOrWhiteSpace(component))return "Select a component";
            try{return engine.ProposedName(ToAssignment());}catch{return "Check version and quantity";}
        }
    }
    public AssignmentRow(Engine engine,int jobId,string source)
    {
        this.engine=engine;Source=source;Choices=engine.Items(jobId);
        var match=System.Text.RegularExpressions.Regex.Match(Filename,@"(?:^|[-_ ])V(\d+)",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if(match.Success&&int.TryParse(match.Groups[1].Value,out int parsed))version=parsed;
        if(Choices.Count==1)Item=Choices[0];
        else if(engine.Get("smart-"+jobId,engine.Get("smart-global","off"))=="on")
        {
            string normalized=Filename.Replace("-"," ").Replace("_"," ");
            var candidates=Choices.Where(i=>normalized.Contains(i.Description,StringComparison.OrdinalIgnoreCase)).ToList();
            if(candidates.Count==1)Item=candidates[0];
        }
    }
    public Assignment ToAssignment()=>new(Source,Item?.Id??throw new InvalidOperationException("Select a PPLAN item for "+Filename),Version,Quantity,Component,Ready);
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string property)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(property));
}

public sealed class AssignmentWindow : WorkWindow
{
    private readonly ObservableCollection<AssignmentRow> rows;
    private readonly DataGrid table;
    public bool Changed {get;private set;}
    public AssignmentWindow(Engine engine,Job job,IEnumerable<string> paths) : base(engine)
    {
        Title="Assign Artwork - "+job.Number;Width=1100;Height=570;MinWidth=880;MinHeight=420;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        rows=new ObservableCollection<AssignmentRow>(paths.Distinct(StringComparer.OrdinalIgnoreCase).Select(p=>new AssignmentRow(engine,job.Id,p)));
        var root=new DockPanel{Margin=new Thickness(20)};
        var head=new StackPanel();head.Children.Add(Ui.Text($"Assign artwork / {job.Number}",23,true));head.Children.Add(Ui.Text(job.Customer+" - "+job.Title));
        head.Children.Add(Ui.Notice("Choose each file's production purpose. Originals stay untouched. A working copy with XMP identity will be created in production. Ready is optional."));
        if(engine.Get("smart-"+job.Id,engine.Get("smart-global","off"))=="on")head.Children.Add(Ui.Notice("Smart suggestions are enabled. Any suggested item is based on filename text; it is not committed until you confirm."));
        DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
        var footer=new StackPanel();footer.Children.Add(Ui.Notice("V = different artwork design. R = chronological correction. First assignment starts at R01. Quantity changes to an existing V require Split / quantity."));
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        actions.Children.Add(Ui.Button("Cancel",()=>Close()));actions.Children.Add(Ui.AsyncButton("Confirm assignments",ConfirmAsync,"Accent"));footer.Children.Add(actions);footer.Children.Add(Status);
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        table=new DataGrid{ItemsSource=rows,IsReadOnly=false,RowHeight=48,CanUserSortColumns=false};
        table.Columns.Add(new DataGridTextColumn{Header="Source file",Binding=new Binding(nameof(AssignmentRow.Filename)),Width=new DataGridLength(1,DataGridLengthUnitType.Star),IsReadOnly=true});
        table.Columns.Add(ComboColumn("PPLAN item",nameof(AssignmentRow.Choices),nameof(AssignmentRow.Item),"Display",220));
        table.Columns.Add(new DataGridTextColumn{Header="V",Binding=new Binding(nameof(AssignmentRow.Version)){UpdateSourceTrigger=UpdateSourceTrigger.LostFocus},Width=48});
        table.Columns.Add(new DataGridTextColumn{Header="Quantity",Binding=new Binding(nameof(AssignmentRow.Quantity)){UpdateSourceTrigger=UpdateSourceTrigger.LostFocus},Width=82});
        table.Columns.Add(ComboColumn("Component",nameof(AssignmentRow.ComponentChoices),nameof(AssignmentRow.Component),null,128));
        table.Columns.Add(new DataGridCheckBoxColumn{Header="Ready",Binding=new Binding(nameof(AssignmentRow.Ready)),Width=60});
        table.Columns.Add(new DataGridTextColumn{Header="Production filename",Binding=new Binding(nameof(AssignmentRow.Proposed)),Width=new DataGridLength(1.3,DataGridLengthUnitType.Star),IsReadOnly=true});
        root.Children.Add(table);Content=root;
    }
    private static DataGridTemplateColumn ComboColumn(string title,string source,string selected,string? display,double width)
    {
        var factory=new FrameworkElementFactory(typeof(ComboBox));factory.SetBinding(ItemsControl.ItemsSourceProperty,new Binding(source));
        factory.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,new Binding(selected){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
        if(display!=null)factory.SetValue(ItemsControl.DisplayMemberPathProperty,display);
        factory.SetValue(FrameworkElement.MarginProperty,new Thickness(3));
        var template=new DataTemplate{VisualTree=factory};return new DataGridTemplateColumn{Header=title,CellTemplate=template,CellEditingTemplate=template,Width=width};
    }
    private async Task ConfirmAsync()
    {
        table.CommitEdit(DataGridEditingUnit.Cell,true);table.CommitEdit(DataGridEditingUnit.Row,true);
        var inputs=rows.Select(r=>r.ToAssignment()).ToList();
        if(inputs.Count==0)return;
        if(inputs.Any(i=>i.Version<1||i.Quantity<1||string.IsNullOrWhiteSpace(i.Component)))throw new InvalidOperationException("Every row needs a positive version and quantity, an item, and a component.");
        var names=inputs.Select(i=>Engine.ProposedName(i)).ToList();
        if(names.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=names.Count)throw new InvalidOperationException("Two selected files would have the same production name. Choose distinct versions or components.");
        int completed=0;
        bool ok=await Work(()=>
        {
            try{foreach(var input in inputs){Engine.Assign(input);completed++;}}
            catch(Exception e){throw new IOException($"{completed} of {inputs.Count} assignments completed. Completed rows were retained; remaining rows were not assigned. {e.Message}",e);}
            return $"{completed} artwork assignment(s) created.";
        });
        if(completed>0){Changed=true;foreach(var row in rows.Take(completed).ToList())rows.Remove(row);}
        if(ok)DialogResult=true;
    }
}

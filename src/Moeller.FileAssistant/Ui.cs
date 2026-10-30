using System.Windows.Automation;
using System.Windows.Media.Imaging;

namespace Moeller.FileAssistant;

internal static class Ui
{
    public static readonly Brush Navy=new SolidColorBrush(Color.FromRgb(16,38,59));
    public static readonly Brush Teal=new SolidColorBrush(Color.FromRgb(0,107,101));
    public static readonly Brush Muted=new SolidColorBrush(Color.FromRgb(65,82,100));
    public static TextBlock Text(string value,double size=13,bool bold=false)
        =>new(){Text=value,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6)};
    public static Button Button(string title,Action action,string style="Secondary")
    {
        var b=new Button{Content=title,Style=(Style)Application.Current.FindResource(style)};
        AutomationProperties.SetAutomationId(b,title.Replace(" ",""));
        b.Click+=(_,_)=>{try{action();}catch(Exception e){Error(Window.GetWindow(b),e);}};
        return b;
    }
    public static Button AsyncButton(string title,Func<Task> action,string style="Secondary")
    {
        var b=new Button{Content=title,Style=(Style)Application.Current.FindResource(style)};
        AutomationProperties.SetAutomationId(b,title.Replace(" ",""));
        b.Click+=async(_,_)=>{try{await action();}catch(Exception e){Error(Window.GetWindow(b),e);}};
        return b;
    }
    public static Border Card(UIElement content,double padding=14)=>new(){Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(203,213,225)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(5),Padding=new Thickness(padding),Child=content};
    public static DataGrid Table(params (string Title,string Property,double Width)[] columns)
    {
        var table=new DataGrid{Margin=new Thickness(0,0,0,8)};
        foreach(var column in columns) table.Columns.Add(new DataGridTextColumn{Header=column.Title,Binding=new Binding(column.Property),Width=column.Width<=0?new DataGridLength(1,DataGridLengthUnitType.Star):new DataGridLength(column.Width)});
        return table;
    }
    public static void Error(Window? owner,Exception e)
    {
        App.WriteError(e);
        if(owner!=null&&owner.IsVisible) MessageBox.Show(owner,e.Message,"Moeller File Assistant - attention",MessageBoxButton.OK,MessageBoxImage.Warning);
        else MessageBox.Show(e.Message,"Moeller File Assistant - attention",MessageBoxButton.OK,MessageBoxImage.Warning);
    }
    public static bool Confirm(Window owner,string message,string title="Confirm demo action")=>MessageBox.Show(owner,message,title,MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes;
    public static void Open(Engine engine,string path)
    {
        path=Safety.Within(engine.Root,path,true);
        if(!File.Exists(path)&&!Directory.Exists(path)) throw new IOException("The demo file or folder does not exist.");
        Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
    }
    public static void RevealExport(string path)
    {
        var info=new ProcessStartInfo("explorer.exe"){UseShellExecute=true}; info.Arguments="/select,\""+path.Replace("\"","")+"\""; Process.Start(info);
    }
    public static void ShowText(Window owner,string title,string text)
    {
        var w=new Window{Owner=owner,Title=title,Width=820,Height=560,MinWidth=550,MinHeight=340,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var dock=new DockPanel{Margin=new Thickness(18)};
        var bottom=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        bottom.Children.Add(Button("Copy",()=>Clipboard.SetText(text))); bottom.Children.Add(Button("Close",()=>w.Close())); DockPanel.SetDock(bottom,Dock.Bottom);dock.Children.Add(bottom);
        var box=new TextBox{Text=text,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new FontFamily("Consolas"),FontSize=12};dock.Children.Add(box);w.Content=dock;w.ShowDialog();
    }
    public static void Capture(Window window,string file)
    {
        window.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);
        bitmap.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);using var output=File.Create(file);png.Save(output);
    }
    public static TextBlock Notice(string text)
    {
        var label=Text(text);label.Foreground=Muted;label.Margin=new Thickness(0,6,0,10);return label;
    }
}

public abstract class WorkWindow : Window
{
    protected readonly Engine Engine;
    protected readonly TextBlock Status=Ui.Text("Ready");
    public bool Busy {get;private set;}
    protected WorkWindow(Engine engine)
    {
        Engine=engine;
        Closing+=(_,e)=>{if(Busy){e.Cancel=true;Status.Text="A file operation is still finishing. The window will remain open to protect the files.";}};
    }
    protected async Task<bool> Work(Func<string> action,Func<Task>? refresh=null)
    {
        if(Busy)return false;
        Busy=true;((App)Application.Current).BusyCount++;IsEnabled=false;Status.Text="Working...";
        try
        {
            string result=await Task.Run(action);
            if(refresh!=null) await refresh();
            Status.Text=result;return true;
        }
        catch(Exception e){Status.Text="Needs attention: "+e.Message;Ui.Error(this,e);return false;}
        finally{IsEnabled=true;Busy=false;((App)Application.Current).BusyCount--;}
    }
}

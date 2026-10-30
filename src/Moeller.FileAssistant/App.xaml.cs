using System.Text.Json;

namespace Moeller.FileAssistant;

public partial class App : Application
{
    private Mutex? mutex;
    private bool ownsMutex;
    private LocalMessages? messages;
    private Moeller.Core.Engine? service;
    private MainWindow? main;
    private SelectionWindow? selection;
    private CompactActionWindow? compact;
    private string pendingAction="more";
    private bool ipcSimulation;
    private System.Windows.Forms.NotifyIcon? tray;
    private readonly List<string[]> queued=[];
    private readonly HashSet<string> pendingPaths=new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> receivedPaths=new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? selectionTimer;
    private bool ready,automated;
    public bool Exiting {get;private set;}
    public int BusyCount {get;set;}

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        automated=e.Args.Any(a=>a is "--initialize" or "--ui-smoke" or "--ipc-smoke" or "--export-demo" or "--diagnostics-json");
        DispatcherUnhandledException+=(_,args)=>
        {
            WriteError(args.Exception);args.Handled=true;
            if(!automated)MessageBox.Show(args.Exception.Message,"Moeller File Assistant - application error",MessageBoxButton.OK,MessageBoxImage.Error);
            Exiting=true;Shutdown(1);
        };
        try
        {
            mutex=new Mutex(true,LocalMessages.MutexName,out bool created);ownsMutex=created;
            if(!ownsMutex)
            {
                try{ownsMutex=mutex.WaitOne(0);}catch(AbandonedMutexException){ownsMutex=true;}
            }
            if(!ownsMutex)
            {
                bool delivered=await LocalMessages.SendAsync(e.Args.Length==0?["--open"]:e.Args);
                if(!delivered&&!automated)MessageBox.Show("The running assistant did not accept this request. No files were changed. Close and reopen the assistant, then try again.","Moeller File Assistant",MessageBoxButton.OK,MessageBoxImage.Warning);
                Shutdown(delivered?0:2);return;
            }
            if(e.Args.Contains("--shutdown")){Exiting=true;Shutdown(0);return;}
            if(e.Args.Contains("--unregister")){ShellIntegration.Unregister();Exiting=true;Shutdown(0);return;}
            messages=new LocalMessages(Receive);
            service=await Task.Run(()=>new Moeller.Core.Engine());ready=true;
            if(e.Args.Contains("--initialize")){ShellIntegration.Register();WriteResult("initialization.json",new{Version=Moeller.Core.Engine.Version,Jobs=service.Jobs().Count,Checks=service.Diagnostics()});Exiting=true;Shutdown(0);return;}
            int export=Array.IndexOf(e.Args,"--export-demo");
            if(export>=0)
            {
                if(export+1>=e.Args.Length)throw new ArgumentException("Export destination is required.");
                await Task.Run(()=>service.ExportDemo(e.Args[export+1]));Exiting=true;Shutdown(0);return;
            }
            int diagnostic=Array.IndexOf(e.Args,"--diagnostics-json");
            if(diagnostic>=0)
            {
                if(diagnostic+1>=e.Args.Length)throw new ArgumentException("Diagnostic report destination is required.");
                File.WriteAllText(e.Args[diagnostic+1],Safety.Json(service.Diagnostics()));Exiting=true;Shutdown(0);return;
            }
            CreateTray();
            selectionTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(750)};selectionTimer.Tick+=(_,_)=>FlushSelection();
            int smoke=Array.IndexOf(e.Args,"--ui-smoke");
            if(smoke>=0)
            {
                if(smoke+1>=e.Args.Length)throw new ArgumentException("Smoke-test screenshot destination is required.");
                await UiSmokeAsync(e.Args[smoke+1]);ExitApplication();return;
            }
            int ipc=Array.IndexOf(e.Args,"--ipc-smoke");
            if(ipc>=0)
            {
                ipcSimulation=false;
                if(ipc+1>=e.Args.Length)throw new ArgumentException("IPC report destination is required.");
                string report=e.Args[ipc+1];Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
                File.WriteAllText(report+".ready","ready");
                var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(12)};
                timer.Tick+=(_,_)=>
                {
                    timer.Stop();FlushSelection();
                    File.WriteAllText(report,Safety.Json(new{Received=receivedPaths.Count,Paths=receivedPaths.ToArray(),PopupCount=Windows.OfType<CompactActionWindow>().Count(),VisiblePopup=compact?.IsVisible??false}));
                    if(compact?.IsVisible==true)Ui.Capture(compact,Path.ChangeExtension(report,"png"));
                    ExitApplication();
                };
                timer.Start();
            }
            else if(e.Args.Contains("--select"))Receive(e.Args);
            else if(!e.Args.Contains("--tray"))await ShowMainAsync();
            foreach(var request in queued.ToList())Receive(request);queued.Clear();
        }
        catch(Exception error)
        {
            WriteError(error);
            if(!automated)MessageBox.Show("The offline demo could not start. Existing data was preserved.\n\n"+error.Message+"\n\nDiagnostics: "+ErrorPath,"Moeller File Assistant",MessageBoxButton.OK,MessageBoxImage.Error);
            Exiting=true;Shutdown(1);
        }
    }
    private void Receive(string[] arguments)
    {
        if(arguments.Contains("--shutdown")){ExitApplication();return;}
        if(!ready){queued.Add(arguments);return;}
        if(arguments.Contains("--select"))
        {
            int actionIndex=Array.IndexOf(arguments,"--action");
            if(actionIndex>=0&&actionIndex+1<arguments.Length)pendingAction=arguments[actionIndex+1];
            int index=Array.IndexOf(arguments,"--select");
            foreach(string path in arguments.Skip(index+1).Where(p=>!p.StartsWith("--",StringComparison.Ordinal)))
            {
                if(pendingPaths.Count>=255)break;
                pendingPaths.Add(path);receivedPaths.Add(path);
            }
            selectionTimer?.Stop();selectionTimer?.Start();
        }
        else if(!arguments.Contains("--tray"))_ = ShowMainAsync();
    }
    private void FlushSelection()
    {
        if(BusyCount>0)return;selectionTimer?.Stop();if(service==null||pendingPaths.Count==0)return;
        var paths=pendingPaths.ToArray();pendingPaths.Clear();
        if(ipcSimulation)
        {
            if(selection==null){selection=new SelectionWindow(service,paths);selection.Closed+=(_,_)=>selection=null;selection.Show();}
            else selection.AddPaths(paths);
            if(selection.WindowState==WindowState.Minimized)selection.WindowState=WindowState.Normal;
            selection.Activate();
            return;
        }
        if(compact==null)
        {
            compact=new CompactActionWindow(service,paths,pendingAction);
            compact.Closed+=(_,_)=>compact=null;
            compact.Show();
        }
        else compact.AddPaths(paths);
        if(compact.WindowState==WindowState.Minimized)compact.WindowState=WindowState.Normal;
        compact.Activate();
    }
    public async Task ShowMainAsync(int? jobId=null)
    {
        if(service==null)return;
        if(main==null)
        {
            main=new MainWindow(service);MainWindow=main;main.Closed+=(_,_)=>{main=null;if(!Exiting&&service.Get("close-to-tray","on")=="off")ExitApplication();};
        }
        main.Show();if(main.WindowState==WindowState.Minimized)main.WindowState=WindowState.Normal;main.Activate();
        if(jobId.HasValue)await main.SelectJobAsync(jobId.Value);else await main.RefreshAsync();
    }
    public async Task RefreshMainAsync(){if(main?.IsVisible==true)await main.RefreshAsync();}
    private void CreateTray()
    {
        tray=new System.Windows.Forms.NotifyIcon{Text="Moeller File Assistant - Offline Demo",Icon=System.Drawing.Icon.ExtractAssociatedIcon(ShellIntegration.Executable)??System.Drawing.SystemIcons.Application,Visible=true};
        var menu=new System.Windows.Forms.ContextMenuStrip();
        void Add(string title,Action action)=>menu.Items.Add(title,null,(_,_)=>Dispatcher.BeginInvoke(action));
        Add("Open File Assistant",()=>_ = ShowMainAsync());
        Add("Open demo workspace",()=>{if(service!=null)Ui.Open(service,service.Root);});
        Add("Settings / diagnostics",async()=>{await ShowMainAsync();main?.ShowSettings();});
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());Add("Exit",ExitApplication);tray.ContextMenuStrip=menu;
        tray.DoubleClick+=(_,_)=>Dispatcher.BeginInvoke(()=>_ = ShowMainAsync());
    }
    public void Notify(string message){if(tray==null)return;tray.BalloonTipTitle="Moeller File Assistant";tray.BalloonTipText=message;tray.ShowBalloonTip(2500);}
    public void ExitApplication()
    {
        if(BusyCount>0){Notify("A file operation is finishing. Try Exit again after it completes.");return;}
        Exiting=true;Shutdown(0);
    }
    private async Task UiSmokeAsync(string screenshot)
    {
        string directory=Path.GetDirectoryName(Path.GetFullPath(screenshot))!;Directory.CreateDirectory(directory);
        await ShowMainAsync(1);await Task.Delay(600);Ui.Capture(main!,screenshot);
        var assign=new AssignmentWindow(service!,service!.Job(2),service.Sources(2).Take(3)){Owner=main};assign.Show();await Task.Delay(300);Ui.Capture(assign,Path.Combine(directory,"assignment-window.png"));assign.Close();
        await main!.SelectJobAsync(16);await Task.Delay(200);Ui.Capture(main,Path.Combine(directory,"mixed-job-window.png"));
        var settings=new SettingsWindow(service){Owner=main};settings.Show();await Task.Delay(250);Ui.Capture(settings,Path.Combine(directory,"settings-window.png"));settings.Close();
        var popup=new SelectionWindow(service,service.Sources(2).Take(3));popup.Show();await Task.Delay(250);Ui.Capture(popup,Path.Combine(directory,"explorer-popup.png"));popup.Close();
        foreach(var mode in new[]{"assign","version","component","replace","more"})
        {
            var panel=new CompactActionWindow(service,service.Sources(2).Take(1),mode);
            panel.Show();
            await Task.Delay(220);
            Ui.Capture(panel,Path.Combine(directory,"compact-"+mode+".png"));
            if(panel.ActualWidth>535)throw new InvalidOperationException("Compact "+mode+" window exceeded quarter-width design.");
            panel.Close();
        }
        File.WriteAllText(Path.Combine(directory,"ui-smoke.json"),Safety.Json(new{Result="Passed",WindowsRendered=10,Version=Moeller.Core.Engine.Version,Jobs=service.Jobs().Count,OS=Environment.OSVersion.ToString(),Architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),Note="All five compact WPF action windows rendered on Windows. This is not manual Windows 11 Explorer acceptance."}));
    }
    private static string ErrorPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Moeller","FileAssistantDemo","startup-errors.log");
    public static void WriteError(Exception error)
    {
        try{Directory.CreateDirectory(Path.GetDirectoryName(ErrorPath)!);File.AppendAllText(ErrorPath,DateTimeOffset.UtcNow.ToString("O")+Environment.NewLine+error+Environment.NewLine+Environment.NewLine);}catch{}
    }
    private void WriteResult(string name,object value){if(service!=null)File.WriteAllText(service.Full(Path.Combine("Logs",name)),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));}
    protected override void OnExit(ExitEventArgs e)
    {
        selectionTimer?.Stop();messages?.Dispose();tray?.Dispose();service?.Dispose();if(ownsMutex)try{mutex?.ReleaseMutex();}catch(ApplicationException){}mutex?.Dispose();base.OnExit(e);
    }
}

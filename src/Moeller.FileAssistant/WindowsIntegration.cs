using Microsoft.Win32;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

namespace Moeller.FileAssistant;

internal static class ShellIntegration
{
    public const string Verb="MoellerHub";
    private const string OldVerb="MoellerFileAssistantDemo";
    public static string Executable=>Environment.ProcessPath??throw new IOException("Application executable path unavailable.");
    private static string Base(string ext)=>@"Software\Classes\SystemFileAssociations\"+ext+@"\shell\";
    private static readonly (string Id,string Label,string Action)[] Actions =
    [
        ("01Assign","Assign Artwork","assign"),
        ("02Version","Create Version / Split Quantity","version"),
        ("03Component","Assign Component","component"),
        ("04Replace","Replace Artwork","replace"),
        ("05More","More...","more")
    ];
    public static void Register()
    {
        foreach(var ext in new[]{".pdf",".zip"})
        {
            Registry.CurrentUser.DeleteSubKeyTree(Base(ext)+OldVerb,false);
            Registry.CurrentUser.DeleteSubKeyTree(Base(ext)+Verb,false);
            using var root=Registry.CurrentUser.CreateSubKey(Base(ext)+Verb)!;
            root.SetValue("MUIVerb","Moeller Hub");
            root.SetValue("Icon",Executable);
            root.SetValue("SubCommands","");
            root.SetValue("MultiSelectModel","Player");
            using var shell=root.CreateSubKey("shell")!;
            foreach(var action in Actions)
            {
                using var item=shell.CreateSubKey(action.Id)!;
                item.SetValue("MUIVerb",action.Label);
                item.SetValue("MultiSelectModel","Player");
                if(action.Action=="more")item.SetValue("CommandFlags",0x20,RegistryValueKind.DWord);
                using var command=item.CreateSubKey("command")!;
                command.SetValue("", "\""+Executable+"\" --action "+action.Action+" --select \"%1\"");
            }
        }
        SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);
    }
    public static void Unregister()
    {
        foreach(var ext in new[]{".pdf",".zip"})
        {
            Registry.CurrentUser.DeleteSubKeyTree(Base(ext)+Verb,false);
            Registry.CurrentUser.DeleteSubKeyTree(Base(ext)+OldVerb,false);
        }
        SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);
    }
    public static bool IsRegistered
    {
        get
        {
            foreach(var ext in new[]{".pdf",".zip"})
            {
                using var root=Registry.CurrentUser.OpenSubKey(Base(ext)+Verb);
                if((root?.GetValue("MUIVerb") as string)!="Moeller Hub" || root.GetValue("SubCommands") is not string)return false;
                foreach(var action in Actions)
                {
                    using var cmd=Registry.CurrentUser.OpenSubKey(Base(ext)+Verb+@"\shell\"+action.Id+@"\command");
                    if((cmd?.GetValue("") as string)!="\""+Executable+"\" --action "+action.Action+" --select \"%1\"")return false;
                }
            }
            return true;
        }
    }
    public static bool StartupEnabled
    {
        get{using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");return key?.GetValue("MoellerFileAssistantDemo")!=null;}
    }
    public static void SetStartup(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")!;
        if(enabled)key.SetValue("MoellerFileAssistantDemo","\""+Executable+"\" --tray");
        else key.DeleteValue("MoellerFileAssistantDemo",false);
    }
    [DllImport("shell32.dll")]private static extern void SHChangeNotify(uint eventId,uint flags,IntPtr item1,IntPtr item2);
}

internal sealed class LocalMessages : IDisposable
{
    public const string MutexName=@"Local\Moeller.FileAssistant.Demo.v020";
    private static string PipeName=>"MoellerFileAssistantDemo-"+(WindowsIdentity.GetCurrent().User?.Value??Environment.UserName).Replace('\\','_');
    private readonly CancellationTokenSource stop=new();
    private readonly Action<string[]> receive;
    public LocalMessages(Action<string[]> receive){this.receive=receive;_ = ListenAsync();}
    private async Task ListenAsync()
    {
        while(!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe=new NamedPipeServerStream(PipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly,16384,16384);
                await pipe.WaitForConnectionAsync(stop.Token);
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var reader=new StreamReader(pipe,new UTF8Encoding(false),false,4096,true);
                using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};
                string? line=await reader.ReadLineAsync(timeout.Token);
                if(line==null||line.Length>131072)throw new IOException("Selection message is too large.");
                string[] request=JsonSerializer.Deserialize<string[]>(line)??[];
                if(request.Length>256)throw new IOException("Select at most 255 files at once in this demo.");
                await Application.Current.Dispatcher.InvokeAsync(()=>receive(request));
                await writer.WriteLineAsync("accepted");
            }
            catch(OperationCanceledException) when(stop.IsCancellationRequested){break;}
            catch(Exception e){App.WriteError(e);if(!stop.IsCancellationRequested)await Task.Delay(120);}
        }
    }
    public static async Task<bool> SendAsync(string[] arguments)
    {
        try
        {
            using var pipe=new NamedPipeClientStream(".",PipeName,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await pipe.ConnectAsync(timeout.Token);
            using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};using var reader=new StreamReader(pipe,new UTF8Encoding(false),false,4096,true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(arguments));return await reader.ReadLineAsync(timeout.Token)=="accepted";
        }
        catch(Exception e){App.WriteError(e);return false;}
    }
    public void Dispose(){stop.Cancel();stop.Dispose();}
}

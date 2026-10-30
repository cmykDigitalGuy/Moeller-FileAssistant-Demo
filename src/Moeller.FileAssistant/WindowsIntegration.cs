using Microsoft.Win32;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

namespace Moeller.FileAssistant;

internal static class ShellIntegration
{
    public const string Verb="MoellerFileAssistantDemo";
    public static string Executable=>Environment.ProcessPath??throw new IOException("Application executable path is unavailable.");
    private static string Key(string extension)=>@"Software\Classes\SystemFileAssociations\"+extension+@"\shell\"+Verb;
    public static void Register()
    {
        foreach(string extension in new[]{".pdf",".zip"})
        {
            using var verb=Registry.CurrentUser.CreateSubKey(Key(extension));
            verb.SetValue("","Moeller File Assistant");verb.SetValue("Icon",Executable);verb.SetValue("MultiSelectModel","Player");
            using var command=verb.CreateSubKey("command");command.SetValue("","\""+Executable+"\" --select \"%1\"");
        }
        SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);
    }
    public static bool IsRegistered
    {
        get
        {
            foreach(string extension in new[]{".pdf",".zip"})
            {
                using var key=Registry.CurrentUser.OpenSubKey(Key(extension)+@"\command");
                if(!string.Equals(key?.GetValue("") as string,"\""+Executable+"\" --select \"%1\"",StringComparison.OrdinalIgnoreCase))return false;
            }
            return true;
        }
    }
    public static bool StartupEnabled
    {
        get{using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");return key?.GetValue(Verb)!=null;}
    }
    public static void SetStartup(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if(enabled)key.SetValue(Verb,"\""+Executable+"\" --tray");else key.DeleteValue(Verb,false);
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

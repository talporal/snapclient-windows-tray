using Microsoft.UI.Xaml;
using System.Security.Principal;
namespace Snapcast.Tray;
public partial class App : Application {
    MainWindow? window;
    Mutex? instance;
    EventWaitHandle? openEvent;
    RegisteredWaitHandle? openWait;
    public App() {
        UnhandledException+=(_,e)=>StartupDiagnostics.Write("Unhandled WinUI exception.",e.Exception);
        InitializeComponent();
        StartupDiagnostics.Write("Application XAML resources loaded.");
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args) {
        StartupDiagnostics.Write("Tray launch event received.");
        var commands=Environment.GetCommandLineArgs();
        var smoke=commands.Contains("--smoke-test");
        try {
            if(!smoke) {
                var eventName="Local\\SnapcastWindows.Tray.Open."+WindowsIdentity.GetCurrent().User!.Value;
                instance=new Mutex(true,"Local\\SnapcastWindows.Tray",out var created);
                if(!created) {
                    try {using var existing=EventWaitHandle.OpenExisting(eventName);existing.Set();}
                    catch(Exception error){StartupDiagnostics.Write("Unable to activate existing tray instance.",error);}
                    Exit();return;
                }
                openEvent=new EventWaitHandle(false,EventResetMode.AutoReset,eventName);
            }
            StartupDiagnostics.Write("Creating settings window.");
            window=new MainWindow(smoke);
            window.InitializeTray(background:smoke||commands.Contains("--background"));
            if(smoke) {
                window.DispatcherQueue.TryEnqueue(()=>{
                    try {window.RunSmokeChecks();StartupDiagnostics.Write("SMOKE PASS: GUI, icon assets and notification window initialized.");Environment.ExitCode=0;}
                    catch(Exception error){StartupDiagnostics.Write("SMOKE FAIL.",error);Environment.ExitCode=1;}
                    finally {window.Shutdown();}
                });
            } else {
                openWait=ThreadPool.RegisterWaitForSingleObject(openEvent!,(_,_)=>window.DispatcherQueue.TryEnqueue(()=>window.ShowGui()),null,Timeout.Infinite,false);
            }
        } catch(Exception error) {
            StartupDiagnostics.Write("Tray launch failed.",error);
            Environment.ExitCode=1;
            if(!smoke) StartupDiagnostics.ShowError(error);
            Exit();
        }
    }
}


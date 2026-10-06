using Microsoft.UI.Xaml;
namespace Snapcast.Tray;
internal static class Program {
    [STAThread]
    static int Main(string[] args) {
        try {
            StartupDiagnostics.Write("Starting tray application.");
            AppDomain.CurrentDomain.UnhandledException+=(_,e)=>StartupDiagnostics.Write("Unhandled process exception.",e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException+=(_,e)=>StartupDiagnostics.Write("Unobserved task failure.",e.Exception);
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(parameters=>{
                var context=new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _=new App();
            });
            return Environment.ExitCode;
        } catch(Exception error) {
            StartupDiagnostics.Write("Fatal startup exception.",error);
            if(!args.Contains("--smoke-test")) StartupDiagnostics.ShowError(error);
            return 1;
        }
    }
}


using System.IO;
using System.Windows;
using System.Threading;
namespace Sendspin.Windows.Tray;
public partial class App:System.Windows.Application {
 Mutex? instance;EventWaitHandle? showEvent;RegisteredWaitHandle? showWait;
 protected override void OnStartup(StartupEventArgs e) {
  base.OnStartup(e);
  DispatcherUnhandledException+=(_,error)=>{Diagnostics.Log("UI exception",error.Exception);System.Windows.MessageBox.Show(error.Exception.Message,"Sendspin Windows",MessageBoxButton.OK,MessageBoxImage.Error);error.Handled=true;};
  try {
   instance=new Mutex(true,"Local\\SendspinWindows.Tray",out var first);
   if(!first){try{using var existing=EventWaitHandle.OpenExisting("Local\\SendspinWindows.Show");existing.Set();}catch(Exception error){Diagnostics.Log("Activate existing controls",error);}Shutdown();return;}
   showEvent=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\SendspinWindows.Show");
   var window=new MainWindow();MainWindow=window;
   showWait=ThreadPool.RegisterWaitForSingleObject(showEvent,(_,_)=>Dispatcher.InvokeAsync(()=>window.OpenSettings()),null,Timeout.Infinite,false);
   window.StartTray();
   if(!e.Args.Contains("--background"))window.OpenSettings();
   Diagnostics.Log("Tray started. Settings and notification icon initialized.");
  }catch(Exception error){Diagnostics.Log("Tray startup failed",error);System.Windows.MessageBox.Show(error.Message,"Sendspin startup failed",MessageBoxButton.OK,MessageBoxImage.Error);Shutdown(1);}
 }
 protected override void OnExit(ExitEventArgs e){showWait?.Unregister(null);showEvent?.Dispose();instance?.Dispose();base.OnExit(e);}
}
internal static class Diagnostics {
 public static void Log(string message,Exception? error=null){try{var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SendspinWindows");Directory.CreateDirectory(dir);var file=Path.Combine(dir,"tray-startup.log");if(File.Exists(file)&&new FileInfo(file).Length>1048576)File.Move(file,file+".old",true);File.AppendAllText(file,$"{DateTimeOffset.Now:O} {message} {error}\n");}catch{}}
}

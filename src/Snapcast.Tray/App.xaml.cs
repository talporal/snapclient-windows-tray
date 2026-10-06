using Microsoft.UI.Xaml;
namespace Snapcast.Tray;
public partial class App : Application {
    MainWindow? window;
    Mutex? instance;
    public App() { InitializeComponent(); }
    protected override void OnLaunched(LaunchActivatedEventArgs args) { instance=new Mutex(true,"Local\\SnapcastWindows.Tray",out var created);if(!created){Exit();return;}window=new MainWindow();if(!Environment.GetCommandLineArgs().Contains("--background")) window.Activate(); }
}


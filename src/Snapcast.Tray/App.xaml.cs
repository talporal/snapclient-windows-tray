using Microsoft.UI.Xaml;
namespace Snapcast.Tray;
public partial class App : Application {
    MainWindow? window;
    public App() { InitializeComponent(); }
    protected override void OnLaunched(LaunchActivatedEventArgs args) { window=new MainWindow();if(!Environment.GetCommandLineArgs().Contains("--background")) window.Activate(); }
}


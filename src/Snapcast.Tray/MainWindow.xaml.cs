using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snapcast.Core;
using System.ComponentModel;
namespace Snapcast.Tray;
public sealed partial class MainWindow : Window {
    TrayIcon? tray;
    readonly DispatcherTimer timer;
    readonly bool smoke;
    Settings config=new(); bool loaded,busy,exiting,closed,controllingService;
    public MainWindow(bool smoke=false) {
        this.smoke=smoke;
        StartupDiagnostics.Write("Loading settings window XAML.");
        InitializeComponent();
        StartupDiagnostics.Write("Settings window XAML loaded.");
        timer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(5)};
    }
    public void InitializeTray(bool background) {
        // Initialize WinUI's native window before using its HWND or creating shell integration.
        Activate();
        StartupDiagnostics.Write("Settings window activated.");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(760,980));
        var iconPath=Path.Combine(AppContext.BaseDirectory,"Assets","Snapcast.ico");
        AppWindow.SetIcon(iconPath);
        AppWindow.Closing+=(_,e)=>{if(!closed){e.Cancel=true;HideGui();}};
        tray=new TrayIcon(DispatcherQueue,ShowGui,()=>_ = RestartService(),()=>_ = RequestExit());
        StartupDiagnostics.Write("Tray notification window initialized.");
        if(background) HideGui();
        if(!smoke){timer.Tick+=async(_,_)=>await Refresh();timer.Start();_=Refresh();}
    }
    public void ShowGui(){Activate();TrayIcon.ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(this),5);}
    void HideGui()=>TrayIcon.ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(this),0);
    public void RunSmokeChecks() {
        if(Content is null || tray is null || !tray.AssetsLoaded || !tray.NativeWindowReady) throw new InvalidOperationException("GUI/tray initialization incomplete.");
        if(Host is null || Transport is null || Status is null) throw new InvalidOperationException("Settings window controls did not load.");
        tray.Update(TrayState.Playing,"Snapcast: server stream playing");
        tray.Update(TrayState.Connected,"Snapcast: connected, idle");
        tray.Update(TrayState.Unavailable,"Snapcast: status unavailable");
        StartupDiagnostics.Write($"Tray asset and state checks passed. Shell registration: {tray.Registered}.");
    }
    public void Shutdown() {
        if(closed)return;closed=true;timer.Stop();tray?.Dispose();Close();Application.Current.Exit();
    }
    async Task<Response?> Call(Request request) {
        try {
            var result=await Protocol.SendAsync(request);
            if(closed)return result;
            Status.Title=result.Message;Status.Severity=result.Ok?InfoBarSeverity.Informational:InfoBarSeverity.Error;
            return result;
        } catch(Exception e) {
            if(!closed){Status.Title="Service unavailable: "+e.Message;Status.Severity=InfoBarSeverity.Error;tray?.Update(TrayState.Unavailable,"Snapcast: service unavailable");}
            return null;
        }
    }
    async Task Refresh() {
        if(busy||closed||controllingService)return;busy=true;
        try {
            var result=await Call(new("status"));
            if(result?.Ok!=true||closed)return;
            if(!loaded && result.Settings is not null) {
                config=result.Settings;Transport.SelectedItem=config.Transport;ControlTransport.SelectedItem=config.ControlTransport;Host.Text=config.Host;StreamPort.Value=config.StreamPort;ControlPort.Value=config.ControlPort;
                Soundcard.Text=config.Soundcard;ClientName.Text=config.Name;Latency.Value=config.Latency;SampleFormat.Text=config.SampleFormat;
                Exclusive.IsOn=config.Exclusive;Enabled.IsOn=config.Enabled;LogLevel.SelectedItem=config.LogLevel;loaded=true;
            }
            Logs.Text=string.Join(Environment.NewLine,result.Logs??[]);
            Status.Severity=result.Connected?InfoBarSeverity.Success:InfoBarSeverity.Warning;
            var state=result.Playing?TrayState.Playing:result.Connected?TrayState.Connected:TrayState.Disconnected;
            tray?.Update(state,result.Playing?"Snapcast: server stream playing":result.Connected?"Snapcast: connected, idle or muted":"Snapcast: "+result.Message);
        } finally {busy=false;}
    }
    async void Save(object sender,RoutedEventArgs e) {
        try {
            if(!loaded) throw new InvalidOperationException("Wait for the service configuration before saving.");
            if (double.IsNaN(StreamPort.Value) || double.IsNaN(ControlPort.Value) || double.IsNaN(Latency.Value)) throw new ArgumentException("Enter numeric port and latency values.");
            var updated=config with{Transport=Transport.SelectedItem?.ToString()??"tcp",ControlTransport=ControlTransport.SelectedItem?.ToString()??"tcp",Host=Host.Text.Trim(),StreamPort=checked((int)StreamPort.Value),ControlPort=checked((int)ControlPort.Value),Soundcard=Soundcard.Text.Trim(),Name=ClientName.Text.Trim(),Latency=checked((int)Latency.Value),SampleFormat=SampleFormat.Text.Trim(),Exclusive=Exclusive.IsOn,Enabled=Enabled.IsOn,LogLevel=LogLevel.SelectedItem?.ToString()??"info"};
            updated.Validate();
            var result=await Call(new("save",updated));if(result?.Ok==true){config=updated;}
        } catch(Exception error) {Status.Title=error.Message;Status.Severity=InfoBarSeverity.Error;}
    }
    async Task<bool> ControlService(string action) {
        if(controllingService)return false;controllingService=true;
        try {
            ShowGui();Status.Title=action=="restart"?"Restarting Windows service…":"Stopping Windows service…";
            await ServiceControl.Run(action);
            Status.Title=action=="restart"?"Windows service restarted.":"Windows service stopped.";
            tray?.Update(TrayState.Disconnected,"Snapcast: "+Status.Title);return true;
        } catch(Win32Exception error) when(error.NativeErrorCode==1223) {
            Status.Title="Administrator approval cancelled. Tray remains open.";return false;
        } catch(Exception error) {
            StartupDiagnostics.Write("Service control failed.",error);
            Status.Title=error.Message;Status.Severity=InfoBarSeverity.Error;return false;
        } finally {controllingService=false;}
    }
    async Task RestartService(){if(await ControlService("restart"))await Refresh();}
    async Task RequestExit() {
        if(exiting||controllingService||closed)return;exiting=true;
        try {
            ShowGui();
            var dialog=new ContentDialog {
                XamlRoot=Content.XamlRoot,
                Title="Exit Snapcast controls?",
                Content="Would you also like to stop the Windows audio service? Keeping it running allows playback to continue after the tray closes or you log out.",
                PrimaryButtonText="Exit tray only",
                SecondaryButtonText="Stop service and exit",
                CloseButtonText="Cancel",
                DefaultButton=ContentDialogButton.Close
            };
            var result=await dialog.ShowAsync();
            if(result==ContentDialogResult.Primary)Shutdown();
            else if(result==ContentDialogResult.Secondary && await ControlService("stop"))Shutdown();
        } catch(Exception error) {StartupDiagnostics.Write("Exit dialog failed.",error);Status.Title=error.Message;Status.Severity=InfoBarSeverity.Error;}
        finally {exiting=false;}
    }
    void MusicAssistantPreset(object sender,RoutedEventArgs e){Transport.SelectedIndex=1;ControlTransport.SelectedIndex=1;StreamPort.Value=1780;ControlPort.Value=1780;}
    async void Reconnect(object sender,RoutedEventArgs e)=>await Call(new("restart"));
    async void ListDevices(object sender,RoutedEventArgs e){var result=await Call(new("devices"));if(result?.Ok==true)DeviceList.Text=result.Devices;}
    async void ApplyVolume(object sender,RoutedEventArgs e)=>await Call(new("volume",Volume:(int)Volume.Value,Muted:Mute.IsOn));
    void Hide(object sender,RoutedEventArgs e)=>HideGui();
}

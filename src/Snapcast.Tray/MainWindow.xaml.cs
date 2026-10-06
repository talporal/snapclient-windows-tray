using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snapcast.Core;
namespace Snapcast.Tray;
public sealed partial class MainWindow : Window {
    readonly TrayIcon tray;
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(5)};
    Settings config=new(); bool loaded,busy;
    public MainWindow() {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(760,980));
        var hwnd=WinRT.Interop.WindowNative.GetWindowHandle(this);
        tray=new TrayIcon(DispatcherQueue,()=>{Activate();TrayIcon.ShowWindow(hwnd,5);},()=>{tray!.Dispose();Application.Current.Exit();},()=>TrayIcon.ShowWindow(hwnd,0));
        AppWindow.Closing+=(_,e)=>{e.Cancel=true;TrayIcon.ShowWindow(hwnd,0);};
        timer.Tick+=async(_,_)=>await Refresh();timer.Start();
        _=Refresh();
    }
    async Task<Response?> Call(Request request) {
        try {
            var result=await Protocol.SendAsync(request);
            Status.Title=result.Message;Status.Severity=result.Ok?InfoBarSeverity.Informational:InfoBarSeverity.Error;
            return result;
        } catch(Exception e) {Status.Title="Service unavailable: "+e.Message;Status.Severity=InfoBarSeverity.Error;return null;}
    }
    async Task Refresh() {
        if(busy)return;busy=true;
        try {
            var result=await Call(new("status"));
            if(result?.Ok!=true)return;
            if(!loaded && result.Settings is not null) {
                config=result.Settings;Transport.SelectedItem=config.Transport;ControlTransport.SelectedItem=config.ControlTransport;Host.Text=config.Host;StreamPort.Value=config.StreamPort;ControlPort.Value=config.ControlPort;
                Soundcard.Text=config.Soundcard;ClientName.Text=config.Name;Latency.Value=config.Latency;SampleFormat.Text=config.SampleFormat;
                Exclusive.IsOn=config.Exclusive;Enabled.IsOn=config.Enabled;LogLevel.SelectedItem=config.LogLevel;loaded=true;
            }
            Logs.Text=string.Join(Environment.NewLine,result.Logs??[]);
            Status.Severity=result.Connected?InfoBarSeverity.Success:InfoBarSeverity.Warning;
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
    void MusicAssistantPreset(object sender,RoutedEventArgs e){Transport.SelectedIndex=1;ControlTransport.SelectedIndex=1;StreamPort.Value=1780;ControlPort.Value=1780;}
    async void Reconnect(object sender,RoutedEventArgs e)=>await Call(new("restart"));
    async void ListDevices(object sender,RoutedEventArgs e){var result=await Call(new("devices"));if(result?.Ok==true)DeviceList.Text=result.Devices;}
    async void ApplyVolume(object sender,RoutedEventArgs e)=>await Call(new("volume",Volume:(int)Volume.Value,Muted:Mute.IsOn));
    void Hide(object sender,RoutedEventArgs e)=>TrayIcon.ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(this),0);
}


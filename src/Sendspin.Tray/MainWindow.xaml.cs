using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sendspin.Windows.Core;
using Forms=System.Windows.Forms;
using Drawing=System.Drawing;
namespace Sendspin.Windows.Tray;
public partial class MainWindow:Window {
 readonly Forms.NotifyIcon tray=new();readonly Dictionary<TrayState,Drawing.Icon> icons=new();
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(2)};
 Settings config=new();bool loaded,busy,closed,editing,listsChanging,devicesBusy;string? lastServerKey;string? lastDeviceId;Response? last;bool syncingVolume,volumeSending;long volumeRevision;
 public MainWindow(){InitializeComponent();Closing+=OnClosing;AppImage.Source=new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory,"Assets","App.ico")));Icon=AppImage.Source;timer.Tick+=async(_,_)=>await Refresh();}
 public void StartTray(){
  foreach(var state in Enum.GetValues<TrayState>())icons.Add(state,new Drawing.Icon(Path.Combine(AppContext.BaseDirectory,"Assets","Tray"+state+".ico")));
  tray.Icon=icons[TrayState.Disconnected];tray.Text="Sendspin Windows · checking service";
  var menu=new Forms.ContextMenuStrip();menu.Items.Add("Settings",null,(_,_)=>Dispatcher.InvokeAsync(()=>OpenSettings()));menu.Items.Add("Restart service",null,(_,_)=>Dispatcher.InvokeAsync(async()=>await ControlService("restart")));menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("Exit app…",null,(_,_)=>Dispatcher.InvokeAsync(async()=>await ExitTray()));
  tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>Dispatcher.InvokeAsync(()=>OpenSettings());tray.Visible=true;timer.Start();_=Refresh();
 }
 public void OpenSettings(){Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();ServerSettings.Visibility=AudioSettings.Visibility=DiagnosticsPanel.Visibility=Visibility.Collapsed;if(loaded)_=LoadDevices();}
 void OnClosing(object? sender,CancelEventArgs e){if(!closed){e.Cancel=true;Hide();}}
 async Task<Response?> Call(Request request,bool updateStatus=true){try{var result=await Protocol.SendAsync(request);if(updateStatus||!result.Ok)Status.Text=result.Message;return result;}catch(Exception e){Status.Text="Service unavailable: "+e.Message;tray.Icon=icons.GetValueOrDefault(TrayState.Unavailable);Diagnostics.Log("Service control",e);return null;}}
 async Task Refresh(){
  if(busy||closed||editing)return;busy=true;
  try {
   long requestedVolumeRevision=volumeRevision;var response=await Call(new("status"));if(response?.Ok!=true)return;last=response;
   if(!loaded&&response.Settings is not null){config=response.Settings;Host.Text=config.Host;Port.Text=config.Port.ToString();Endpoint.Text=config.Path;PlayerName.Text=config.Name;EnableDiscovery.IsChecked=config.Discover;Enabled.IsChecked=config.Enabled;OutputBuffer.Text=config.OutputLatencyMs.ToString();NetworkBuffer.Text=config.MinBufferMs.ToString();SetVolumeControls(response.Volume,response.Muted);loaded=true;await LoadDevices();}
   Logs.Text=string.Join(Environment.NewLine,response.Logs??[]);Stats.Text=response.Playing?"Playing":response.Connected?"Connected · waiting for audio":"Not connected";
   DiagnosticStats.Text=$"Audio frames rendered: {response.FramesRendered:N0}";
   if(!volumeSending&&requestedVolumeRevision==volumeRevision)SetVolumeControls(response.Volume,response.Muted);
   UpdateSummary();
   UpdateServers(response.Servers??[]);
   var state=response.Playing?TrayState.Playing:response.Connected?TrayState.Connected:TrayState.Disconnected;tray.Icon=icons[state];tray.Text=response.Playing?"Sendspin Windows · rendering audio":response.Connected?"Sendspin Windows · connected":"Sendspin Windows · disconnected";
  }finally{busy=false;}
 }
 void UpdateServers(ServerEntry[] servers){var key=string.Join("|",servers.Select(s=>$"{s.Name}:{s.Host}:{s.Port}:{s.Path}"));if(key==lastServerKey)return;lastServerKey=key;listsChanging=true;try{var selected=Servers.SelectedItem as ServerEntry;Servers.ItemsSource=servers;if(selected is not null)Servers.SelectedItem=servers.FirstOrDefault(s=>s.Host==selected.Host&&s.Port==selected.Port&&s.Path==selected.Path);}finally{listsChanging=false;}}
 async Task LoadDevices(){
  if(devicesBusy)return;devicesBusy=true;
  try {
   DeviceSummary.Text="Refreshing outputs from the Windows service…";
   var r=await Call(new("devices"));
   if(r?.Ok!=true){DeviceSummary.Text="Could not list service outputs: "+(r?.Message??Status.Text);ServiceOutputs.Text=DeviceSummary.Text;return;}
   var items=r.Devices??[];var physical=items.Where(d=>d.Id!="").ToArray();
   DeviceSummary.Text=$"Service reports {physical.Length} active output(s). This list comes from the service, including when controls run over RDP.";
   ServiceOutputs.Text=string.Join(Environment.NewLine+Environment.NewLine,items.Select(d=>d.Id==""?d.Name:d.Name+Environment.NewLine+"Device ID: "+d.Id));
   if(physical.Length==0)ServiceOutputs.Text+=Environment.NewLine+Environment.NewLine+"No named active outputs were returned by the service.";
   lastDeviceId=(Devices.SelectedItem as DeviceEntry)?.Id??config.DeviceId;Devices.ItemsSource=items;Devices.SelectedItem=items.FirstOrDefault(d=>d.Id==lastDeviceId);
   if(Devices.SelectedItem is null){var missing=new DeviceEntry(lastDeviceId,"Saved output is unavailable · choose another device");Devices.ItemsSource=items.Append(missing).ToArray();Devices.SelectedItem=missing;}
   UpdateSummary();
  }finally{devicesBusy=false;}
 }
 void ChooseServer(object sender,SelectionChangedEventArgs e){if(listsChanging)return;if(Servers.SelectedItem is ServerEntry s){Host.Text=s.Host;Port.Text=s.Port.ToString();Endpoint.Text=s.Path;}}
 async void Discover(object sender,RoutedEventArgs e){var r=await Call(new("discover"));if(r?.Ok==true){UpdateServers(r.Servers??[]);Status.Text=(r.Servers?.Length??0)==0?"No server discovered yet. Check LAN/multicast connectivity or enter an address.":"Choose a discovered server above.";}}
 void AutoSelect(object sender,RoutedEventArgs e){Host.Text="";EnableDiscovery.IsChecked=true;Servers.SelectedItem=null;Status.Text="Save to automatically connect when exactly one server is discovered.";}
 async void RefreshDevices(object sender,RoutedEventArgs e)=>await LoadDevices();
 Settings ReadSettings(string section){
  if(!loaded)throw new InvalidOperationException("Wait for the Windows service settings to load.");
  var value=config with{Volume=(int)Volume.Value,Muted=Mute.IsChecked==true};
  if(section=="server"){
   if(!int.TryParse(Port.Text,out var port))throw new ArgumentException("Enter a numeric server port.");
   value=value with{Host=Host.Text.Trim(),Port=port,Path=Endpoint.Text.Trim(),Name=PlayerName.Text.Trim(),Discover=EnableDiscovery.IsChecked==true,Enabled=Enabled.IsChecked==true};
  }else{
   if(!int.TryParse(OutputBuffer.Text,out var output)||!int.TryParse(NetworkBuffer.Text,out var network))throw new ArgumentException("Enter numeric audio buffer values.");
   value=value with{DeviceId=(Devices.SelectedItem as DeviceEntry)?.Id??config.DeviceId,OutputLatencyMs=output,MinBufferMs=network};
  }
  value.Validate();return value;
 }
 async Task<bool> SaveSettings(string section){try{var value=ReadSettings(section);var r=await Call(new("save",value));if(r?.Ok==true){config=value;return true;}return false;}catch(Exception e){Status.Text=e.Message;return false;}}
 async void Save(object sender,RoutedEventArgs e){if(editing)return;editing=true;var button=(Button)sender;button.IsEnabled=false;try{if(await SaveSettings(button.Tag as string??"audio")){ServerSettings.Visibility=AudioSettings.Visibility=Visibility.Collapsed;UpdateSummary();}}finally{editing=false;button.IsEnabled=true;}}
 async void TestSpeakers(object sender,RoutedEventArgs e){if(editing)return;editing=true;try{if(await SaveSettings("audio")){UpdateSummary();await Call(new("test-audio"));}}finally{editing=false;}}
 void ToggleServerSettings(object sender,RoutedEventArgs e){AudioSettings.Visibility=Visibility.Collapsed;ServerSettings.Visibility=ServerSettings.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;}
 async void ToggleAudioSettings(object sender,RoutedEventArgs e){ServerSettings.Visibility=Visibility.Collapsed;AudioSettings.Visibility=AudioSettings.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;if(AudioSettings.Visibility==Visibility.Visible)await LoadDevices();}
 void ToggleDiagnostics(object sender,RoutedEventArgs e)=>DiagnosticsPanel.Visibility=DiagnosticsPanel.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;
 void UpdateSummary(){
  ServerInfo.Text=config.Host==""?"Automatic server discovery":$"{config.Host}:{config.Port}";
  ServerInfo.Text+=" · "+config.Name+(config.Enabled?"":" · playback disabled");
  OutputInfo.Text=(Devices.ItemsSource as IEnumerable<DeviceEntry>)?.FirstOrDefault(d=>d.Id==config.DeviceId)?.Name??(config.DeviceId==""?"Service default output":"Saved output unavailable");
 }
 void SetVolumeControls(int value,bool muted){syncingVolume=true;try{Volume.Value=value;Mute.IsChecked=muted;if(VolumePercent is not null)VolumePercent.Text=$"{value}%";}finally{syncingVolume=false;}}
 void VolumeChanged(object sender,RoutedPropertyChangedEventArgs<double> e){if(VolumePercent is not null)VolumePercent.Text=$"{(int)e.NewValue}%";QueueVolume();}
 void MuteChanged(object sender,RoutedEventArgs e)=>QueueVolume();
 void QueueVolume(){if(!loaded||syncingVolume||closed)return;volumeRevision++;if(!volumeSending)_=SendVolume();}
 async Task SendVolume(){
  volumeSending=true;
  try{while(!closed){long version=volumeRevision;int value=(int)Volume.Value;bool muted=Mute.IsChecked==true;var response=await Call(new("volume",Volume:value,Muted:muted),false);if(response?.Ok==true)config=config with{Volume=value,Muted=muted};await Task.Delay(60);if(version==volumeRevision)break;}}
  finally{volumeSending=false;}
 }
 async void Restart(object sender,RoutedEventArgs e)=>await ControlService("restart");
 void Hide(object sender,RoutedEventArgs e)=>Hide();
 async Task<bool> ControlService(string action){
  if(editing)return false;editing=true;OpenSettings();
  try {
   var exe=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","Service","Sendspin.Service.exe"));
   using var p=Process.Start(new ProcessStartInfo(exe,"--control-service "+action){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden})??throw new IOException("Could not start the service helper.");
   using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(75));await p.WaitForExitAsync(timeout.Token);if(p.ExitCode!=0)throw new IOException("Service operation failed. Check Windows service status.");Status.Text=action=="restart"?"Sendspin service restarted.":"Sendspin service stopped.";return true;
  }catch(Win32Exception e) when(e.NativeErrorCode==1223){Status.Text="Administrator approval cancelled.";return false;}catch(Exception e){Diagnostics.Log("Service operation failed",e);Status.Text=e.Message;return false;}finally{editing=false;}
 }
 async Task ExitTray(){
  if(editing||closed)return;OpenSettings();
  var choice=MessageBox.Show(this,"Also stop the audio service?\n\nYes: stop service and exit.\nNo: exit tray and keep audio running.\nCancel: keep controls open.","Exit Sendspin controls",MessageBoxButton.YesNoCancel,MessageBoxImage.Question,MessageBoxResult.Cancel);
  if(choice==MessageBoxResult.Cancel)return;if(choice==MessageBoxResult.Yes&&!await ControlService("stop"))return;
  closed=true;timer.Stop();tray.Visible=false;tray.ContextMenuStrip?.Dispose();tray.Dispose();foreach(var icon in icons.Values)icon.Dispose();Close();System.Windows.Application.Current.Shutdown();
 }
}

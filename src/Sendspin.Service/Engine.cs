using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sendspin.SDK.Audio;
using Sendspin.SDK.Client;
using Sendspin.SDK.Connection;
using Sendspin.SDK.Discovery;
using Sendspin.SDK.Synchronization;
using Sendspin.Windows.Core;
namespace Sendspin.Windows.Service;
public sealed class Engine {
 readonly object gate=new();readonly ConcurrentQueue<string> logs=new();readonly ILoggerFactory loggerFactory;
 readonly string file=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"SendspinWindows","settings.json");
 Settings settings=new();long revision;int testRequested;volatile bool connected,playing;long frames;string message="Starting Sendspin discovery";
 readonly MdnsServerDiscovery discovery;
 public Engine() {
  loggerFactory=LoggerFactory.Create(b=>b.SetMinimumLevel(LogLevel.Information).AddProvider(new RingLogProvider(Log)));
  discovery=new(loggerFactory.CreateLogger<MdnsServerDiscovery>());
  discovery.ServerFound+=(_,s)=>Log("Discovered "+s.Name+" at "+s.Host+":"+s.Port);
  Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
  if(File.Exists(file)){try{settings=JsonSerializer.Deserialize<Settings>(File.ReadAllText(file),Protocol.Json)??new();settings.Validate();}catch(Exception e){Log("Settings: "+e.Message);settings=new(){Enabled=false};}}
  // Persist a stable player ID before the first network connection.
  if(!File.Exists(file))File.WriteAllText(file,JsonSerializer.Serialize(settings,Protocol.Json));
 }
 public Settings Config {get{lock(gate)return settings;}}
 public long Revision=>Interlocked.Read(ref revision);
 public void Save(Settings value){value.Validate();lock(gate){value=value with{ClientId=settings.ClientId};File.WriteAllText(file+".tmp",JsonSerializer.Serialize(value,Protocol.Json));File.Move(file+".tmp",file,true);settings=value;Interlocked.Increment(ref revision);}}
 public void Restart()=>Interlocked.Increment(ref revision);
 public void TestAudio(){Interlocked.Exchange(ref testRequested,1);Restart();}
 public void Log(string text){logs.Enqueue($"{DateTimeOffset.Now:HH:mm:ss} {text}");while(logs.Count>200)logs.TryDequeue(out _);}
 ServerEntry ToEntry(DiscoveredServer s){var address=s.IpAddresses.FirstOrDefault(a=>System.Net.IPAddress.TryParse(a,out var ip)&&ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork)??s.Host;var path=s.Properties.TryGetValue("path",out var p)?p:"/sendspin";try{(new Settings{Host=address,Port=s.Port,Path=path}).Validate();}catch{path="/sendspin";}return new(s.Name,address,s.Port,path);}
 public ServerEntry[] Servers=>!Config.Discover?[]:discovery.Servers.OrderBy(s=>s.Name).Take(50).Select(ToEntry).ToArray();
 public Response Status(){var config=Config;return new(true,Volatile.Read(ref message),config,connected,playing,logs.ToArray(),Servers,FramesRendered:Interlocked.Read(ref frames),Volume:config.Volume,Muted:config.Muted);}
 void State(string text){Volatile.Write(ref message,text);Log(text);}
 public async Task Run(CancellationToken token) {
  try {
   while(!token.IsCancellationRequested) {
    var config=Config;var generation=Revision;
    try {if(config.Discover&&!discovery.IsDiscovering)await discovery.StartAsync(token);else if(!config.Discover&&discovery.IsDiscovering)await discovery.StopAsync();}catch(Exception e){StateOnce("Discovery unavailable: "+e.Message);}
    if(Interlocked.Exchange(ref testRequested,0)==1) {
     State("Playing a two-second test tone through the service output");
     try{await using var output=new WasapiPlayer(config.DeviceId,config.OutputLatencyMs,Log,_=>{});var tone=new ToneSource();await output.InitializeAsync(tone.Format,token);output.SetSampleSource(tone);output.Play();await Task.Delay(2000,token);output.Stop();State("Test tone finished. Confirm sound at the selected speakers.");}catch(Exception e){State("Speaker test failed: "+e.Message);}
    }
    if(!config.Enabled){StateOnce("Playback disabled");await Task.Delay(1000,token);continue;}
    Uri? server=null;
    if(config.Host!="")server=config.ServerUri();
    else if(config.Discover){var candidates=Servers;if(candidates.Length==1){var s=candidates[0];server=(config with{Host=s.Host,Port=s.Port,Path=s.Path}).ServerUri();}}
    if(server is null){StateOnce(Servers.Length>1?"Several servers found. Select one in Settings.":"Searching for a Sendspin server; you can also enter an address.");await Task.Delay(1000,token);continue;}
    await using var session=new Session(loggerFactory,config,Log,n=>Interlocked.Add(ref frames,n));
    try {
     connected=false;playing=false;State("Connecting to "+server);
     session.Client.ConnectionStateChanged+=(_,e)=>{connected=e.NewState==ConnectionState.Connected;if(!connected)playing=false;Log("Connection: "+e.NewState+" "+e.Reason);};
     session.Pipeline.StateChanged+=(_,state)=>{playing=state==AudioPipelineState.Playing;Volatile.Write(ref message,playing?"Sendspin stream rendering to Windows audio":"Connected to Sendspin · "+state);};
     session.Pipeline.ErrorOccurred+=(_,e)=>{playing=false;State("Audio error: "+e.Message);};
     session.Client.PlayerStateChanged+=(_,s)=>{lock(gate)settings=settings with{Volume=s.Volume,Muted=s.Muted};};
     await session.Client.ConnectAsync(server,token);connected=true;State("Connected to "+(session.Client.ServerName??server.Host));
     await session.Client.SendPlayerStateAsync(config.Volume,config.Muted);
     int volume=config.Volume;bool muted=config.Muted;
     while(!token.IsCancellationRequested && Revision==generation && session.Client.ConnectionState!=ConnectionState.Disconnected) {
      var current=Config;session.Pipeline.SetVolume(current.Volume);session.Pipeline.SetMuted(current.Muted);
      if(current.Volume!=volume||current.Muted!=muted){await session.Client.SendPlayerStateAsync(current.Volume,current.Muted);volume=current.Volume;muted=current.Muted;}
      await Task.Delay(500,token);
     }
    }catch(OperationCanceledException) when(token.IsCancellationRequested){break;}catch(Exception e){State("Sendspin: "+e.Message);}
    finally {connected=false;playing=false;}
    if(Revision==generation)await Task.Delay(3000,token);
   }
  }catch(OperationCanceledException) when(token.IsCancellationRequested){}
  finally{connected=false;playing=false;await discovery.DisposeAsync();loggerFactory.Dispose();}
 }
 void StateOnce(string text){if(Volatile.Read(ref message)!=text)State(text);}
 public void Volume(int percent,bool mute){if(percent is <0 or >100)throw new ArgumentException("Volume must be 0–100.");lock(gate){settings=settings with{Volume=percent,Muted=mute};File.WriteAllText(file+".tmp",JsonSerializer.Serialize(settings,Protocol.Json));File.Move(file+".tmp",file,true);}}
}
public sealed class Session:IAsyncDisposable {
 public SendspinClientService Client {get;}
 public AudioPipeline Pipeline {get;}
 public Session(ILoggerFactory log,Settings config,Action<string> diagnostic,Action<int> frames,Func<IAudioPlayer>? outputFactory=null) {
  var clock=new KalmanClockSynchronizer(log.CreateLogger<KalmanClockSynchronizer>());
  Pipeline=new(log.CreateLogger<AudioPipeline>(),new AudioDecoderFactory(log),clock,
   (format,sync)=>new TimedAudioBuffer(format,sync,logger:log.CreateLogger<TimedAudioBuffer>()){TargetBufferMilliseconds=config.MinBufferMs},
   outputFactory??(()=>new WasapiPlayer(config.DeviceId,config.OutputLatencyMs,diagnostic,frames)),
   (buffer,time)=>new TimedSource(buffer,time));
  var connection=new SendspinConnection(log.CreateLogger<SendspinConnection>());
  Client=new(log.CreateLogger<SendspinClientService>(),connection,clock,new ClientCapabilities {
   ClientId="sendspin-windows-"+config.ClientId,ClientName=config.Name,ProductName="Sendspin Windows Speaker",Manufacturer="Sendspin Windows",SoftwareVersion="0.2.0",
   Roles=["player@v1"],RequiredLeadTimeMs=200,MinBufferMs=config.MinBufferMs,ExpectedOutputLatencyMs=config.OutputLatencyMs,InitialVolume=config.Volume,InitialMuted=config.Muted
  },Pipeline);
 }
 public async ValueTask DisposeAsync(){await Client.DisposeAsync();await Pipeline.DisposeAsync();}
}
internal sealed class RingLogProvider(Action<string> log):ILoggerProvider {
 public ILogger CreateLogger(string categoryName)=>new RingLogger(log);
 public void Dispose(){}
 sealed class RingLogger(Action<string> log):ILogger {
  public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
  public bool IsEnabled(LogLevel level)=>level>=LogLevel.Information;
  public void Log<TState>(LogLevel level,EventId id,TState state,Exception? exception,Func<TState,Exception?,string> formatter){if(IsEnabled(level))log(formatter(state,exception));}
 }
}

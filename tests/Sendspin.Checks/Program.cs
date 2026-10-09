using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sendspin.SDK.Audio;
using Sendspin.SDK.Models;
using Sendspin.Windows.Core;
using Sendspin.Windows.Service;
static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
static void Reject(Settings settings){try{settings.Validate();throw new Exception("Invalid settings accepted.");}catch(ArgumentException){}}
var settings=new Settings{Host="192.168.1.100",Name="Windows test speaker"};settings.Validate();
Check(settings.ServerUri().ToString()=="ws://192.168.1.100:8927/sendspin","Wrong default endpoint.");
Check((settings with{Host="2001:db8::1"}).ServerUri().Host.Contains("2001:db8::1"),"IPv6 URI invalid.");
Reject(settings with{Host="http://host"});Reject(settings with{Port=0});Reject(settings with{Path="//other-server"});Reject(settings with{Path="/sendspin?token=secret"});Reject(settings with{OutputLatencyMs=1});Reject(settings with{Volume=101});
var persisted=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings,Protocol.Json),Protocol.Json)!;
Check(persisted.ClientId==settings.ClientId,"Player identity did not round-trip.");
using(var decoder=new AudioDecoderFactory().Create(new(){Codec="pcm",SampleRate=48000,Channels=2,BitDepth=16})) {
 byte[] bytes=[0,64,0,192];float[] samples=new float[4];var read=decoder.Decode(bytes,samples);Check(read==2 && Math.Abs(samples[0]-0.5f)<0.001 && Math.Abs(samples[1]+0.5f)<0.001,"PCM samples/endianness incorrect.");
}
var rendered=0;var provider=new PullProvider(new PartialSource(),()=>0.5f,n=>rendered+=n);float[] output=Enumerable.Repeat(9f,10).ToArray();
Check(provider.Read(output,2,6)==6,"Audio callback stopped on short read.");Check(output[0]==9&&output[8]==9,"Callback wrote outside its requested range.");Check(output[2]==0.5f&&output[3]==-0.5f&&output.Skip(4).Take(4).All(v=>v==0),"Gain or silence padding failed.");Check(rendered==1,"Rendered frame count invalid.");
var muted=new PullProvider(new PartialSource(),()=>0,_=>{});muted.Read(output,2,6);Check(output.Skip(2).Take(6).All(v=>v==0),"Mute did not silence audio.");
Console.WriteLine("PASS: settings validation, identity persistence, PCM decoding and audio callback gain/mute/underrun checks.");
// Exercise fresh sessions after abrupt loss, graceful close, and a silent half-open peer.
for(int recoveryCase=0;recoveryCase<3;recoveryCase++) {
var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;
using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(25));
using var logging=LoggerFactory.Create(b=>b.AddConsole().SetMinimumLevel(LogLevel.Warning));
var fake=new FakePlayer();await using var session=new Session(logging,settings,_=>{},_=>{},()=>fake,TimeSpan.FromSeconds(3));
var connect=session.Client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/sendspin"),deadline.Token);
using var accepted=await listener.AcceptTcpClientAsync(deadline.Token);var stream=accepted.GetStream();
var head=new StringBuilder();byte[] one=new byte[1];while(!head.ToString().EndsWith("\r\n\r\n")){Check(head.Length<16384,"Oversize WebSocket upgrade.");Check(await stream.ReadAsync(one,deadline.Token)==1,"Socket closed during upgrade.");head.Append((char)one[0]);}
var key=head.ToString().Split("\r\n").First(s=>s.StartsWith("Sec-WebSocket-Key:",StringComparison.OrdinalIgnoreCase)).Split(':',2)[1].Trim();
var accept=Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key+"258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"),deadline.Token);
using var socket=WebSocket.CreateFromStream(stream,true,null,TimeSpan.FromSeconds(30));
byte[] receive=new byte[65536];var received=await socket.ReceiveAsync(receive.AsMemory(),deadline.Token);
Check(received.EndOfMessage,"Unexpected fragmented test hello.");using var hello=JsonDocument.Parse(receive.AsMemory(0,received.Count));var payload=hello.RootElement.GetProperty("payload");
Check(hello.RootElement.GetProperty("type").GetString()=="client/hello","Missing Sendspin client hello.");
Check(payload.GetProperty("client_id").GetString()=="sendspin-windows-"+settings.ClientId,"Configured player identity was not used.");
Check(payload.GetProperty("supported_roles").EnumerateArray().Select(e=>e.GetString()).SequenceEqual(new[]{"player@v1"}),"Unexpected non-player role advertised.");
async Task Send(string text)=>await socket.SendAsync(Encoding.UTF8.GetBytes(text).AsMemory(),WebSocketMessageType.Text,true,deadline.Token);
await Send("{\"type\":\"server/hello\",\"payload\":{\"server_id\":\"local-check\",\"name\":\"Test Sendspin server\",\"version\":1,\"active_roles\":[\"player@v1\"]}}");
await connect;
Check(session.Client.ServerName=="Test Sendspin server","Server handshake did not complete.");
await Send("{\"type\":\"stream/start\",\"payload\":{\"player\":{\"codec\":\"pcm\",\"sample_rate\":48000,\"channels\":2,\"bit_depth\":16}}}");
for(int i=0;i<100&&!session.Pipeline.IsReady;i++)await Task.Delay(20,deadline.Token);
Check(session.Pipeline.IsReady&&fake.Source is not null,"Service pipeline did not initialize its output backend.");
var chunk=new byte[9+1920];chunk[0]=4;BinaryPrimitives.WriteInt64BigEndian(chunk.AsSpan(1,8),DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()*1000+500000);
for(int i=9;i<chunk.Length;i+=4){chunk[i]=0;chunk[i+1]=64;chunk[i+2]=0;chunk[i+3]=192;}
await socket.SendAsync(chunk.AsMemory(),WebSocketMessageType.Binary,true,deadline.Token);
for(int i=0;i<100&&(session.Pipeline.BufferStats?.TotalSamplesWritten??0)==0;i++)await Task.Delay(20,deadline.Token);
Check((session.Pipeline.BufferStats?.TotalSamplesWritten??0)>0,"Binary Sendspin audio did not reach the service's decoded buffer.");
Console.WriteLine("PASS: real WebSocket Sendspin handshake, player-only capability, stable ID, stream initialization and binary PCM audio dispatch.");
Check(!session.NeedsReconnect,"Healthy session was marked for reconnect.");
if(recoveryCase==0)socket.Abort();
else if(recoveryCase==1)
 await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"server restart",deadline.Token);
else {
 // Keep TCP open but stop answering time probes: the same watchdog covers net8 half-open sockets.
 await Task.Delay(3200,deadline.Token);
 Check(session.Client.ConnectionState==Sendspin.SDK.Connection.ConnectionState.Connected,
  "Silent-peer check must exercise a transport still marked Connected.");
}
for(int i=0;i<100&&!session.NeedsReconnect;i++)await Task.Delay(20,deadline.Token);
Check(session.NeedsReconnect,"Connection loss did not request a fresh session.");
await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8),deadline.Token);
Check(fake.State==AudioPlayerState.Stopped,"Failed session left its audio output running.");
socket.Abort();listener.Stop();
Console.WriteLine($"PASS: recovery case {recoveryCase}: loss detection and bounded cleanup; next iteration creates a fresh handshake/audio pipeline.");
}
sealed class PartialSource:IAudioSampleSource {
 public AudioFormat Format {get;}=new(){Codec="pcm",SampleRate=48000,Channels=2,BitDepth=16};
 public int Read(float[] buffer,int offset,int count){buffer[offset]=1;buffer[offset+1]=-1;return 2;}
}
sealed class FakePlayer:IAudioPlayer {
 public IAudioSampleSource? Source {get;private set;}
 public AudioPlayerState State {get;private set;}
 public float Volume {get;set;}=1;public bool IsMuted {get;set;}public int OutputLatencyMs=>100;
 public event EventHandler<AudioPlayerState>? StateChanged;public event EventHandler<AudioPlayerError>? ErrorOccurred;
 public Task InitializeAsync(AudioFormat format,CancellationToken ct=default){State=AudioPlayerState.Stopped;return Task.CompletedTask;}
 public void SetSampleSource(IAudioSampleSource source)=>Source=source;
 public void Play(){State=AudioPlayerState.Playing;StateChanged?.Invoke(this,State);}
 public void Pause()=>State=AudioPlayerState.Paused;
 public void Stop()=>State=AudioPlayerState.Stopped;
 public Task SwitchDeviceAsync(string? id,CancellationToken ct=default)=>Task.CompletedTask;
 public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
}

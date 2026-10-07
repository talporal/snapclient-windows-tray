using System.IO.Pipes;
using System.Text.Json;
namespace Sendspin.Windows.Core;
public record Settings {
 public string Host {get;init;}="";
 public int Port {get;init;}=8927;
 public string Path {get;init;}="/sendspin";
 public string DeviceId {get;init;}="";
 public string ClientId {get;init;}=Guid.NewGuid().ToString("N");
 public string Name {get;init;}=Environment.MachineName;
 public bool Enabled {get;init;}=true;
 public bool Discover {get;init;}=true;
 public int OutputLatencyMs {get;init;}=100;
 public int MinBufferMs {get;init;}=150;
 public int Volume {get;init;}=75;
 public bool Muted {get;init;}
 public void Validate() {
  if(Host.Length>253 || Host.Any(c=>char.IsWhiteSpace(c)||"/\\\"".Contains(c)) || Host.Length>0 && Uri.CheckHostName(Host)==UriHostNameType.Unknown)throw new ArgumentException("Enter a hostname or IP address without a URL or port.");
  if(Port is <1 or >65535)throw new ArgumentException("Port must be 1–65535.");
  if(!Path.StartsWith('/') || Path.StartsWith("//") || Path.Length>512 || Path.Any(c=>char.IsControl(c)||char.IsWhiteSpace(c)||"?#\\".Contains(c)))throw new ArgumentException("Enter an endpoint path such as /sendspin.");
  if(Name.Length is <1 or >128 || Name.Any(char.IsControl))throw new ArgumentException("Player name must be 1–128 characters.");
  if(ClientId.Length!=32 || !ClientId.All(Uri.IsHexDigit))throw new ArgumentException("Invalid client identity.");
  if(DeviceId.Length>1024 || DeviceId.Any(char.IsControl))throw new ArgumentException("Invalid audio device.");
  if(OutputLatencyMs is <20 or >500 || MinBufferMs is <50 or >2000 || Volume is <0 or >100)throw new ArgumentException("Audio buffer: 20–500 ms; network buffer: 50–2000 ms; volume: 0–100.");
 }
 public Uri ServerUri() {Validate();if(Host=="")throw new ArgumentException("Select a discovered server or enter an address.");return new UriBuilder("ws",Host,Port,Path).Uri;}
}
public record ServerEntry(string Name,string Host,int Port,string Path) {public override string ToString()=>$"{Name} · {Host}:{Port}";}
public record DeviceEntry(string Id,string Name) {public override string ToString()=>Name;}
public record Request(string Command,Settings? Settings=null,int Volume=75,bool Muted=false);
public record Response(bool Ok,string Message,Settings? Settings=null,bool Connected=false,bool Playing=false,string[]? Logs=null,ServerEntry[]? Servers=null,DeviceEntry[]? Devices=null,long FramesRendered=0,int Volume=75,bool Muted=false);
public enum TrayState {Playing,Connected,Disconnected,Unavailable}
public static class Protocol {
 public const string PipeName="SendspinWindows.Control.v1";
 public static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
 public static async Task<Response> SendAsync(Request request) {
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
  using var pipe=new NamedPipeClientStream(".",PipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
  await pipe.ConnectAsync(timeout.Token);
  using var writer=new StreamWriter(pipe,leaveOpen:true){AutoFlush=true};using var reader=new StreamReader(pipe,leaveOpen:true);
  await writer.WriteLineAsync(JsonSerializer.Serialize(request,Json).AsMemory(),timeout.Token);
  var line=await reader.ReadLineAsync(timeout.Token)??throw new IOException("Service closed the connection.");
  return JsonSerializer.Deserialize<Response>(line,Json)??throw new IOException("Invalid service response.");
 }
}

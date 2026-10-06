using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
namespace Snapcast.Core;
public record Settings {
    public string Host { get; init; } = "";
    public int StreamPort { get; init; } = 1704;
    public int ControlPort { get; init; } = 1705;
    public string Soundcard { get; init; } = "default";
    public string ClientId { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = Environment.MachineName;
    public int Latency { get; init; }
    public string SampleFormat { get; init; } = "";
    public bool Exclusive { get; init; }
    public bool Enabled { get; init; } = true;
    public string LogLevel { get; init; } = "info";
    public void Validate() {
        if (Host.Length > 253 || Host.Any(c => char.IsWhiteSpace(c) || "/\\\"".Contains(c)) || (Host.Length > 0 && Uri.CheckHostName(Host) == UriHostNameType.Unknown)) throw new ArgumentException("Enter a hostname or IP address, without a port or URL.");
        if (StreamPort is < 1 or > 65535 || ControlPort is < 1 or > 65535) throw new ArgumentException("Ports must be 1–65535.");
        if (Latency is < -10000 or > 10000) throw new ArgumentException("Latency must be between -10000 and 10000 ms.");
        if (Soundcard.Length > 1024 || Name.Length > 128 || ClientId.Length != 32 || !ClientId.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid device, name or client identity.");
        if (!new[]{"trace","debug","info","notice","warning","error"}.Contains(LogLevel)) throw new ArgumentException("Invalid log level.");
        if (SampleFormat != "" && !System.Text.RegularExpressions.Regex.IsMatch(SampleFormat, @"^(\*|[0-9]{4,6}):(\*|16|24|32):(\*|[1-8])$")) throw new ArgumentException("Sample format must be rate:bits:channels, for example 48000:16:2.");
    }
    public string[] Arguments() {
        Validate();
        var args = new List<string>{"--host",Host,"--port",StreamPort.ToString(),"--hostID",ClientId,"--player","wasapi","--soundcard",Soundcard,"--latency",Latency.ToString(),"--sharingmode",Exclusive?"exclusive":"shared","--logsink","stdout","--logfilter","*:"+LogLevel};
        if (SampleFormat != "") args.AddRange(new[]{"--sampleformat",SampleFormat});
        return args.ToArray();
    }
}
public record Request(string Command, Settings? Settings = null, int Volume = 50, bool Muted = false);
public record Response(bool Ok, string Message, Settings? Settings = null, bool ProcessRunning = false, bool Connected = false, string[]? Logs = null, string? Devices = null);
public static class Protocol {
    public const string PipeName = "SnapcastWindows.Control.v1";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static async Task<Response> SendAsync(Request request) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        using var writer = new StreamWriter(pipe, leaveOpen:true){AutoFlush=true};
        using var reader = new StreamReader(pipe, leaveOpen:true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request,Json).AsMemory(),timeout.Token);
        var line = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("Service closed the connection.");
        return JsonSerializer.Deserialize<Response>(line,Json) ?? throw new IOException("Invalid service response.");
    }
}


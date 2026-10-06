using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Snapcast.Core;
namespace Snapcast.Service;
public sealed class Engine {
    readonly object gate = new();
    readonly SemaphoreSlim rpcGate = new(1,1);
    readonly ConcurrentQueue<string> logs = new();
    readonly string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"SnapcastWindows","settings.json");
    public readonly string Exe = Path.Combine(AppContext.BaseDirectory,"engine","snapclient.exe");
    Settings settings = new();
    Process? process;
    long revision;
    public Engine() {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if(File.Exists(file)) {
            try { settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(file),Protocol.Json) ?? new(); settings.Validate(); }
            catch(Exception e) { Log("Configuration could not be read: "+e.Message); settings = new() { Enabled=false }; }
        }
    }
    public Settings Config { get { lock(gate) return settings; } }
    public long Revision { get { lock(gate) return revision; } }
    public bool Running { get { lock(gate) return process is { HasExited:false }; } }
    public void Save(Settings value) {
        value.Validate();
        lock(gate) {
            value = value with { ClientId=settings.ClientId };
            File.WriteAllText(file+".tmp",JsonSerializer.Serialize(value,Protocol.Json));
            File.Move(file+".tmp",file,true);
            settings=value; revision++;
        }
    }
    public void Restart() { lock(gate) revision++; }
    public void Log(string message) {
        logs.Enqueue($"{DateTimeOffset.Now:HH:mm:ss} {message}");
        while(logs.Count>200) logs.TryDequeue(out _);
    }
    public string[] Logs => logs.ToArray();
    public void Start() {
        lock(gate) {
            if(!settings.Enabled || settings.Host=="" || process is {HasExited:false}) return;
            if(!File.Exists(Exe)) throw new FileNotFoundException("Bundled snapclient.exe is missing.",Exe);
            var info=new ProcessStartInfo(Exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(Exe)!};
            foreach(var arg in settings.Arguments()) info.ArgumentList.Add(arg);
            process=new Process{StartInfo=info};
            process.OutputDataReceived+=(_,e)=>{if(e.Data is not null) Log(e.Data);};
            process.ErrorDataReceived+=(_,e)=>{if(e.Data is not null) Log(e.Data);};
            process.Start();
            try { ProcessJob.Attach(process); }
            catch { process.Kill(true); process.Dispose(); process=null; throw; }
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            Log("Audio engine started in the service session.");
        }
    }
    public void Stop() {
        lock(gate) {
            if(process is null) return;
            try { if(!process.HasExited) { process.Kill(true); process.WaitForExit(5000); } }
            finally { process.Dispose(); process=null; }
        }
    }
    public async Task<JsonElement> Rpc(string method, object? parameters, CancellationToken token) {
        await rpcGate.WaitAsync(token);
        try {
            var config=Config;
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(5000);
            using var tcp=new TcpClient();
            await tcp.ConnectAsync(config.Host,config.ControlPort,timeout.Token);
            using var stream=tcp.GetStream();
            using var writer=new StreamWriter(stream,leaveOpen:true){AutoFlush=true};
            using var reader=new StreamReader(stream,leaveOpen:true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new {jsonrpc="2.0",id=1,method,@params=parameters},Protocol.Json).AsMemory(),timeout.Token);
            for(int i=0;i<100;i++) {
                var line=await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("Snapserver closed its control connection.");
                using var doc=JsonDocument.Parse(line);
                var obj=doc.RootElement;
                if(!obj.TryGetProperty("id",out var id) || id.ToString()!="1") continue;
                if(obj.TryGetProperty("error",out var error)) throw new IOException(error.ToString());
                return obj.GetProperty("result").Clone();
            }
            throw new IOException("No matching server reply.");
        } finally { rpcGate.Release(); }
    }
    public async Task<bool> Connected(CancellationToken token) {
        if(!Running || Config.Host=="") return false;
        var result=await Rpc("Server.GetStatus",null,token);
        foreach(var group in result.GetProperty("server").GetProperty("groups").EnumerateArray())
            foreach(var client in group.GetProperty("clients").EnumerateArray())
                if(client.GetProperty("id").GetString()==Config.ClientId && client.GetProperty("connected").GetBoolean()) return true;
        return false;
    }
    public async Task<string> Devices(CancellationToken token) {
        var info=new ProcessStartInfo(Exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.ArgumentList.Add("--player");info.ArgumentList.Add("wasapi");info.ArgumentList.Add("--list");
        using var p=Process.Start(info) ?? throw new IOException("Unable to list audio devices.");
        try {
            ProcessJob.Attach(p);
            var output=p.StandardOutput.ReadToEndAsync(token);
            var errors=p.StandardError.ReadToEndAsync(token);
            await p.WaitForExitAsync(token);
            var text=await output; var error=await errors;
            if(p.ExitCode!=0) throw new IOException(error);
            return text+error;
        } finally { if(!p.HasExited) p.Kill(true); }
    }
}


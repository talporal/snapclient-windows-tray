using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Snapcast.Core;
namespace Snapcast.Service;
public sealed class PlaybackWorker(Engine engine) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken token) {
        long last=-1; int failures=0; DateTime next=DateTime.MinValue;
        try {
            while(!token.IsCancellationRequested) {
                if(engine.Revision!=last) { engine.Stop();last=engine.Revision;failures=0;next=DateTime.MinValue; }
                if(!engine.Running && DateTime.UtcNow>=next) {
                    try { engine.Start(); }
                    catch(Exception e) { engine.Log(e.Message); }
                    next=DateTime.UtcNow.AddSeconds(Math.Min(30,Math.Pow(2,Math.Min(failures++,5))));
                }
                await Task.Delay(1000,token);
            }
        } catch(OperationCanceledException) when(token.IsCancellationRequested) {}
        finally { engine.Stop(); }
    }
}
public sealed class ControlWorker(Engine engine) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken token) {
        while(!token.IsCancellationRequested) {
            var acl=new PipeSecurity();
            acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
            foreach(var sid in new[]{WellKnownSidType.LocalServiceSid,WellKnownSidType.LocalSystemSid,WellKnownSidType.BuiltinAdministratorsSid})
                acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sid,null),PipeAccessRights.FullControl,AccessControlType.Allow));
            acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid,null),PipeAccessRights.ReadWrite,AccessControlType.Allow));
            using var pipe=NamedPipeServerStreamAcl.Create(Protocol.PipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,acl);
            try {
                await pipe.WaitForConnectionAsync(token);
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(12000);
                using var reader=new StreamReader(pipe,leaveOpen:true);
                using var writer=new StreamWriter(pipe,leaveOpen:true){AutoFlush=true};
                // Bound requests rather than allocating an unbounded line from a local client.
                var text=new System.Text.StringBuilder();var buffer=new char[1];
                while(true) {
                    if(await reader.ReadAsync(buffer.AsMemory(),timeout.Token)==0) throw new IOException("Client disconnected.");
                    if(buffer[0]=='\n') break;
                    if(text.Length>=16384) throw new IOException("Request too large.");
                    text.Append(buffer[0]);
                }
                Response response;
                try {
                    var request=JsonSerializer.Deserialize<Request>(text.ToString(),Protocol.Json) ?? throw new ArgumentException("Invalid request.");
                    response=await Handle(request,timeout.Token);
                } catch(Exception e) { response=new(false,e.Message); }
                await writer.WriteLineAsync(JsonSerializer.Serialize(response,Protocol.Json).AsMemory(),timeout.Token);
            } catch(OperationCanceledException) when(token.IsCancellationRequested) { break; }
            catch(Exception e) { engine.Log("Control: "+e.Message); }
        }
    }
    async Task<Response> Handle(Request request,CancellationToken token) {
        switch(request.Command) {
            case "status":
                bool connected=false,playing=false;string message="Waiting for configuration";
                if(engine.Config.Host!="") {
                    try { var state=await engine.Playback(token);connected=state.Connected;playing=state.Playing;message=playing?"Server stream playing":connected?"Connected to Snapserver (idle or muted)":"Audio engine disconnected or starting"; }
                    catch(Exception e) { message=(engine.Running?"Engine running; control status unavailable: ":"Audio engine stopped; control status unavailable: ")+e.Message; }
                }
                if(!engine.Config.Enabled) message="Playback disabled";
                return new(true,message,engine.Config,engine.Running,connected,engine.Logs,Playing:playing);
            case "save": engine.Save(request.Settings ?? throw new ArgumentException("Missing settings."));return new(true,"Saved. Audio engine will reconnect.");
            case "restart": engine.Restart();return new(true,"Reconnect requested.");
            case "devices": return new(true,"Audio devices visible to the service",Devices:await engine.Devices(token));
            case "volume":
                if(request.Volume is <0 or >100) throw new ArgumentException("Volume must be 0–100.");
                await engine.Rpc("Client.SetVolume",new {id=engine.Config.ClientId,volume=new {percent=request.Volume,muted=request.Muted}},token);
                return new(true,"Volume updated.");
            case "name": await engine.Rpc("Client.SetName",new{id=engine.Config.ClientId,name=engine.Config.Name},token);return new(true,"Server client name updated.");
            default: throw new ArgumentException("Unknown command.");
        }
    }
}


using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Sendspin.Windows.Core;
namespace Sendspin.Windows.Service;
public sealed class PlaybackWorker(Engine engine):BackgroundService {protected override Task ExecuteAsync(CancellationToken token)=>engine.Run(token);}
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

    Task<Response> Handle(Request request,CancellationToken token) {
        Response result=request.Command switch {
            "status"=>engine.Status(),
            "save"=>Save(request),
            "restart"=>Restart(),
            "discover"=>new(true,"Discovered Sendspin servers",Servers:engine.Servers),
            "devices"=>new(true,"Audio outputs visible to the service",Devices:WasapiPlayer.Devices()),
            "volume"=>Volume(request),
            "test-audio"=>Test(),
            _=>new(false,"Unknown command")
        };return Task.FromResult(result);
    }
    Response Save(Request r){engine.Save(r.Settings??throw new ArgumentException("Missing settings"));return new(true,"Saved. Reconnecting Sendspin.");}
    Response Restart(){engine.Restart();return new(true,"Reconnect requested.");}
    Response Volume(Request r){engine.Volume(r.Volume,r.Muted);return new(true,"Volume updated.");}
    Response Test(){engine.TestAudio();return new(true,"Speaker test requested. Listen for the two-second tone.");}
}

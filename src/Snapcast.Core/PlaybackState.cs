using System.Text.Json;
namespace Snapcast.Core;
public enum TrayState { Playing,Connected,Disconnected,Unavailable }
public record PlaybackState(bool Connected,bool Playing,string? Name);
public static class PlaybackStatus {
    public static PlaybackState Read(JsonElement result,string clientId) {
        var server=result.GetProperty("server");
        foreach(var group in server.GetProperty("groups").EnumerateArray()) {
            foreach(var client in group.GetProperty("clients").EnumerateArray()) {
                if(client.GetProperty("id").GetString()!=clientId)continue;
                var connected=client.GetProperty("connected").GetBoolean();
                var config=client.GetProperty("config");
                var muted=config.GetProperty("volume").GetProperty("muted").GetBoolean() || group.GetProperty("muted").GetBoolean();
                var streamId=group.GetProperty("stream_id").GetString();
                var playing=false;
                if(connected && !muted)
                    foreach(var stream in server.GetProperty("streams").EnumerateArray())
                        if(stream.GetProperty("id").GetString()==streamId && stream.GetProperty("status").GetString()=="playing") playing=true;
                return new(connected,playing,config.GetProperty("name").GetString());
            }
        }
        return new(false,false,null);
    }
}

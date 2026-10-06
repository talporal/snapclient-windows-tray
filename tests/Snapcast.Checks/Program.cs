using Snapcast.Core;
using System.Text.Json;
void Reject(Settings settings){try{settings.Validate();}catch(ArgumentException){return;}throw new Exception("Invalid settings accepted.");}
Reject(new(){Host="server --player something"});Reject(new(){Host="http://server"});Reject(new(){StreamPort=0});Reject(new(){ControlPort=65536});Reject(new(){SampleFormat="not-an-audio-format"});Reject(new(){LogLevel="info --help"});
var config=new Settings {Host="192.168.1.100",Soundcard="Speakers (USB Audio)",SampleFormat="48000:16:2"};
var roundtrip=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(config,Protocol.Json),Protocol.Json)!;
if(roundtrip!=config)throw new Exception("Configuration roundtrip lost data.");
var argsList=config.Arguments();
if(argsList[Array.IndexOf(argsList,"--soundcard")+1]!=config.Soundcard)throw new Exception("Device name was not retained as one argument.");
if(argsList[Array.IndexOf(argsList,"--hostID")+1]!=config.ClientId)throw new Exception("Client identity lost.");
var ipv6=new Settings{Host="::1",Transport="ws",StreamPort=1780};
if(ipv6.Arguments()[^1]!="ws://[::1]:1780")throw new Exception("IPv6 endpoint incorrectly formatted.");
Reject(new(){Transport="file"});Reject(new(){ControlTransport="ftp"});
Console.WriteLine("Configuration, identity, and argument checks passed.");


const string active="""
{"server":{"groups":[{"id":"office","clients":[{"id":"office-client","connected":true,"config":{"name":"Office","volume":{"muted":false,"percent":25}}}],"muted":false,"stream_id":"music"}],"streams":[{"id":"music","status":"playing"}]}}
""";
PlaybackState State(string json,string id="office-client") {using var document=JsonDocument.Parse(json);return PlaybackStatus.Read(document.RootElement,id);}
if(State(active)!=new PlaybackState(true,true,"Office"))throw new Exception("Active office stream not reported as playing.");
if(State(active.Replace("\"muted\":false","\"muted\":true")).Playing)throw new Exception("Muted client/group reported as playing.");
if(State(active.Replace("\"status\":\"playing\"","\"status\":\"idle\"")).Playing)throw new Exception("Idle stream reported as playing.");
if(State(active.Replace("\"connected\":true","\"connected\":false")).Playing)throw new Exception("Disconnected client reported as playing.");
if(State(active,"another-client").Connected)throw new Exception("Unrelated client used for local playback status.");
Console.WriteLine("Server playback state and muted/disconnected checks passed.");

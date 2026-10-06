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


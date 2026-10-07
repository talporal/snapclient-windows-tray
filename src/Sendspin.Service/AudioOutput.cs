using NAudio.CoreAudioApi;
using NAudio.Wave;
using Sendspin.Windows.Core;
using Sendspin.SDK.Audio;
using Sendspin.SDK.Models;
namespace Sendspin.Windows.Service;
// WASAPI calls are serialized on a dedicated MTA thread: setup, device switch and
// disposal never run on the audio callback or a UI thread.
public sealed class WasapiPlayer(string deviceId,int latencyMs,Action<string> log,Action<int> frames) : IAudioPlayer {
 readonly AudioThread thread=new();
 MMDeviceEnumerator? enumerator;MMDevice? device;WasapiOut? output;IAudioSampleSource? source;
 AudioFormat? format;float volume=1;bool muted;bool disposed;
 public AudioPlayerState State {get;private set;}=AudioPlayerState.Uninitialized;
 public float Volume {get=>Volatile.Read(ref volume);set=>Volatile.Write(ref volume,Math.Clamp(value,0,1));}
 public bool IsMuted {get=>Volatile.Read(ref muted);set=>Volatile.Write(ref muted,value);}
 public int OutputLatencyMs=>latencyMs;
 public AudioFormat? OutputFormat=>format;
 public event EventHandler<AudioPlayerState>? StateChanged;
 public event EventHandler<AudioPlayerError>? ErrorOccurred;
 void Change(AudioPlayerState value){State=value;StateChanged?.Invoke(this,value);}
 public Task InitializeAsync(AudioFormat value,CancellationToken cancellationToken=default) {
  cancellationToken.ThrowIfCancellationRequested();thread.Run(()=>{CloseOutput();format=value;enumerator=new();device=deviceId==""?enumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia):enumerator.GetDevice(deviceId);output=new WasapiOut(device,AudioClientShareMode.Shared,true,latencyMs);output.PlaybackStopped+=(_,e)=>{if(e.Exception is not null){Change(AudioPlayerState.Error);ErrorOccurred?.Invoke(this,new(e.Exception.Message,e.Exception));log("WASAPI: "+e.Exception.Message);}};Change(AudioPlayerState.Stopped);});return Task.CompletedTask;
 }
 public void SetSampleSource(IAudioSampleSource value){thread.Run(()=>{source=value;output!.Init(new NAudio.Wave.SampleProviders.SampleToWaveProvider(new PullProvider(value,()=>IsMuted?0:Volume,frames)));});}
 public void Play()=>thread.Run(()=>{output!.Play();Change(AudioPlayerState.Playing);});
 public void Pause()=>thread.Run(()=>{output?.Pause();Change(AudioPlayerState.Paused);});
 public void Stop()=>thread.Run(()=>{output?.Stop();Change(AudioPlayerState.Stopped);});
 public async Task SwitchDeviceAsync(string? id,CancellationToken cancellationToken=default) {
  deviceId=id??"";var previous=State;var saved=source;if(format is null)return;
  await InitializeAsync(format,cancellationToken);if(saved is not null)SetSampleSource(saved);if(previous==AudioPlayerState.Playing)Play();
 }
 void CloseOutput(){output?.Stop();output?.Dispose();output=null;device?.Dispose();device=null;enumerator?.Dispose();enumerator=null;}
 public ValueTask DisposeAsync(){if(!disposed){disposed=true;thread.Run(CloseOutput);thread.Dispose();}return ValueTask.CompletedTask;}
 public static DeviceEntry[] Devices() {
  using var e=new MMDeviceEnumerator();string defaultName;
  try{using var current=e.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);defaultName="Service default · "+current.FriendlyName;}
  catch(System.Runtime.InteropServices.COMException){defaultName="Service default · no default output available";}
  var result=new List<DeviceEntry>{new("",defaultName)};
  foreach(var d in e.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active)){using(d)result.Add(new(d.ID,d.FriendlyName));}return result.ToArray();
 }
}
public sealed class PullProvider(IAudioSampleSource source,Func<float> gain,Action<int> frames):ISampleProvider {
 public WaveFormat WaveFormat {get;}=WaveFormat.CreateIeeeFloatWaveFormat(source.Format.SampleRate,source.Format.Channels);
 public int Read(float[] buffer,int offset,int count) {
  int read=source.Read(buffer,offset,count);if(read<0||read>count)throw new IOException("Invalid audio sample count.");
  var g=gain();for(int i=offset;i<offset+read;i++)buffer[i]*=g;
  Array.Clear(buffer,offset+read,count-read);if(read>0)frames(read/source.Format.Channels);
  // Always emit a full callback: temporary network silence must not terminate WASAPI.
  return count;
 }
}
public sealed class TimedSource(ITimedAudioBuffer buffer,Func<long> clock):IAudioSampleSource {
 public AudioFormat Format=>buffer.Format;
 public int Read(float[] samples,int offset,int count)=>buffer.Read(samples.AsSpan(offset,count),clock());
}
public sealed class ToneSource:IAudioSampleSource {
 long sample;
 public AudioFormat Format {get;}=new(){Codec="pcm",SampleRate=48000,Channels=2,BitDepth=16};
 public int Read(float[] buffer,int offset,int count){for(int i=0;i<count;i+=2){float value=(float)(Math.Sin(2*Math.PI*440*sample++/48000)*0.15);buffer[offset+i]=value;if(i+1<count)buffer[offset+i+1]=value;}return count;}
}
internal sealed class AudioThread:IDisposable {
 readonly System.Collections.Concurrent.BlockingCollection<Action> work=new();readonly Thread thread;
 public AudioThread(){thread=new Thread(()=>{foreach(var action in work.GetConsumingEnumerable())action();}){IsBackground=true,Name="Sendspin WASAPI"};thread.SetApartmentState(ApartmentState.MTA);thread.Start();}
 public void Run(Action action){if(Thread.CurrentThread==thread){action();return;}var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);work.Add(()=>{try{action();done.SetResult();}catch(Exception e){done.SetException(e);}});done.Task.GetAwaiter().GetResult();}
 public void Dispose(){work.CompleteAdding();thread.Join(TimeSpan.FromSeconds(10));work.Dispose();}
}

using Snapcast.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
if (!OperatingSystem.IsWindowsVersionAtLeast(10,0,22000)) throw new PlatformNotSupportedException("Requires Windows 11 or Windows Server 2025 Desktop Experience.");
if(args.Length>0 && args[0]=="--control-service") {
    if(args.Length!=2 || args[1] is not ("restart" or "stop")) {Environment.ExitCode=2;return;}
    try {
        using var service=new System.ServiceProcess.ServiceController("SnapcastWindows");
        service.Refresh();
        if(service.Status==System.ServiceProcess.ServiceControllerStatus.StopPending) service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(30));
        service.Refresh();
        if(service.Status!=System.ServiceProcess.ServiceControllerStatus.Stopped) {service.Stop();service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(30));}
        if(args[1]=="restart") {service.Start();service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running,TimeSpan.FromSeconds(30));}
        Environment.ExitCode=0;
    } catch(Exception error) {Console.Error.WriteLine(error);Environment.ExitCode=1;}
    return;
}
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "SnapcastWindows");
builder.Services.AddSingleton<Engine>();
builder.Services.AddHostedService<PlaybackWorker>();
builder.Services.AddHostedService<ControlWorker>();
await builder.Build().RunAsync();


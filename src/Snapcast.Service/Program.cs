using Snapcast.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
if (!OperatingSystem.IsWindowsVersionAtLeast(10,0,22000)) throw new PlatformNotSupportedException("Requires Windows 11 or Windows Server 2025 Desktop Experience.");
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "SnapcastWindows");
builder.Services.AddSingleton<Engine>();
builder.Services.AddHostedService<PlaybackWorker>();
builder.Services.AddHostedService<ControlWorker>();
await builder.Build().RunAsync();


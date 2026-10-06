using System.ComponentModel;
using System.Diagnostics;
namespace Snapcast.Tray;
internal static class ServiceControl {
    public static async Task Run(string action) {
        if(action is not ("restart" or "stop")) throw new ArgumentException("Unknown service action.");
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,".."));
        var helper=Path.Combine(root,"Service","Snapcast.Service.exe");
        if(!File.Exists(helper)) throw new FileNotFoundException("The installed service controller could not be found. Run the installer to repair the app.",helper);
        var info=new ProcessStartInfo(helper){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(helper)!,Arguments="--control-service "+action};
        // The service helper accepts only these two fixed actions and does not change startup configuration.
        using var process=await Task.Run(()=>Process.Start(info)) ?? throw new IOException("Windows could not start service control.");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(75));
        await process.WaitForExitAsync(timeout.Token);
        if(process.ExitCode!=0) throw new IOException($"Windows service {action} failed (code {process.ExitCode}). Check the SnapcastWindows service in Windows Services.");
    }
}


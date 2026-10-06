using System.Diagnostics;
using System.Runtime.InteropServices;
namespace Snapcast.Tray;
internal static class StartupDiagnostics {
    static readonly object gate = new();
    public static string LogPath {
        get {
            var args=Environment.GetCommandLineArgs();
            var index=Array.IndexOf(args,"--smoke-log");
            if(args.Contains("--smoke-test") && index>=0 && index+1<args.Length) return Path.GetFullPath(args[index+1]);
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SnapcastWindows","tray-startup.log");
        }
    }
    public static void Write(string message, Exception? exception=null) {
        try {
            lock(gate) {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if(File.Exists(LogPath) && new FileInfo(LogPath).Length>1048576) File.Move(LogPath,LogPath+".previous",true);
                File.AppendAllText(LogPath,$"{DateTimeOffset.Now:O} PID {Environment.ProcessId} Session {Process.GetCurrentProcess().SessionId}: {message}\n{exception}\n");
            }
        } catch { /* Logging must not interrupt startup. */ }
    }
    public static void ShowError(Exception error) {
        try { MessageBox(IntPtr.Zero,$"Snapcast controls could not start.\n\n{error.Message}\n\nStartup log: {LogPath}","Snapcast startup error",0x10); } catch {}
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="MessageBoxW")]static extern int MessageBox(IntPtr hwnd,string text,string caption,uint type);
}


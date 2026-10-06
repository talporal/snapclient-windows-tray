using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Snapcast.Core;
namespace Snapcast.Tray;
internal sealed class TrayIcon : IDisposable {
    const uint Callback=0x8001;
    readonly WindowProc proc;
    readonly IntPtr hwnd;
    readonly string className="SnapcastTray."+Guid.NewGuid().ToString("N");
    readonly IntPtr module;
    readonly DispatcherQueue queue;
    readonly Action show,restart,exit;
    readonly uint taskbarCreated;
    readonly Dictionary<TrayState,IntPtr> icons=new();
    readonly Microsoft.UI.Xaml.DispatcherTimer retry=new(){Interval=TimeSpan.FromSeconds(10)};
    NotifyData data;
    bool disposed;
    public bool Registered {get;private set;}
    public bool AssetsLoaded=>icons.Count==4 && icons.Values.All(icon=>icon!=IntPtr.Zero);
    public bool NativeWindowReady=>hwnd!=IntPtr.Zero;
    public TrayIcon(DispatcherQueue queue,Action show,Action restart,Action exit) {
        this.queue=queue;this.show=show;this.restart=restart;this.exit=exit;
        proc=WndProc;module=GetModuleHandle(null);
        try {
            foreach(var state in Enum.GetValues<TrayState>()) {
                var path=Path.Combine(AppContext.BaseDirectory,"Assets","Tray"+state+".ico");
                var icon=LoadImage(IntPtr.Zero,path,1,GetSystemMetrics(49),GetSystemMetrics(50),0x10);
                if(icon==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Unable to load tray icon: "+path);
                icons.Add(state,icon);
            }
            var cls=new WindowClass {Size=(uint)Marshal.SizeOf<WindowClass>(),Instance=module,ClassName=className,Proc=Marshal.GetFunctionPointerForDelegate(proc)};
            if(RegisterClassEx(ref cls)==0)throw new System.ComponentModel.Win32Exception();
            hwnd=CreateWindowEx(0,className,"Snapcast control",0,0,0,0,0,IntPtr.Zero,IntPtr.Zero,module,IntPtr.Zero);
            if(hwnd==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
            taskbarCreated=RegisterWindowMessage("TaskbarCreated");
            data=new NotifyData {Size=(uint)Marshal.SizeOf<NotifyData>(),Hwnd=hwnd,Id=1,Flags=7,CallbackMessage=Callback,Icon=icons[TrayState.Disconnected],Tip="Snapcast: checking service",Info="",InfoTitle=""};
            retry.Tick+=(_,_)=>{if(!Registered)Register();};
            Register();retry.Start();
        } catch {Dispose();throw;}
    }
    void Register() {
        if(disposed)return;
        Registered=Shell_NotifyIcon(0,ref data);
        StartupDiagnostics.Write(Registered?"Tray icon registered with Windows shell.":"Windows shell is not ready for tray registration; retry scheduled.");
    }
    public void Update(TrayState state,string tooltip) {
        if(disposed)return;
        var tip=tooltip.Length>127?tooltip[..127]:tooltip;
        if(data.Icon==icons[state] && data.Tip==tip)return;
        data.Icon=icons[state];data.Tip=tip;
        if(Registered && !Shell_NotifyIcon(1,ref data)) {Registered=false;Register();}
    }
    IntPtr WndProc(IntPtr window,uint message,IntPtr w,IntPtr l) {
        try {
            if(taskbarCreated!=0 && message==taskbarCreated){Registered=false;Register();return IntPtr.Zero;}
            if(message==Callback) {
                if((uint)l.ToInt64()==0x202)queue.TryEnqueue(()=>show());
                if((uint)l.ToInt64()==0x205) {
                    var menu=CreatePopupMenu();
                    try {
                        AppendMenu(menu,0,1,"Open GUI");AppendMenu(menu,0,2,"Restart service");AppendMenu(menu,0x800,0,"");AppendMenu(menu,0,3,"Exit app…");
                        GetCursorPos(out var pt);SetForegroundWindow(hwnd);
                        var selected=TrackPopupMenu(menu,0x100|0x2,pt.X,pt.Y,0,hwnd,IntPtr.Zero);
                        PostMessage(hwnd,0,IntPtr.Zero,IntPtr.Zero);
                        if(selected==1)queue.TryEnqueue(()=>show());if(selected==2)queue.TryEnqueue(()=>restart());if(selected==3)queue.TryEnqueue(()=>exit());
                    } finally {if(menu!=IntPtr.Zero)DestroyMenu(menu);}
                }
                return IntPtr.Zero;
            }
        } catch(Exception error) {StartupDiagnostics.Write("Tray callback failed.",error);}
        return DefWindowProc(window,message,w,l);
    }
    public void Dispose(){
        if(disposed)return;disposed=true;retry.Stop();
        if(Registered){Shell_NotifyIcon(2,ref data);Registered=false;}
        if(hwnd!=IntPtr.Zero)DestroyWindow(hwnd);
        if(module!=IntPtr.Zero)UnregisterClass(className,module);
        foreach(var icon in icons.Values)DestroyIcon(icon);icons.Clear();GC.KeepAlive(proc);
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr WindowProc(IntPtr h,uint m,IntPtr w,IntPtr l);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct WindowClass {public uint Size,Style;public IntPtr Proc;public int ClassExtra,WindowExtra;public IntPtr Instance,Icon,Cursor,Background;[MarshalAs(UnmanagedType.LPWStr)]public string? MenuName;[MarshalAs(UnmanagedType.LPWStr)]public string ClassName;public IntPtr SmallIcon;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct NotifyData {public uint Size;public IntPtr Hwnd;public uint Id,Flags,CallbackMessage;public IntPtr Icon;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;public uint State,StateMask;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;public uint Version;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string InfoTitle;public uint InfoFlags;public Guid Guid;public IntPtr BalloonIcon;}
    [StructLayout(LayoutKind.Sequential)] struct Point {public int X,Y;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern ushort RegisterClassEx(ref WindowClass cls);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateWindowEx(uint ex,string cls,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool UnregisterClass(string name,IntPtr instance);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr DefWindowProc(IntPtr h,uint m,IntPtr w,IntPtr l);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern bool Shell_NotifyIcon(uint message,ref NotifyData data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadImage(IntPtr instance,string name,uint type,int cx,int cy,uint flags);
    [DllImport("user32.dll")]static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")]static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool AppendMenu(IntPtr menu,uint flags,uint id,string text);
    [DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]static extern uint TrackPopupMenu(IntPtr menu,uint flags,int x,int y,int reserved,IntPtr hwnd,IntPtr rect);
    [DllImport("user32.dll")]static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")]static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")]static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr w,IntPtr l);
    [DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr hwnd,int command);
}

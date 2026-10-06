using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
namespace Snapcast.Tray;
internal sealed class TrayIcon : IDisposable {
    const uint Callback=0x8001;
    readonly WindowProc proc;
    readonly IntPtr hwnd;
    readonly DispatcherQueue queue;
    readonly Action show,exit,hide;
    readonly uint taskbarCreated;
    NotifyData data;
    bool disposed;
    public TrayIcon(DispatcherQueue queue,Action show,Action exit,Action hide) {
        this.queue=queue;this.show=show;this.exit=exit;this.hide=hide;
        proc=WndProc;
        var cls=new WindowClass {Size=(uint)Marshal.SizeOf<WindowClass>(),Instance=GetModuleHandle(null),ClassName="SnapcastTray."+Guid.NewGuid().ToString("N"),Proc=Marshal.GetFunctionPointerForDelegate(proc)};
        if(RegisterClassEx(ref cls)==0)throw new System.ComponentModel.Win32Exception();
        hwnd=CreateWindowEx(0,cls.ClassName,"Snapcast control",0,0,0,0,0,IntPtr.Zero,IntPtr.Zero,cls.Instance,IntPtr.Zero);
        if(hwnd==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
        taskbarCreated=RegisterWindowMessage("TaskbarCreated");
        data=new NotifyData {Size=(uint)Marshal.SizeOf<NotifyData>(),Hwnd=hwnd,Id=1,Flags=7,CallbackMessage=Callback,Icon=LoadIcon(IntPtr.Zero,(IntPtr)32512),Tip="Snapcast • Open controls",Info="",InfoTitle=""};
        Shell_NotifyIcon(0,ref data);
    }
    IntPtr WndProc(IntPtr window,uint message,IntPtr w,IntPtr l) {
        if(message==taskbarCreated){Shell_NotifyIcon(0,ref data);return IntPtr.Zero;}
        if(message==Callback) {
            if((uint)l.ToInt64()==0x202)queue.TryEnqueue(()=>show());
            if((uint)l.ToInt64()==0x205) {
                var menu=CreatePopupMenu();
                AppendMenu(menu,0,1,"Open controls");AppendMenu(menu,0,2,"Hide controls");AppendMenu(menu,0x800,0,"");AppendMenu(menu,0,3,"Exit tray (keep playback running)");
                GetCursorPos(out var pt);SetForegroundWindow(hwnd);
                var selected=TrackPopupMenu(menu,0x100|0x2,pt.X,pt.Y,0,hwnd,IntPtr.Zero);
                DestroyMenu(menu);PostMessage(hwnd,0,IntPtr.Zero,IntPtr.Zero);
                if(selected==1)queue.TryEnqueue(()=>show());if(selected==2)queue.TryEnqueue(()=>hide());if(selected==3)queue.TryEnqueue(()=>exit());
            }
            return IntPtr.Zero;
        }
        return DefWindowProc(window,message,w,l);
    }
    public void Dispose(){if(disposed)return;disposed=true;Shell_NotifyIcon(2,ref data);DestroyWindow(hwnd);GC.KeepAlive(proc);}
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr WindowProc(IntPtr h,uint m,IntPtr w,IntPtr l);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct WindowClass {public uint Size,Style;public IntPtr Proc;public int ClassExtra,WindowExtra;public IntPtr Instance,Icon,Cursor,Background;[MarshalAs(UnmanagedType.LPWStr)]public string? MenuName;[MarshalAs(UnmanagedType.LPWStr)]public string ClassName;public IntPtr SmallIcon;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct NotifyData {public uint Size;public IntPtr Hwnd;public uint Id,Flags,CallbackMessage;public IntPtr Icon;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;public uint State,StateMask;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;public uint Version;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string InfoTitle;public uint InfoFlags;public Guid Guid;public IntPtr BalloonIcon;}
    [StructLayout(LayoutKind.Sequential)] struct Point {public int X,Y;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern ushort RegisterClassEx(ref WindowClass cls);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateWindowEx(uint ex,string cls,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr DefWindowProc(IntPtr h,uint m,IntPtr w,IntPtr l);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern bool Shell_NotifyIcon(uint message,ref NotifyData data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")]static extern IntPtr LoadIcon(IntPtr instance,IntPtr name);
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


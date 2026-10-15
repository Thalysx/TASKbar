using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskBAR {
    internal static class Native {
        sealed class ProcessEntry { public string Name; public DateTime Expires; }
        sealed class BackendEntry { public string Kind; public uint Pid; public DateTime Expires; }
        static readonly object cacheLock = new object();
        static readonly Dictionary<uint,ProcessEntry> processCache = new Dictionary<uint,ProcessEntry>();
        static readonly Dictionary<IntPtr,BackendEntry> backendCache = new Dictionary<IntPtr,BackendEntry>();
        static DateTime conflictExpires = DateTime.MinValue;
        static bool conflictValue;
        internal static int ProcessQueries;
        internal delegate bool EnumProc(IntPtr hwnd, IntPtr parameter);
        internal delegate void EventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct Power { public byte AC, Flag, Percent, Saver; public uint Lifetime, FullLifetime; }
        [StructLayout(LayoutKind.Sequential)] internal struct AccentPolicy { public int State, Flags; public uint Color; public int Animation; }
        [StructLayout(LayoutKind.Sequential)] internal struct CompositionData { public int Attribute; public IntPtr Data; public int Size; }
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc proc, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW", SetLastError=true)] internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll", SetLastError=true)] internal static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
        [DllImport("user32.dll", SetLastError=true)] internal static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionData data);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wp, IntPtr lp, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, EventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("kernel32.dll")] internal static extern bool GetSystemPowerStatus(out Power status);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
        [DllImport("ExplorerTAP.dll", CallingConvention=CallingConvention.Cdecl, ExactSpelling=true)] internal static extern int InjectExplorerTAP(IntPtr hwnd, ref Guid iid, out IntPtr service);
        internal static string ClassName(IntPtr hwnd) { var b = new StringBuilder(256); GetClassName(hwnd, b, b.Capacity); return b.ToString(); }
        internal static string Title(IntPtr hwnd) { var b = new StringBuilder(1024); GetWindowText(hwnd, b, b.Capacity); return b.ToString(); }
        internal static string ProcessName(IntPtr hwnd) {
            uint pid; GetWindowThreadProcessId(hwnd,out pid); if (pid == 0) return "";
            lock(cacheLock) {
                ProcessEntry entry; DateTime now = DateTime.UtcNow;
                if (processCache.TryGetValue(pid,out entry) && now < entry.Expires) return entry.Name;
                string name; try { using(var p = Process.GetProcessById((int)pid)) { ProcessQueries++; name = p.ProcessName + ".exe"; } } catch { return ""; }
                if (processCache.Count >= 256) processCache.Clear();
                processCache[pid] = new ProcessEntry { Name=name,Expires=now.AddSeconds(2) }; return name;
            }
        }
        internal static bool Conflict(bool force = false) {
            lock(cacheLock) {
                DateTime now = DateTime.UtcNow; if (!force && now < conflictExpires) return conflictValue;
                var processes = Process.GetProcessesByName("TranslucentTB"); conflictValue = processes.Length > 0;
                foreach(var process in processes) process.Dispose(); conflictExpires = now.AddSeconds(1); return conflictValue;
            }
        }
        internal static List<IntPtr> Taskbars() {
            var bars = new List<IntPtr>();
            EnumWindows(delegate(IntPtr h, IntPtr unused) { string c = ClassName(h); if (c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") bars.Add(h); return true; }, IntPtr.Zero);
            return bars;
        }
        internal static string TaskbarKind(IntPtr taskbar) {
            uint pid; GetWindowThreadProcessId(taskbar,out pid);
            lock(cacheLock) { BackendEntry cached; if (backendCache.TryGetValue(taskbar,out cached) && cached.Pid == pid && DateTime.UtcNow < cached.Expires) return cached.Kind; }
            string kind = DetectTaskbarKind(taskbar,pid);
            lock(cacheLock) { if (backendCache.Count > 32) backendCache.Clear(); backendCache[taskbar] = new BackendEntry { Kind=kind,Pid=pid,Expires=DateTime.UtcNow.AddSeconds(kind == "unknown" ? 2 : 30) }; }
            return kind;
        }
        static string DetectTaskbarKind(IntPtr taskbar, uint pid) {
            int islands = 0;
            EnumChildWindows(taskbar,delegate(IntPtr h,IntPtr unused) { if (ClassName(h) == "Windows.UI.Composition.DesktopWindowContentBridge" && Title(h) == "DesktopWindowXamlSource") islands++; return true; },IntPtr.Zero);
            if (islands == 0) return "legacy";
            try {
                using(var process = Process.GetProcessById((int)pid)) {
                    bool taskbarDll = false; foreach (ProcessModule module in process.Modules) if (string.Equals(module.ModuleName,"Taskbar.dll",StringComparison.OrdinalIgnoreCase)) { taskbarDll = true; break; }
                    if (!taskbarDll) return "legacy";
                }
            } catch { return "unknown"; }
            return islands == 1 ? "xaml" : islands == 2 ? "legacy" : "unknown";
        }
        internal static object DescribeTaskbar(IntPtr taskbar) {
            var children = new List<string>();
            EnumChildWindows(taskbar,delegate(IntPtr h,IntPtr unused) { children.Add(ClassName(h) + " | " + Title(h)); return true; },IntPtr.Zero);
            uint pid; GetWindowThreadProcessId(taskbar,out pid); var modules = new List<string>();
            try { using(var p = Process.GetProcessById((int)pid)) foreach(ProcessModule m in p.Modules) if (m.ModuleName.IndexOf("taskbar",StringComparison.OrdinalIgnoreCase) >= 0) modules.Add(m.ModuleName); } catch(Exception ex) { modules.Add(ex.Message); }
            return new Dictionary<string,object> { { "class",ClassName(taskbar) }, { "children",children.ToArray() }, { "taskbar_modules",modules.ToArray() } };
        }
    }
    [ComImport, Guid("5bcf9150-c28a-4ef2-913c-4c3ea2f5ead0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITaskbarAppearanceService {
        [PreserveSig] int SetTaskbarAppearance(IntPtr hwnd, int brush, uint color);
        [PreserveSig] int SetTaskbarBlur(IntPtr hwnd, uint color, float amount);
        [PreserveSig] int ReturnTaskbarToDefaultAppearance(IntPtr hwnd);
        [PreserveSig] int SetTaskbarBorderVisibility(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool visible);
        [PreserveSig] int RestoreAllTaskbarsToDefault();
        [PreserveSig] int RestoreAllTaskbarsToDefaultWhenProcessDies(uint pid);
        [PreserveSig] int KillExplorerWhenPackageUninstalls([MarshalAs(UnmanagedType.LPWStr)] string package);
    }
    [ComImport, Guid("2246EA2D-CAEA-4444-A3C4-6DE827E44313"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAppVisibility {
        [PreserveSig] int GetAppVisibilityOnMonitor(IntPtr monitor, out int mode);
        [PreserveSig] int IsLauncherVisible([MarshalAs(UnmanagedType.Bool)] out bool visible);
        [PreserveSig] int Advise(IntPtr sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
    }
    internal sealed class WindowObserver : IDisposable {
        readonly List<IntPtr> hooks = new List<IntPtr>();
        readonly Native.EventProc callback;
        readonly Action changed;
        IAppVisibility visibility;
        bool peek;
        public WindowObserver(Action changed) {
            this.changed = changed;
            callback = delegate(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint tid, uint time) {
                if (evt >= 0x8000 && (hwnd == IntPtr.Zero || Native.GetAncestor(hwnd,2) != hwnd)) return;
                if (evt == 0x24) peek = true; else if (evt == 0x25) peek = false;
                if (obj == 0 || evt < 0x8000) this.changed();
            };
            foreach (uint[] range in new [] { new uint[]{3,3}, new uint[]{0x16,0x17}, new uint[]{0x24,0x25}, new uint[]{0x8000,0x800C}, new uint[]{0x8017,0x8018} }) {
                IntPtr h = Native.SetWinEventHook(range[0],range[1],IntPtr.Zero,callback,0,0,2);
                if (h != IntPtr.Zero) hooks.Add(h);
            }
            try { visibility = (IAppVisibility)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("7E5FE3D9-985F-4908-91F9-EE19F9FD1514"))); } catch { }
        }
        public DesktopSnapshot Capture() {
            var d = new DesktopSnapshot(); d.Peek = peek;
            Native.Power power; d.BatterySaver = Native.GetSystemPowerStatus(out power) && power.Saver == 1;
            bool start; try { if (visibility != null && visibility.IsLauncherVisible(out start) == 0) d.Start = start; } catch(COMException) { Marshal.ReleaseComObject(visibility); visibility = null; }
            IntPtr fg = Native.GetForegroundWindow(); d.ShellMonitor = Native.MonitorFromWindow(fg,2);
            string fgProcess = Native.ProcessName(fg), fgClass = Native.ClassName(fg);
            d.Start |= fgProcess.Equals("StartMenuExperienceHost.exe",StringComparison.OrdinalIgnoreCase);
            d.Search = fgProcess.Equals("SearchHost.exe",StringComparison.OrdinalIgnoreCase) || fgProcess.Equals("SearchApp.exe",StringComparison.OrdinalIgnoreCase) || fgProcess.Equals("SearchUI.exe",StringComparison.OrdinalIgnoreCase);
            d.TaskView = fgClass == "MultitaskingViewFrame";
            // New Windows builds expose Task View under a XAML host; keep this detection explicit, not a guessed foreground match.
            int ownPid; using(var ownProcess = Process.GetCurrentProcess()) ownPid = ownProcess.Id;
            Native.EnumWindows(delegate(IntPtr h, IntPtr unused) {
                string c = Native.ClassName(h);
                if (c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") { d.Taskbars.Add(h); return true; }
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
                if (c == "MultitaskingViewFrame") { d.TaskView = true; return true; }
                if (c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd" || c == "Progman" || c == "WorkerW" || c == "Windows.UI.Core.CoreWindow") return true;
                uint pid; Native.GetWindowThreadProcessId(h,out pid); if (pid == ownPid) return true;
                long style = Native.GetWindowLongPtr(h,-20).ToInt64();
                if ((style & 0x80) != 0 || (Native.GetWindow(h,4) != IntPtr.Zero && (style & 0x40000) == 0)) return true;
                int cloaked; if (Native.DwmGetWindowAttribute(h,14,out cloaked,4) == 0 && cloaked != 0) return true;
                Native.Rect rect; if (!Native.GetWindowRect(h,out rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top) return true;
                string process = Native.ProcessName(h);
                if (process == "StartMenuExperienceHost.exe" || process == "SearchHost.exe" || process == "SearchApp.exe") return true;
                d.Windows.Add(new WindowInfo { Handle = h, Monitor = Native.MonitorFromWindow(h,2), Class = c, Title = Native.Title(h), Process = process, Maximized = Native.IsZoomed(h), Foreground = h == fg });
                return true;
            },IntPtr.Zero);
            return d;
        }
        public void Dispose() { foreach (IntPtr h in hooks) Native.UnhookWinEvent(h); if (visibility != null) Marshal.ReleaseComObject(visibility); }
    }
}

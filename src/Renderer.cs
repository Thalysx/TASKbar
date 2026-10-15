using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace TaskBAR {
    internal sealed class Renderer : IDisposable {
        readonly Thread thread;
        readonly AutoResetEvent signal = new AutoResetEvent(false);
        readonly object gate = new object();
        readonly Action<string, bool> report;
        List<Decision> pending;
        readonly RenderEpoch epoch = new RenderEpoch();
        int pendingEpoch;
        bool quitting, restore, retry;
        ITaskbarAppearanceService service;
        uint explorerPid;
        readonly Dictionary<IntPtr,string> applied = new Dictionary<IntPtr,string>();
        readonly Dictionary<IntPtr,DateTime> appliedAt = new Dictionary<IntPtr,DateTime>();
        readonly Dictionary<IntPtr,long> peekStyles = new Dictionary<IntPtr,long>();
        DateTime reconnectAfter = DateTime.MinValue;
        bool owned;
        public Renderer(Action<string,bool> report) { this.report = report; thread = new Thread(Loop) { IsBackground = true, Name = "TaskBAR renderer" }; thread.SetApartmentState(ApartmentState.STA); thread.Start(); }
        public void Submit(List<Decision> decisions, bool force) { lock(gate) { if (quitting) return; pending = decisions; pendingEpoch = epoch.Current; retry |= force; } signal.Set(); }
        public void Restore() { lock(gate) { epoch.Cancel(); pending = null; restore = true; } signal.Set(); }
        static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
        void Connect(IntPtr taskbar) {
            uint pid; Native.GetWindowThreadProcessId(taskbar,out pid);
            if (service != null && pid == explorerPid) return;
            if (explorerPid != 0 && pid != explorerPid) {
                if (service != null) { Marshal.ReleaseComObject(service); service = null; }
                applied.Clear(); appliedAt.Clear(); peekStyles.Clear(); owned = false; reconnectAfter = DateTime.MinValue;
            }
            if (service != null) { Marshal.ReleaseComObject(service); service = null; applied.Clear(); }
            string kind = Native.TaskbarKind(taskbar);
            if (kind == "unknown") throw new InvalidOperationException("Esta estrutura de barra de tarefas ainda não é reconhecida pelo TaskBAR.");
            if (kind == "legacy") { explorerPid = pid; return; }
            if (DateTime.UtcNow < reconnectAfter) throw new InvalidOperationException("Conexão com o Explorer indisponível. Clique em Ativar para tentar novamente.");
            reconnectAfter = DateTime.UtcNow.AddSeconds(30);
            Guid iid = typeof(ITaskbarAppearanceService).GUID; IntPtr pointer;
            Check(Native.InjectExplorerTAP(taskbar,ref iid,out pointer));
            try { service = (ITaskbarAppearanceService)Marshal.GetObjectForIUnknown(pointer); } finally { Marshal.Release(pointer); }
            Check(service.RestoreAllTaskbarsToDefaultWhenProcessDies((uint)Process.GetCurrentProcess().Id));
            explorerPid = pid; owned = true; applied.Clear();
        }
        void Apply(Decision d, int ticket) {
            if (!epoch.Accepts(ticket)) return;
            if (!Native.IsWindow(d.Taskbar)) { applied.Remove(d.Taskbar); return; }
            Connect(d.Taskbar);
            if (!epoch.Accepts(ticket)) return;
            string fingerprint = d.Appearance.Fingerprint; string previous;
            DateTime stamp;
            if (applied.TryGetValue(d.Taskbar,out previous) && previous == fingerprint && (service != null || (appliedAt.TryGetValue(d.Taskbar,out stamp) && DateTime.UtcNow - stamp < TimeSpan.FromSeconds(2)))) return;
            var p = d.Appearance; uint color = p.Abgr;
            owned = true; applied[d.Taskbar] = "";
            if (service != null) {
                if (p.Accent == "normal") Check(service.ReturnTaskbarToDefaultAppearance(d.Taskbar));
                else if (p.Accent == "blur") Check(service.SetTaskbarBlur(d.Taskbar,color,p.BlurRadius / 3f));
                else Check(service.SetTaskbarAppearance(d.Taskbar,p.Accent == "acrylic" ? 0 : 1,p.Accent == "opaque" ? color | 0xFF000000u : color));
                Check(service.SetTaskbarBorderVisibility(d.Taskbar,p.ShowLine));
            } else {
                SetLegacy(d.Taskbar,p);
                IntPtr notify = Native.FindWindowEx(d.Taskbar,IntPtr.Zero,"TrayNotifyWnd",null);
                IntPtr peek = notify == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(notify,IntPtr.Zero,"TrayShowDesktopButtonWClass",null);
                if (peek != IntPtr.Zero) {
                    long style; if (!peekStyles.TryGetValue(peek,out style)) { style = Native.GetWindowLongPtr(peek,-20).ToInt64(); peekStyles[peek] = style; }
                    Native.SetWindowLongPtr(peek,-20,new IntPtr(p.ShowPeek ? style : style | 0x80000));
                    if (!p.ShowPeek) Native.SetLayeredWindowAttributes(peek,0,1,2);
                }
            }
            owned = true; applied[d.Taskbar] = fingerprint; appliedAt[d.Taskbar] = DateTime.UtcNow;
        }
        internal static void SetLegacy(IntPtr hwnd, Appearance p) {
            if (p.Accent == "normal") { IntPtr result; Native.SendMessageTimeout(hwnd,0x31E,new IntPtr(1),IntPtr.Zero,2,500,out result); return; }
            uint color = p.Abgr;
            if (p.Accent == "opaque") color |= 0xFF000000;
            if (p.Accent == "acrylic" && (color >> 24) == 0) color |= 0x01000000;
            var policy = new Native.AccentPolicy { State = p.AccentValue, Flags = p.Accent == "acrylic" ? 0 : 2, Color = color };
            IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.AccentPolicy)));
            try { Marshal.StructureToPtr(policy,memory,false); var data = new Native.CompositionData { Attribute = 0x13, Data = memory, Size = Marshal.SizeOf(typeof(Native.AccentPolicy)) }; if (!Native.SetWindowCompositionAttribute(hwnd,ref data)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
            finally { Marshal.FreeHGlobal(memory); }
        }
        void RestoreOwned() {
            if (!owned) return;
            try { if (service != null) Check(service.RestoreAllTaskbarsToDefault()); else foreach (IntPtr hwnd in new List<IntPtr>(applied.Keys)) if (Native.IsWindow(hwnd)) SetLegacy(hwnd,new Appearance { Accent = "normal" }); }
            finally {
                foreach (var pair in peekStyles) if (Native.IsWindow(pair.Key)) Native.SetWindowLongPtr(pair.Key,-20,new IntPtr(pair.Value));
                peekStyles.Clear(); applied.Clear(); appliedAt.Clear(); owned = false;
            }
        }
        void Loop() {
            try {
                while (true) {
                    signal.WaitOne(); List<Decision> batch; bool reset, force, exit; int ticket;
                    lock(gate) { batch = pending; ticket = pendingEpoch; pending = null; reset = restore; restore = false; force = retry; retry = false; exit = quitting; }
                    try {
                        if (reset || exit) { RestoreOwned(); report("Aparência original restaurada",false); }
                        if (exit) break;
                        if (batch == null || !epoch.Accepts(ticket)) continue;
                        if (Native.Conflict(true)) { report("Feche o TranslucentTB para ativar o TaskBAR",true); continue; }
                        if (force) { applied.Clear(); reconnectAfter = DateTime.MinValue; }
                        foreach (Decision d in batch) { if (!epoch.Accepts(ticket)) break; Apply(d,ticket); }
                        if (epoch.Accepts(ticket)) report(service != null ? "Ativo · Windows 11 / XAML" : "Ativo · composição de janela",false);
                    } catch (Exception ex) {
                        lock(gate) { pending = null; }
                        string message = ex.Message;
                        try { RestoreOwned(); } catch(Exception cleanup) { message += " Restauração: " + cleanup.Message; }
                        applied.Clear(); if (epoch.Accepts(ticket)) report("Não foi possível aplicar: " + message,true);
                        if (exit) break;
                    }
                }
            } finally { if (service != null) Marshal.ReleaseComObject(service); }
        }
        public void Dispose() { lock(gate) { epoch.Cancel(); quitting = true; pending = null; } signal.Set(); thread.Join(2500); }
    }
}

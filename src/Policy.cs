using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskBAR {
    public sealed class WindowInfo {
        public IntPtr Handle, Monitor;
        public string Class = "", Title = "", Process = "";
        public bool Maximized, Foreground;
    }
    public sealed class DesktopSnapshot {
        public List<WindowInfo> Windows = new List<WindowInfo>(); // EnumWindows supplies top-to-bottom Z order.
        public List<IntPtr> Taskbars = new List<IntPtr>();
        public bool BatterySaver, TaskView, Peek, Start;
        public IntPtr ShellMonitor;
        public bool Search;
    }
    public sealed class Decision {
        public IntPtr Taskbar;
        public string State, Reason;
        public Appearance Appearance;
    }
    public static class Policy {
        public static bool Matches(WindowInfo w, string kind, string value) {
            if (kind == "window_class") return w.Class == value;
            if (kind == "process_name") return string.Equals(w.Process, value, StringComparison.OrdinalIgnoreCase);
            return w.Title.IndexOf(value, StringComparison.Ordinal) >= 0;
        }
        public static bool Ignored(Settings s, WindowInfo w) { return s.Ignored.Any(pair => pair.Value.Any(v => Matches(w, pair.Key, v))); }
        static Appearance Rule(Settings s, string key, WindowInfo w) {
            foreach (string kind in new [] { "window_class", "process_name", "window_title" })
                foreach (WindowRule r in s.Rules[key]) if (r.Kind == kind && Matches(w, kind, r.Match)) return (!w.Foreground && r.Inactive != null ? r.Inactive : r.Active).Copy();
            return null;
        }
        public static Decision Select(Settings s, DesktopSnapshot d, IntPtr monitor, IntPtr taskbar) {
            string key = Settings.Keys[0]; string reason = "Nenhuma janela elegível"; Appearance result = null;
            var windows = d.Windows.Where(w => w.Monitor == monitor && !Ignored(s, w)).ToList();
            WindowInfo maximized = windows.FirstOrDefault(w => w.Maximized);
            bool start = d.Start && d.ShellMonitor == monitor;
            if (d.BatterySaver && s.Profiles[Settings.Keys[6]].Enabled) { key = Settings.Keys[6]; reason = "Economia de bateria ativa"; }
            else if (d.TaskView && s.Profiles[Settings.Keys[5]].Enabled) { key = Settings.Keys[5]; reason = "Visão de tarefas detectada"; }
            else if (d.Peek) { key = Settings.Keys[0]; reason = "Aero Peek ativo"; }
            else if (start && s.Profiles[Settings.Keys[3]].Enabled) { key = Settings.Keys[3]; reason = "Menu Iniciar aberto"; }
            else if (!start && d.Search && d.ShellMonitor == monitor && s.Profiles[Settings.Keys[4]].Enabled) { key = Settings.Keys[4]; reason = "Pesquisa aberta"; }
            else if (maximized != null && s.Profiles[Settings.Keys[2]].Enabled) { key = Settings.Keys[2]; result = Rule(s, key, maximized); reason = maximized.Process; }
            else if (windows.Count > 0 && s.Profiles[Settings.Keys[1]].Enabled) {
                key = Settings.Keys[1]; var foreground = windows.FirstOrDefault(w => w.Foreground);
                if (maximized == null && foreground != null) result = Rule(s, key, foreground);
                reason = foreground != null ? foreground.Process : "Janela neste monitor";
            }
            return new Decision { Taskbar = taskbar, State = Settings.Names[Array.IndexOf(Settings.Keys, key)], Reason = reason + (result == null ? "" : " · regra personalizada"), Appearance = result ?? s.Profiles[key].Copy() };
        }
    }
}

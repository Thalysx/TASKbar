using System;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace TaskBAR {
    internal static class Customization {
        public static readonly string[] Presets = { "Transparente", "Vidro fosco", "Acrílico discreto", "Sólido escuro", "Adaptativo" };
        public static string WithOpacity(string color, double percent) {
            if (double.IsNaN(percent) || double.IsInfinity(percent)) throw new FormatException("Opacidade inválida.");
            string normalized = Settings.NormalizeColor(color);
            byte alpha = (byte)Math.Round(Math.Max(0,Math.Min(100,percent)) * 255 / 100,MidpointRounding.AwayFromZero);
            return normalized.Substring(0,7) + alpha.ToString("X2");
        }
        public static Settings Preset(Settings original, int index) {
            if (index < 0 || index >= Presets.Length) throw new ArgumentOutOfRangeException("index");
            var candidate = Settings.Parse(original.ToJson());
            foreach (Appearance p in candidate.Profiles.Values) {
                p.Enabled = true; p.ShowLine = false;
                p.Accent = index == 0 || index == 4 ? "clear" : index == 1 ? "blur" : index == 2 ? "acrylic" : "opaque";
                p.Color = index == 0 || index == 4 ? "#00000000" : index == 1 ? "#14202C40" : index == 2 ? "#14202C88" : "#172030FF";
                p.BlurRadius = 18;
            }
            if (index == 4) {
                candidate.Profiles[Settings.Keys[1]].Accent = "blur"; candidate.Profiles[Settings.Keys[1]].Color = "#14202C40";
                candidate.Profiles[Settings.Keys[2]].Accent = "normal";
                candidate.Profiles[Settings.Keys[6]].Accent = "opaque"; candidate.Profiles[Settings.Keys[6]].Color = "#172030FF";
            }
            return candidate;
        }
        public static void UpsertRule(Settings candidate, string scope, string kind, string match, Appearance active, Appearance inactive) {
            if (!candidate.Rules.ContainsKey(scope)) throw new FormatException("Selecione um perfil de janela.");
            if (Array.IndexOf(new [] { "process_name", "window_class", "window_title" },kind) < 0) throw new FormatException("Tipo de regra inválido.");
            match = (match ?? "").Trim(); if (match.Length == 0) throw new FormatException("Informe o aplicativo, a classe ou o título.");
            if (kind == "process_name" && (match.IndexOfAny(new [] { '\\', '/' }) >= 0 || !match.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))) throw new FormatException("Use somente o executável, por exemplo Code.exe.");
            active = active.Copy(); active.Color = Settings.NormalizeColor(active.Color);
            if (active.AccentValue < 0 || active.BlurRadius < 0 || active.BlurRadius > 750 || float.IsNaN(active.BlurRadius) || float.IsInfinity(active.BlurRadius)) throw new FormatException("Aparência inválida.");
            if (inactive != null) { inactive = inactive.Copy(); inactive.Color = Settings.NormalizeColor(inactive.Color); if (inactive.AccentValue < 0) throw new FormatException("Efeito inativo inválido."); }
            var comparison = kind == "process_name" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var existing = candidate.Rules[scope].FirstOrDefault(r => r.Kind == kind && string.Equals(r.Match,match,comparison));
            if (existing == null) candidate.Rules[scope].Add(new WindowRule { Kind=kind,Match=match,Active=active,Inactive=inactive });
            else { existing.Active = active; existing.Inactive = inactive; }
        }
    }
    internal sealed class ScanScheduler {
        DateTime last = DateTime.MinValue;
        bool changed = true;
        public int Scans { get; private set; }
        public void Invalidate() { changed = true; }
        public bool Due(DateTime now, bool force, bool active, bool visible) {
            double seconds = active || visible ? 2 : 5;
            if (!force && now - last < TimeSpan.FromMilliseconds(150)) return false;
            if (!force && !changed && now - last < TimeSpan.FromSeconds(seconds)) return false;
            changed = false; last = now; Scans++; return true;
        }
    }
    internal sealed class RenderEpoch {
        int value;
        public int Current { get { return Volatile.Read(ref value); } }
        public void Cancel() { Interlocked.Increment(ref value); }
        public bool Accepts(int ticket) { return ticket == Current; }
    }
}

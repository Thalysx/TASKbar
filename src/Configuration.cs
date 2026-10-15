using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace TaskBAR {
    public sealed class Appearance {
        public bool Enabled = true, ShowLine = false, ShowPeek = true;
        public string Accent = "clear", Color = "#00000000";
        public float BlurRadius = 9;
        public Appearance Copy() { return (Appearance)MemberwiseClone(); }
        public uint Abgr {
            get { uint rgba = uint.Parse(Color.Substring(1), NumberStyles.HexNumber); return (rgba & 255) << 24 | (rgba >> 8 & 255) << 16 | (rgba >> 16 & 255) << 8 | rgba >> 24; }
        }
        public int AccentValue { get { return Array.IndexOf(new [] { "normal", "opaque", "clear", "blur", "acrylic" }, Accent); } }
        public string Fingerprint { get { return Accent + Color + ShowLine + ShowPeek + BlurRadius.ToString(CultureInfo.InvariantCulture); } }
    }
    public sealed class WindowRule {
        public string Kind, Match;
        public Appearance Active, Inactive;
    }
    public sealed class Settings {
        public static readonly string[] Keys = { "desktop_appearance", "visible_window_appearance", "maximized_window_appearance", "start_opened_appearance", "search_opened_appearance", "task_view_opened_appearance", "battery_saver_appearance" };
        public static readonly string[] Names = { "Área de trabalho", "Janela visível", "Janela maximizada", "Menu Iniciar", "Pesquisa", "Visão de tarefas", "Economia de bateria" };
        public readonly Dictionary<string, Appearance> Profiles = new Dictionary<string, Appearance>();
        public readonly Dictionary<string, List<WindowRule>> Rules = new Dictionary<string, List<WindowRule>>();
        public readonly Dictionary<string, List<string>> Ignored = new Dictionary<string, List<string>>();
        public bool DisableSaving = false;
        public string Verbosity = "info";
        public Settings() {
            foreach (string key in Keys) Profiles[key] = new Appearance();
            Profiles[Keys[0]].ShowPeek = false;
            Profiles[Keys[5]].ShowPeek = false;
            Rules[Keys[1]] = new List<WindowRule>(); Rules[Keys[2]] = new List<WindowRule>();
            foreach (string kind in new [] { "window_class", "process_name", "window_title" }) Ignored[kind] = new List<string>();
        }
        static IDictionary<string, object> Object(object value, string name) {
            var d = value as IDictionary<string, object>;
            if (d == null) throw new FormatException(name + " deve ser um objeto JSON.");
            return d;
        }
        static bool Bool(IDictionary<string, object> d, string key, bool fallback) {
            object v; if (!d.TryGetValue(key, out v)) return fallback;
            if (!(v is bool)) throw new FormatException(key + " deve ser true ou false.");
            return (bool)v;
        }
        static string Str(IDictionary<string, object> d, string key, string fallback) {
            object v; if (!d.TryGetValue(key, out v)) return fallback;
            if (!(v is string)) throw new FormatException(key + " deve ser texto."); return (string)v;
        }
        public static string NormalizeColor(string value) {
            if (value == null || !value.StartsWith("#")) throw new FormatException("Use uma cor como #142034CC.");
            string h = value.Substring(1);
            if (h.Length == 3 || h.Length == 4) { var b = new StringBuilder(); foreach (char c in h) { b.Append(c); b.Append(c); } h = b.ToString(); }
            if (h.Length == 6) h += "FF";
            uint n; if (h.Length != 8 || !uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n)) throw new FormatException("Cor inválida. Use #RRGGBB ou #RRGGBBAA.");
            return "#" + h.ToUpperInvariant();
        }
        static Appearance ReadAppearance(IDictionary<string, object> d, Appearance fallback) {
            var p = fallback.Copy();
            p.Enabled = Bool(d, "enabled", p.Enabled); p.ShowLine = Bool(d, "show_line", p.ShowLine); p.ShowPeek = Bool(d, "show_peek", p.ShowPeek);
            p.Accent = Str(d, "accent", p.Accent).ToLowerInvariant();
            if (p.AccentValue < 0) throw new FormatException("accent: use normal, opaque, clear, blur ou acrylic.");
            p.Color = NormalizeColor(Str(d, "color", p.Color));
            object r; if (d.TryGetValue("blur_radius", out r)) {
                if (r is string || r is bool || r == null) throw new FormatException("blur_radius deve ser um número.");
                p.BlurRadius = Convert.ToSingle(r, CultureInfo.InvariantCulture);
                if (float.IsNaN(p.BlurRadius) || float.IsInfinity(p.BlurRadius) || p.BlurRadius < 0 || p.BlurRadius > 750) throw new FormatException("blur_radius deve estar entre 0 e 750.");
            }
            return p;
        }
        public static Settings Parse(string json) {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 1048576, RecursionLimit = 48 };
            var root = Object(serializer.DeserializeObject(CleanJson(json)), "Configuração");
            var s = new Settings();
            foreach (string key in Keys) {
                object v; if (!root.TryGetValue(key, out v)) continue;
                var d = Object(v, key); s.Profiles[key] = ReadAppearance(d, s.Profiles[key]);
                if (!s.Rules.ContainsKey(key) || !d.TryGetValue("rules", out v)) continue;
                var rules = Object(v, "rules");
                foreach (string kind in new [] { "window_class", "process_name", "window_title" }) {
                    if (!rules.TryGetValue(kind, out v)) continue;
                    foreach (var pair in Object(v, kind)) {
                        if (string.IsNullOrWhiteSpace(pair.Key)) throw new FormatException("Uma regra precisa de um nome.");
                        var rd = Object(pair.Value, "Regra " + pair.Key);
                        var rule = new WindowRule { Kind = kind, Match = pair.Key, Active = ReadAppearance(rd, s.Profiles[key]) };
                        if (rd.TryGetValue("inactive", out v)) rule.Inactive = ReadAppearance(Object(v, "inactive"), rule.Active);
                        s.Rules[key].Add(rule);
                    }
                }
            }
            object ignored;
            if (root.TryGetValue("ignored_windows", out ignored)) {
                var d = Object(ignored, "ignored_windows");
                foreach (string kind in s.Ignored.Keys) {
                    object v; if (!d.TryGetValue(kind, out v)) continue;
                    var items = v as object[]; if (items == null) throw new FormatException(kind + " deve ser uma lista.");
                    foreach (object item in items) { if (!(item is string)) throw new FormatException("Filtros devem conter texto."); s.Ignored[kind].Add((string)item); }
                }
            }
            s.DisableSaving = Bool(root, "disable_saving", false);
            s.Verbosity = Str(root, "verbosity", "info");
            if (Array.IndexOf(new [] { "trace", "debug", "info", "warn", "err", "critical", "off" }, s.Verbosity) < 0) throw new FormatException("verbosity inválida.");
            return s;
        }
        static Dictionary<string, object> WriteAppearance(Appearance p) {
            return new Dictionary<string, object> { { "accent", p.Accent }, { "color", p.Color }, { "show_peek", p.ShowPeek }, { "show_line", p.ShowLine }, { "blur_radius", p.BlurRadius } };
        }
        public string ToJson() {
            var root = new Dictionary<string, object>();
            foreach (string key in Keys) {
                var d = WriteAppearance(Profiles[key]); if (key != Keys[0]) d["enabled"] = Profiles[key].Enabled;
                if (Rules.ContainsKey(key)) {
                    var rules = new Dictionary<string, object>();
                    foreach (string kind in new [] { "window_class", "window_title", "process_name" }) {
                        var map = new Dictionary<string, object>();
                        foreach (WindowRule r in Rules[key]) if (r.Kind == kind) { var a = WriteAppearance(r.Active); if (r.Inactive != null) a["inactive"] = WriteAppearance(r.Inactive); map[r.Match] = a; }
                        rules[kind] = map;
                    }
                    d["rules"] = rules;
                }
                root[key] = d;
            }
            root["ignored_windows"] = Ignored; root["disable_saving"] = DisableSaving; root["verbosity"] = Verbosity;
            return Pretty(new JavaScriptSerializer().Serialize(root));
        }
        public static string CleanJson(string json) {
            var b = new StringBuilder(); bool quoted = false, escaped = false;
            for (int i = 0; i < json.Length; i++) {
                char c = json[i];
                if (quoted) { b.Append(c); if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '"') quoted = false; continue; }
                if (c == '"') { quoted = true; b.Append(c); continue; }
                if (c == '/' && i + 1 < json.Length && json[i + 1] == '/') { while (i < json.Length && json[i] != '\n') i++; b.Append('\n'); continue; }
                if (c == '/' && i + 1 < json.Length && json[i + 1] == '*') { i += 2; while (i + 1 < json.Length && !(json[i] == '*' && json[i + 1] == '/')) i++; if (i + 1 >= json.Length) throw new FormatException("Comentário não terminado."); i++; b.Append(' '); continue; }
                b.Append(c);
            }
            string clean = b.ToString(); b.Clear(); quoted = false; escaped = false;
            for (int i = 0; i < clean.Length; i++) {
                char c = clean[i];
                if (!quoted && c == ',') { int j = i + 1; while (j < clean.Length && char.IsWhiteSpace(clean[j])) j++; if (j < clean.Length && (clean[j] == '}' || clean[j] == ']')) continue; }
                b.Append(c); if (escaped) escaped = false; else if (quoted && c == '\\') escaped = true; else if (c == '"') quoted = !quoted;
            }
            return b.ToString();
        }
        static string Pretty(string json) {
            var b = new StringBuilder(); int indent = 0; bool quoted = false, escaped = false;
            foreach (char c in json) {
                if (quoted) { b.Append(c); if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '"') quoted = false; continue; }
                if (c == '"') { quoted = true; b.Append(c); }
                else if (c == '{' || c == '[') { b.Append(c).AppendLine(); indent++; b.Append(' ', indent * 2); }
                else if (c == '}' || c == ']') { b.AppendLine(); indent--; b.Append(' ', indent * 2).Append(c); }
                else if (c == ',') b.Append(c).AppendLine().Append(' ', indent * 2);
                else if (c == ':') b.Append(": ");
                else b.Append(c);
            }
            return b.ToString();
        }
        public void Save(string path) {
            if (DisableSaving) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)); string temp = path + ".tmp";
            File.WriteAllText(temp, ToJson(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
        }
    }
}

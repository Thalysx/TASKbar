using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TaskBAR {
    internal static class SelfTests {
        static readonly List<string> results = new List<string>();
        static void Expect(string name, bool condition) { if (!condition) throw new Exception("FAIL: " + name); results.Add("PASS " + name); }
        static void Reject(string name, string json) { bool rejected = false; try { Settings.Parse(json); } catch { rejected = true; } Expect(name,rejected); }
        public static int Run(string path) {
            try {
                var m1 = new IntPtr(1); var m2 = new IntPtr(2); var bar = new IntPtr(3);
                var s = new Settings(); var d = new DesktopSnapshot();
                Expect("desktop fallback",Policy.Select(s,d,m1,bar).State == Settings.Names[0]);
                d.Windows.Add(new WindowInfo { Monitor=m1,Process="normal.exe",Foreground=true });
                Expect("normal window",Policy.Select(s,d,m1,bar).State == Settings.Names[1]);
                d.Windows.Insert(0,new WindowInfo { Monitor=m1,Process="max.exe",Maximized=true });
                Expect("maximized outranks normal foreground",Policy.Select(s,d,m1,bar).State == Settings.Names[2]);
                Expect("monitor isolation",Policy.Select(s,d,m2,bar).State == Settings.Names[0]);
                d.Search=true; d.ShellMonitor=m1;
                Expect("search outranks maximized",Policy.Select(s,d,m1,bar).State == Settings.Names[4]);
                d.Start=true;
                Expect("start outranks search",Policy.Select(s,d,m1,bar).State == Settings.Names[3]);
                s.Profiles[Settings.Keys[3]].Enabled=false;
                Expect("disabled start still excludes search",Policy.Select(s,d,m1,bar).State == Settings.Names[2]);
                d.Peek=true;
                Expect("peek chooses desktop",Policy.Select(s,d,m1,bar).State == Settings.Names[0]);
                d.TaskView=true;
                Expect("task view outranks peek",Policy.Select(s,d,m1,bar).State == Settings.Names[5]);
                d.BatterySaver=true;
                Expect("battery outranks task view",Policy.Select(s,d,m1,bar).State == Settings.Names[6]);
                s.Profiles[Settings.Keys[6]].Enabled=false;
                Expect("disabled battery falls through",Policy.Select(s,d,m1,bar).State == Settings.Names[5]);
                d = new DesktopSnapshot(); d.Windows.Add(new WindowInfo { Monitor=m1,Maximized=true,Process="MAX.EXE",Class="Editor",Title="Project A" });
                s.Rules[Settings.Keys[2]].Add(new WindowRule { Kind="process_name",Match="max.exe",Active=new Appearance { Accent="acrylic" },Inactive=new Appearance { Accent="opaque" } });
                Expect("inactive maximized rule",Policy.Select(s,d,m1,bar).Appearance.Accent == "opaque");
                d.Windows[0].Foreground=true;
                Expect("active process rule ignores filename case",Policy.Select(s,d,m1,bar).Appearance.Accent == "acrylic");
                s.Rules[Settings.Keys[2]].Add(new WindowRule { Kind="window_class",Match="Editor",Active=new Appearance { Accent="blur" } });
                Expect("class rule outranks process rule",Policy.Select(s,d,m1,bar).Appearance.Accent == "blur");
                d.Windows.Insert(0,new WindowInfo { Monitor=m1,Maximized=true,Process="top.exe" });
                Expect("only highest maximized rule considered",Policy.Select(s,d,m1,bar).Appearance.Accent == "clear");
                s.Profiles[Settings.Keys[2]].Enabled=false;
                Expect("visible fallback when maximized disabled",Policy.Select(s,d,m1,bar).State == Settings.Names[1]);
                s.Ignored["process_name"].Add("top.exe"); s.Ignored["window_title"].Add("Project");
                Expect("ignored windows excluded",Policy.Select(s,d,m1,bar).State == Settings.Names[0]);
                Expect("JSON round trip preserves rules",Settings.Parse(s.ToJson()).Rules[Settings.Keys[2]].Count == 2);
                Expect("defaults are transparent in every state",new Settings().Profiles.Values.All(p => p.Accent == "clear" && p.Enabled));
                using(var fixture = new StreamReader(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("TaskBAR.ImportFixture.json"))) {
                    var imported = Settings.Parse(fixture.ReadToEnd());
                    Expect("user TranslucentTB JSON imports",!imported.Profiles[Settings.Keys[3]].Enabled && imported.Profiles[Settings.Keys[2]].ShowLine && imported.Profiles[Settings.Keys[0]].Color == "#00000000");
                    Expect("imported configuration round trip",Settings.Parse(imported.ToJson()).Profiles[Settings.Keys[5]].ShowPeek == false);
                }
                var jsonc = Settings.Parse("// comment\n{\"desktop_appearance\":{\"color\":\"#1234\",},\"ignored_windows\":{\"window_title\":[\"https://host/a,}\",],},}");
                Expect("JSONC comments/trailing commas and quoted punctuation",jsonc.Profiles[Settings.Keys[0]].Color == "#11223344" && jsonc.Ignored["window_title"][0] == "https://host/a,}");
                Expect("RGBA to ABGR conversion",new Appearance { Color="#11223344" }.Abgr == 0x44332211);
                Expect("six digit color assumes opaque alpha",Settings.NormalizeColor("#123456") == "#123456FF");
                Expect("opacity preserves RGB with midpoint alpha",Customization.WithOpacity("#11223300",50) == "#11223380");
                Expect("opacity endpoints",Customization.WithOpacity("#11223388",0) == "#11223300" && Customization.WithOpacity("#11223300",100) == "#112233FF");
                var customized=new Settings(); customized.Ignored["process_name"].Add("ignored.exe"); customized.Rules[Settings.Keys[2]].Add(new WindowRule { Kind="process_name",Match="Code.exe",Active=new Appearance { Accent="opaque" } });
                var glass=Customization.Preset(customized,1);
                Expect("presets preserve rules and exclusions",glass.Rules[Settings.Keys[2]][0].Active.Accent == "opaque" && glass.Ignored["process_name"].Contains("ignored.exe"));
                Expect("presets do not mutate original",customized.Profiles[Settings.Keys[0]].Accent == "clear" && glass.Profiles[Settings.Keys[0]].Accent == "blur");
                var adaptive=Customization.Preset(customized,4);
                Expect("adaptive preset follows desktop-window-maximized states",adaptive.Profiles[Settings.Keys[0]].Accent == "clear" && adaptive.Profiles[Settings.Keys[1]].Accent == "blur" && adaptive.Profiles[Settings.Keys[2]].Accent == "normal");
                Customization.UpsertRule(customized,Settings.Keys[2],"process_name","code.EXE",new Appearance { Accent="blur" },null);
                Expect("visual edits replace case-insensitive process rule",customized.Rules[Settings.Keys[2]].Count == 1 && customized.Rules[Settings.Keys[2]][0].Active.Accent == "blur");
                bool badRule=false; try { Customization.UpsertRule(customized,Settings.Keys[2],"process_name","C:\\Code.exe",new Appearance(),null); } catch(FormatException) { badRule=true; }
                Expect("rule editor rejects file paths",badRule && customized.Rules[Settings.Keys[2]].Count == 1);
                var scheduling=new ScanScheduler(); DateTime start=DateTime.UtcNow;
                for(int tick=0;tick<120;tick++) scheduling.Due(start.AddMilliseconds(tick*250),false,false,false);
                Expect("background idle scans reduced to six per thirty seconds",scheduling.Scans == 6);
                scheduling.Invalidate(); Expect("window event prompts refresh",scheduling.Due(start.AddSeconds(30),false,true,false));
                scheduling.Invalidate(); Expect("event bursts coalesced",!scheduling.Due(start.AddSeconds(30).AddMilliseconds(10),false,true,false));
                Expect("manual changes refresh immediately",scheduling.Due(start.AddSeconds(30).AddMilliseconds(10),true,true,false));
                var epoch=new RenderEpoch(); int ticket=epoch.Current; epoch.Cancel();
                Expect("pause invalidates pending render ticket",!epoch.Accepts(ticket) && epoch.Accepts(epoch.Current));
                Expect("startup command quotes paths and enables hidden mode",WindowsStartup.CommandFor("C:\\Apps with spaces\\TaskBAR.exe") == "\"C:\\Apps with spaces\\TaskBAR.exe\" --startup");
                Reject("invalid effect rejected","{\"desktop_appearance\":{\"accent\":\"oops\"}}");
                Reject("invalid color rejected","{\"desktop_appearance\":{\"color\":\"#XX000000\"}}");
                Reject("negative radius rejected","{\"desktop_appearance\":{\"blur_radius\":-1}}");
                Reject("radius above maximum rejected","{\"desktop_appearance\":{\"blur_radius\":751}}");
                Reject("boolean type enforced","{\"desktop_appearance\":{\"enabled\":\"true\"}}");
                Reject("malformed JSON rejected","{\"desktop_appearance\":");
                Reject("unterminated comments rejected","{/* broken");
                string tempDir = Path.Combine(Path.GetDirectoryName(path),"persistence-test"); Directory.CreateDirectory(tempDir); string file=Path.Combine(tempDir,"settings.json");
                var persisted=new Settings(); persisted.Save(file); persisted.Profiles[Settings.Keys[0]].Color="#12345678"; persisted.Save(file);
                Expect("atomic save and backup",File.Exists(file+".bak") && Settings.Parse(File.ReadAllText(file)).Profiles[Settings.Keys[0]].Color == "#12345678");
                persisted.DisableSaving=true; persisted.Profiles[Settings.Keys[0]].Color="#00000000"; persisted.Save(file);
                Expect("disable saving preserves disk",Settings.Parse(File.ReadAllText(file)).Profiles[Settings.Keys[0]].Color == "#12345678");
                File.Delete(file); File.Delete(file+".bak"); Directory.Delete(tempDir);
                results.Add("SUCCESS " + (results.Count) + " assertions"); File.WriteAllLines(path,results); return 0;
            } catch(Exception ex) { results.Add(ex.ToString()); File.WriteAllLines(path,results); return 1; }
        }
    }
}

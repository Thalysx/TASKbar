using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace TaskBAR {
    internal sealed class TrayController : ApplicationContext {
        readonly string configPath,logPath;
        readonly Control dispatch=new Control();
        readonly ScanScheduler scheduler=new ScanScheduler();
        readonly WindowObserver observer;
        readonly Renderer renderer;
        readonly Timer timer;
        readonly Icon icon;
        readonly NotifyIcon tray;
        readonly CompactTrayMenu menu;
        Settings settings=new Settings();
        DateTime savedAt=DateTime.MinValue;
        bool wanted,disposed;
        string lastStatus="";
        bool lastStatusError;
        Process editor;
        public TrayController(string storage=null,bool autoEnable=true) {
            string folder=storage ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data");
            configPath=Path.Combine(folder,"settings.json"); logPath=Path.Combine(folder,"taskbar.log");
            wanted=autoEnable;
            string initialError=null;
            try { if(File.Exists(configPath)) settings=Settings.Parse(File.ReadAllText(configPath)); else settings.Save(configPath); savedAt=File.GetLastWriteTimeUtc(configPath); }
            catch(Exception ex) { wanted=false; initialError="Configuração inválida: "+ex.Message; }
            IntPtr handle=dispatch.Handle;
            observer=new WindowObserver(delegate { scheduler.Invalidate(); });
            renderer=new Renderer(delegate(string status,bool error) {
                if(disposed) return;
                try { dispatch.BeginInvoke(new Action(delegate { if(disposed) return; if(error) wanted=false; Status(status,error); })); } catch(InvalidOperationException) { }
            });
            icon=Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            menu=new CompactTrayMenu(ChangeProfile,ApplyPreset,OpenAdvanced,Toggle,ToggleStartup,ShowTips,delegate { ExitThread(); });
            tray=new NotifyIcon { Icon=icon,Text="TaskBAR",Visible=true,ContextMenuStrip=menu };
            tray.MouseClick+=delegate(object sender,MouseEventArgs e) { if(e.Button==MouseButtons.Left) menu.Show(Cursor.Position); };
            tray.DoubleClick+=delegate { menu.Show(Cursor.Position); };
            menu.Opening+=delegate { RefreshMenu(); };
            RefreshMenu();
            timer=new Timer { Interval=250 }; timer.Tick+=delegate { Tick(false); }; timer.Start();
            if(initialError!=null) Status(initialError,true); else Status(wanted ? "Aguardando a barra de tarefas" : "Pausado",false);
            Tick(true);
        }
        bool EditorOpen() {
            if(editor==null) return false;
            try { if(!editor.HasExited) return true; } catch(InvalidOperationException) { }
            editor.Dispose(); editor=null; return false;
        }
        void RefreshMenu() {
            bool xaml=false; foreach(IntPtr bar in Native.Taskbars()) if(Native.TaskbarKind(bar)=="xaml") { xaml=true; break; }
            bool boot=false; try { boot=WindowsStartup.Enabled; } catch { }
            menu.Refresh(settings,wanted,boot,EditorOpen(),xaml);
        }
        void Save(Settings candidate) {
            candidate.Save(configPath); settings=candidate;
            if(File.Exists(configPath)) savedAt=File.GetLastWriteTimeUtc(configPath);
            scheduler.Invalidate(); Tick(true);
        }
        void ChangeProfile(string key,Action<Appearance> change) {
            if(EditorOpen()) { Status("Feche a configuração avançada antes de alterar o perfil",true); return; }
            try { var candidate=Settings.Parse(settings.ToJson()); change(candidate.Profiles[key]); Save(candidate); }
            catch(Exception ex) { Status("Não foi possível salvar: "+ex.Message,true); }
        }
        void ApplyPreset(int index) {
            if(EditorOpen()) return;
            try { Save(Customization.Preset(settings,index)); } catch(Exception ex) { Status(ex.Message,true); }
        }
        void OpenAdvanced(string page) {
            if(page.StartsWith("color:",StringComparison.Ordinal)) {
                string key=page.Substring(6); string color=settings.Profiles[key].Color; uint rgba=uint.Parse(color.Substring(1),NumberStyles.HexNumber);
                using(var picker=new ColorDialog { FullOpen=true,Color=Color.FromArgb((int)(rgba>>24),(int)(rgba>>16 & 255),(int)(rgba>>8 & 255)) }) {
                    if(picker.ShowDialog()==DialogResult.OK) { string next="#"+picker.Color.R.ToString("X2")+picker.Color.G.ToString("X2")+picker.Color.B.ToString("X2")+color.Substring(7,2); ChangeProfile(key,delegate(Appearance p) { p.Color=next; }); }
                }
                return;
            }
            if(EditorOpen()) { Status("A configuração avançada já está aberta",false); return; }
            try { editor=Process.Start(new ProcessStartInfo { FileName=Assembly.GetExecutingAssembly().Location,Arguments="--settings "+page,UseShellExecute=false }); }
            catch(Exception ex) { Status("Não foi possível abrir a configuração: "+ex.Message,true); }
        }
        void ToggleStartup() { try { WindowsStartup.SetEnabled(!WindowsStartup.Enabled); } catch(Exception ex) { Status(ex.Message,true); } }
        void ShowTips() {
            MessageBox.Show("Clique no ícone da bandeja para abrir o menu.\n\nEscolha um perfil e ajuste o efeito, a cor e a opacidade. As mudanças são salvas automaticamente.\n\nEm Avançado você encontra presets, regras, JSON e a opção de pausar e restaurar o Windows.","TaskBAR · Dicas e atalhos",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        void Toggle() {
            wanted=!wanted;
            if(!wanted) { renderer.Restore(); Status("Pausado · aparência original",false); }
            else { scheduler.Invalidate(); Tick(true); }
        }
        void Tick(bool force) {
            if(disposed || !scheduler.Due(DateTime.UtcNow,force,wanted,false)) return;
            try {
                if(File.Exists(configPath) && File.GetLastWriteTimeUtc(configPath)!=savedAt) {
                    try { settings=Settings.Parse(File.ReadAllText(configPath)); savedAt=File.GetLastWriteTimeUtc(configPath); }
                    catch(Exception ex) { Status("JSON inválido; últimos perfis preservados: "+ex.Message,true); }
                }
                if(!wanted) return;
                if(Native.Conflict()) { Status("Aguardando o TranslucentTB encerrar",false); return; }
                DesktopSnapshot snapshot=observer.Capture(); var decisions=new List<Decision>();
                foreach(IntPtr bar in snapshot.Taskbars) decisions.Add(Policy.Select(settings,snapshot,Native.MonitorFromWindow(bar,2),bar));
                if(decisions.Count>0) renderer.Submit(decisions,force); else Status("Aguardando a barra de tarefas",false);
            } catch(Exception ex) { Status("Não foi possível observar a barra: "+ex.Message,true); }
        }
        void Status(string status,bool error) {
            string tooltip="TaskBAR · "+status; tray.Text=tooltip.Length>63 ? tooltip.Substring(0,63) : tooltip;
            if(status==lastStatus && error==lastStatusError) return; lastStatus=status; lastStatusError=error;
            if(error) tray.ShowBalloonTip(5000,"TaskBAR",status,ToolTipIcon.Warning);
            try {
                if(settings.Verbosity=="off") return;
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                if(File.Exists(logPath) && new FileInfo(logPath).Length>1048576) File.WriteAllText(logPath,"");
                File.AppendAllText(logPath,DateTimeOffset.Now.ToString("o")+" "+status+Environment.NewLine);
            } catch { }
        }
        internal void CheckMenu(string output,string preview,string profilePreview) {
            var checks=new List<string>();
            if(wanted) throw new Exception("O teste não deve aplicar efeitos.");
            if(menu.Items.Count!=12 || !tray.Visible) throw new Exception("Menu compacto ou ícone ausente.");
            checks.Add("PASS tray-first context without a dashboard window");
            for(int i=0;i<6;i++) if(menu.Items[i].Text!=Settings.Names[i]) throw new Exception("Ordem dos perfis incorreta.");
            checks.Add("PASS six primary profiles in reference order");
            var desktop=(ToolStripMenuItem)menu.Items[0];
            if(((ToolStripMenuItem)desktop.DropDownItems[2]).Tag.ToString()!="clear" || !((ToolStripMenuItem)desktop.DropDownItems[2]).Checked) throw new Exception("Efeito selecionado não marcado.");
            checks.Add("PASS current accent indicated in submenu");
            menu.SavePreview(preview); menu.SavePreview(profilePreview,0);
            ((ToolStripMenuItem)desktop.DropDownItems[3]).PerformClick();
            if(settings.Profiles[Settings.Keys[0]].Accent!="blur" || Settings.Parse(File.ReadAllText(configPath)).Profiles[Settings.Keys[0]].Accent!="blur") throw new Exception("Efeito não foi salvo pelo menu.");
            checks.Add("PASS submenu action persists selected effect");
            ChangeProfile(Settings.Keys[0],delegate(Appearance p) { p.Color=Customization.WithOpacity("#11223300",50); });
            if(settings.Profiles[Settings.Keys[0]].Color!="#11223380") throw new Exception("Opacidade não preservou RGB.");
            checks.Add("PASS tray opacity preserves color channels");
            Customization.UpsertRule(settings,Settings.Keys[2],"process_name","Code.exe",new Appearance { Accent="opaque" },null);
            ApplyPreset(0);
            if(settings.Rules[Settings.Keys[2]].Count!=1 || settings.Rules[Settings.Keys[2]][0].Active.Accent!="opaque") throw new Exception("Preset apagou regras.");
            checks.Add("PASS presets preserve custom rules");
            RefreshMenu(); menu.SavePreview(preview);
            if(System.Windows.Application.Current!=null && System.Windows.Application.Current.Windows.Count>0) throw new Exception("Janela WPF foi criada no modo de bandeja.");
            File.WriteAllLines(output,checks,new UTF8Encoding(false));
        }
        protected override void ExitThreadCore() { Dispose(); base.ExitThreadCore(); }
        protected override void Dispose(bool disposing) {
            if(disposing && !disposed) {
                disposed=true; timer.Stop(); timer.Dispose(); observer.Dispose(); renderer.Dispose();
                if(EditorOpen()) { try { editor.CloseMainWindow(); } catch { } editor.Dispose(); editor=null; }
                tray.Visible=false; tray.Dispose(); menu.Dispose(); icon.Dispose(); dispatch.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

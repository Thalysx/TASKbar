using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace TaskBAR {
    internal sealed partial class MainWindowController : IDisposable {
        public readonly Window Window;
        readonly string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","settings.json");
        readonly string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","taskbar.log");
        readonly FrameworkElement rulesPage;
        readonly ScanScheduler scheduler = new ScanScheduler();
        sealed class RuleLocation { public string Scope; public WindowRule Rule; public override string ToString() { return Settings.Names[Array.IndexOf(Settings.Keys,Scope)] + " · " + Rule.Kind.Replace("process_name","Processo").Replace("window_class","Classe").Replace("window_title","Título") + " · " + Rule.Match; } }
        RuleLocation editingRule;
        bool syncingOpacity;
        Settings settings = new Settings();
        WindowObserver observer;
        Renderer renderer;
        DispatcherTimer timer;
        Forms.NotifyIcon tray;
        bool active, loading, exiting, dirty, jsonDirty;
        bool startupPending;
        int selected = -1;
        DateTime savedAt = DateTime.MinValue;
        string lastStatus;
        public MainWindowController(bool preview, string storageDirectory = null, bool editorOnly = false) {
            if (storageDirectory != null) { configPath=Path.Combine(storageDirectory,"settings.json"); logPath=Path.Combine(storageDirectory,"taskbar.log"); }
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TaskBAR.MainWindow.xaml")) Window = (Window)XamlReader.Load(stream);
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TaskBAR.RulesPage.xaml")) rulesPage = (FrameworkElement)XamlReader.Load(stream);
            Get<Grid>("PageArea").Children.Add(rulesPage);
            SetupCustomization();
            if (!preview) {
                try { if (File.Exists(configPath)) { settings = Settings.Parse(File.ReadAllText(configPath)); savedAt = File.GetLastWriteTimeUtc(configPath); } else { settings.Save(configPath); savedAt = File.GetLastWriteTimeUtc(configPath); } }
                catch(Exception ex) { SetStatus("Configuração não carregada: " + ex.Message,true); }
            }
            var profiles = Get<ListBox>("Profiles");
            foreach (string name in Settings.Names) profiles.Items.Add(name);
            foreach (string name in new [] { "Padrão do Windows", "Opaco", "Transparente", "Desfoque", "Acrílico" }) Get<ComboBox>("Effect").Items.Add(name);
            profiles.SelectionChanged += delegate {
                if (loading) return;
                int next = profiles.SelectedIndex;
                if (!GuardJson()) { loading = true; profiles.SelectedIndex = selected; loading = false; return; }
                if (dirty && !SaveProfile()) { loading = true; profiles.SelectedIndex = selected; loading = false; return; }
                selected = next; ShowPage("editor"); LoadProfile();
            };
            profiles.PreviewMouseLeftButtonUp += delegate { if (GuardJson()) { ShowPage("editor"); if (!dirty) LoadProfile(); } };
            Get<ComboBox>("Effect").SelectionChanged += delegate { if (!loading) { dirty = true; UpdatePreview(); } };
            foreach (string name in new [] { "HexColor", "BlurRadius" }) Get<TextBox>(name).TextChanged += delegate { if (!loading) { dirty = true; UpdatePreview(); } };
            foreach (string name in new [] { "Enabled", "ShowLine", "ShowPeek" }) { Get<CheckBox>(name).Checked += Changed; Get<CheckBox>(name).Unchecked += Changed; }
            Click("SaveButton",delegate { SaveProfile(); });
            Click("TransparentButton",delegate {
                if (!GuardJson()) return;
                try {
                    var candidate = Settings.Parse(settings.ToJson());
                    foreach (Appearance p in candidate.Profiles.Values) { p.Enabled = true; p.Accent = "clear"; p.Color = "#00000000"; p.ShowLine = false; }
                    foreach (var list in candidate.Rules.Values) foreach (WindowRule r in list) { r.Active.Accent = "clear"; r.Active.Color = "#00000000"; if (r.Inactive != null) { r.Inactive.Accent = "clear"; r.Inactive.Color = "#00000000"; } }
                    Commit(candidate); dirty = false; LoadProfile(); Get<TextBlock>("EditMessage").Text = "Todos os perfis estão transparentes."; Tick(true);
                } catch(Exception ex) { Get<TextBlock>("EditMessage").Text = ex.Message; }
            });
            Click("ActivateButton",delegate { Toggle(); });
            Click("RestoreButton",delegate { Pause(); });
            Click("TrayButton",delegate { Window.Hide(); });
            Click("AboutButton",delegate { ShowPage("about"); });
            Click("JsonButton",delegate { if (dirty && !SaveProfile()) return; if (!jsonDirty) LoadJson(); ShowPage("json"); });
            Get<TextBox>("JsonEditor").TextChanged += delegate { if (!loading) jsonDirty = true; };
            Click("ApplyJsonButton",delegate {
                try { var candidate = Settings.Parse(Get<TextBox>("JsonEditor").Text); Commit(candidate); dirty = false; jsonDirty = false; LoadProfile(); Get<TextBlock>("JsonMessage").Text = candidate.DisableSaving ? "JSON válido e aplicado à sessão. A gravação está desabilitada." : "JSON válido, salvo e aplicado."; Tick(true); }
                catch(Exception ex) { Get<TextBlock>("JsonMessage").Text = "Configuração preservada. " + ex.Message; }
            });
            Click("ImportButton",delegate { Import(); });
            Click("ExportButton",delegate { Export(); });
            Click("ColorButton",delegate {
                using (var dialog = new Forms.ColorDialog()) {
                    string h; try { h = Settings.NormalizeColor(Get<TextBox>("HexColor").Text); } catch { h = "#00000000"; }
                    uint rgba = uint.Parse(h.Substring(1),NumberStyles.HexNumber);
                    dialog.Color = System.Drawing.Color.FromArgb((int)(rgba >> 24),(int)(rgba >> 16 & 255),(int)(rgba >> 8 & 255));
                    if (dialog.ShowDialog() == Forms.DialogResult.OK) Get<TextBox>("HexColor").Text = "#" + dialog.Color.R.ToString("X2") + dialog.Color.G.ToString("X2") + dialog.Color.B.ToString("X2") + h.Substring(7,2);
                }
            });
            profiles.SelectedIndex = 0;
            if (preview) { Get<TextBlock>("MonitorStatus").Text = "Monitor 1 · Área de trabalho · transparente"; return; }
            if(editorOnly) {
                Window.Title="TaskBAR · Configuração avançada";
                Get<Button>("ActivateButton").Visibility=Visibility.Collapsed;
                Get<Button>("RestoreButton").Visibility=Visibility.Collapsed;
                Get<Button>("TrayButton").Visibility=Visibility.Collapsed;
                Get<TextBlock>("MonitorStatus").Text="O aplicativo da bandeja carrega automaticamente as alterações salvas.";
                SetStatus("Configuração avançada · efeitos controlados pelo menu da bandeja",false);
                return;
            }
            observer = new WindowObserver(delegate { scheduler.Invalidate(); });
            renderer = new Renderer(delegate(string status, bool error) { Window.Dispatcher.BeginInvoke(new Action(delegate { if (exiting) return; if (error) { active = false; Get<Button>("ActivateButton").Content = "Ativar TaskBAR"; } SetStatus(status,error); })); });
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) }; timer.Tick += delegate { Tick(false); }; timer.Start();
            tray = new Forms.NotifyIcon { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location), Text = "TaskBAR · pausado", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Abrir TaskBAR",null,delegate { Open(); });
            menu.Items.Add("Ativar / pausar",null,delegate { Toggle(); });
            menu.Items.Add("Restaurar Windows",null,delegate { Pause(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Sair",null,delegate { Exit(); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { Open(); };
            Window.Closing += delegate(object sender,System.ComponentModel.CancelEventArgs e) { if (!exiting) { e.Cancel = true; Window.Hide(); } };
            Tick(true);
        }
        T Get<T>(string name) where T : class { return (T)(Window.FindName(name) ?? rulesPage.FindName(name)); }
        void Click(string name, Action callback) { Get<Button>(name).Click += delegate { callback(); }; }
        void Changed(object sender, RoutedEventArgs e) { if (!loading) { dirty = true; UpdatePreview(); } }
        void ShowPage(string page) {
            Get<Grid>("EditorPage").Visibility = page == "editor" ? Visibility.Visible : Visibility.Collapsed;
            Get<Grid>("JsonPage").Visibility = page == "json" ? Visibility.Visible : Visibility.Collapsed;
            Get<StackPanel>("AboutPage").Visibility = page == "about" ? Visibility.Visible : Visibility.Collapsed;
            rulesPage.Visibility = page == "rules" ? Visibility.Visible : Visibility.Collapsed;
            Get<TextBlock>("PageTitle").Text = page == "editor" ? Settings.Names[Math.Max(0,selected)] : page == "json" ? "Regras e configuração" : page == "rules" ? "Regras por aplicativo" : "Sobre o TaskBAR";
            Get<TextBlock>("PageDescription").Text = page == "editor" ? "Escolha como a barra aparece neste estado." : page == "json" ? "Controle os detalhes em um único arquivo." : page == "rules" ? "Personalize cada aplicativo sem editar JSON." : "Uma experiência própria para sua barra de tarefas.";
        }
        void LoadProfile() {
            if (selected < 0) return; loading = true;
            var p = settings.Profiles[Settings.Keys[selected]];
            Get<ComboBox>("Effect").SelectedIndex = p.AccentValue;
            Get<TextBox>("HexColor").Text = p.Color; Get<TextBox>("BlurRadius").Text = p.BlurRadius.ToString(CultureInfo.InvariantCulture);
            Get<CheckBox>("Enabled").IsChecked = selected == 0 || p.Enabled; Get<CheckBox>("Enabled").IsEnabled = selected != 0;
            Get<CheckBox>("ShowLine").IsChecked = p.ShowLine; Get<CheckBox>("ShowPeek").IsChecked = p.ShowPeek;
            Get<TextBlock>("EditMessage").Text = ""; loading = false; dirty = false; UpdatePreview();
        }
        bool SaveProfile() {
            if (selected < 0) return true;
            if (!GuardJson()) return false;
            try {
                var candidate = Settings.Parse(settings.ToJson()); var p = candidate.Profiles[Settings.Keys[selected]];
                p.Accent = new [] { "normal", "opaque", "clear", "blur", "acrylic" }[Get<ComboBox>("Effect").SelectedIndex];
                p.Color = Settings.NormalizeColor(Get<TextBox>("HexColor").Text);
                float radius; if (!float.TryParse(Get<TextBox>("BlurRadius").Text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out radius) || float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0 || radius > 750) throw new FormatException("O raio deve estar entre 0 e 750.");
                p.BlurRadius = radius; p.Enabled = selected == 0 || Get<CheckBox>("Enabled").IsChecked == true;
                p.ShowLine = Get<CheckBox>("ShowLine").IsChecked == true; p.ShowPeek = Get<CheckBox>("ShowPeek").IsChecked == true;
                Commit(candidate); dirty = false;
                Get<TextBlock>("EditMessage").Text = settings.DisableSaving ? "Perfil aplicado à sessão. A gravação está desabilitada." : "Perfil salvo."; Tick(true); return true;
            } catch(Exception ex) { Get<TextBlock>("EditMessage").Text = ex.Message; return false; }
        }
        void UpdatePreview() {
            try {
                string h = Settings.NormalizeColor(Get<TextBox>("HexColor").Text); uint rgba = uint.Parse(h.Substring(1),NumberStyles.HexNumber);
                byte alpha = (byte)rgba; int effect = Get<ComboBox>("Effect").SelectedIndex;
                Color c = Color.FromArgb(effect == 1 ? (byte)255 : effect == 0 ? (byte)230 : alpha,(byte)(rgba >> 24),(byte)(rgba >> 16),(byte)(rgba >> 8));
                Get<Border>("PreviewBar").Background = new SolidColorBrush(c);
                Get<Border>("PreviewBar").BorderThickness = new Thickness(0,Get<CheckBox>("ShowLine").IsChecked == true ? 1 : 0,0,0);
                Get<TextBlock>("PreviewCaption").Text = Get<ComboBox>("Effect").Text + " · opacidade " + (effect == 1 ? 100 : Math.Round(alpha / 255.0 * 100)).ToString() + "%" + (dirty ? " · alterações por salvar" : "");
                Get<TextBox>("BlurRadius").IsEnabled = effect == 3;
                var slider = Window.FindName("OpacitySlider") as Slider;
                if (slider != null) {
                    syncingOpacity = true; slider.Value = Math.Round(alpha / 255.0 * 100); syncingOpacity = false;
                    slider.IsEnabled = effect > 1;
                    Get<TextBlock>("OpacityValue").Text = effect == 0 ? "Controlada pelo Windows" : effect == 1 ? "100% · efeito opaco" : slider.Value.ToString("0") + "%";
                }
            } catch { Get<TextBlock>("PreviewCaption").Text = "Informe uma cor válida para atualizar a prévia."; }
        }
        void Commit(Settings candidate) {
            candidate.Save(configPath); settings = candidate; if (File.Exists(configPath)) savedAt = File.GetLastWriteTimeUtc(configPath); jsonDirty = false;
        }
        void Persist(string success) { try { Commit(settings); Get<TextBlock>("EditMessage").Text = success; Tick(true); } catch(Exception ex) { Get<TextBlock>("EditMessage").Text = "Não foi possível salvar: " + ex.Message; } }
        void Import() {
            if (!GuardJson()) return;
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Configuração JSON|*.json|Todos os arquivos|*.*", Title = "Importar configuração" };
            if (dialog.ShowDialog(Window) != true) return;
            try { var candidate = Settings.Parse(File.ReadAllText(dialog.FileName)); Commit(candidate); dirty = false; jsonDirty = false; LoadProfile(); Get<TextBlock>("EditMessage").Text = "Configuração importada."; Tick(true); }
            catch(Exception ex) { SetStatus("Importação não realizada: " + ex.Message,true); }
        }
        void Export() {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Configuração JSON|*.json", FileName = "taskbar-settings.json", Title = "Exportar configuração" };
            if (dialog.ShowDialog(Window) != true) return;
            try { File.WriteAllText(dialog.FileName,settings.ToJson(),new UTF8Encoding(false)); Get<TextBlock>("JsonMessage").Text = "Configuração exportada."; } catch(Exception ex) { Get<TextBlock>("JsonMessage").Text = ex.Message; }
        }
        public void Tick(bool force) {
            if (observer == null || exiting) return;
            try {
                if (!scheduler.Due(DateTime.UtcNow,force,active,Window.IsVisible)) return;
                bool conflict = Native.Conflict(); Get<Border>("ConflictPanel").Visibility = conflict ? Visibility.Visible : Visibility.Collapsed;
                if (active && conflict) { active = false; Get<Button>("ActivateButton").Content = "Ativar TaskBAR"; renderer.Restore(); SetStatus("Feche o TranslucentTB para ativar o TaskBAR",true); }
                if (!dirty && !jsonDirty && File.Exists(configPath) && File.GetLastWriteTimeUtc(configPath) != savedAt) {
                    settings = Settings.Parse(File.ReadAllText(configPath)); savedAt = File.GetLastWriteTimeUtc(configPath); LoadProfile();
                }
                var snapshot = observer.Capture(); var batch = new List<Decision>(); var lines = new List<string>(); int index = 1;
                foreach (IntPtr bar in snapshot.Taskbars) { var choice = Policy.Select(settings,snapshot,Native.MonitorFromWindow(bar,2),bar); batch.Add(choice); lines.Add("Monitor " + index++ + " · " + choice.State + " · " + choice.Appearance.Accent + " · " + choice.Reason); }
                Get<TextBlock>("MonitorStatus").Text = lines.Count > 0 ? string.Join("\n",lines) : "A barra de tarefas não foi encontrada. Aguardando o Explorer…";
                if (startupPending && !active && batch.Count > 0 && !conflict) {
                    startupPending=false; active=true; Get<Button>("ActivateButton").Content="Pausar";
                    SetStatus("Inicialização automática · aplicando seus perfis",false);
                }
                if (active && batch.Count > 0) renderer.Submit(batch,force);
                if (tray != null) tray.Text = "TaskBAR · " + (active ? "ativo" : "pausado");
            } catch(Exception ex) { SetStatus("Falha ao observar o desktop: " + ex.Message,true); }
        }
        public void Toggle() {
            if (active) { Pause(); return; }
            if (dirty && !SaveProfile()) return;
            if (Native.Conflict(true)) { SetStatus("Encerre o TranslucentTB pelo ícone da bandeja e tente novamente.",true); return; }
            active = true; Get<Button>("ActivateButton").Content = "Pausar"; SetStatus("Conectando à barra de tarefas…",false); Tick(true);
        }
        public void Pause() { startupPending=false; active = false; Get<Button>("ActivateButton").Content = "Ativar TaskBAR"; if (renderer != null) renderer.Restore(); SetStatus("Pausado · restaurando aparência original",false); }
        internal void PrepareAutomaticStartup() { startupPending=true; scheduler.Invalidate(); Window.Hide(); SetStatus("Inicialização automática · aguardando a barra de tarefas",false); }
        internal void CheckAutomaticStartup(string output) {
            PrepareAutomaticStartup();
            if (Window.IsVisible || !startupPending || tray == null || !tray.Visible || active) throw new Exception("Inicialização pela bandeja não foi preparada corretamente.");
            System.IO.File.WriteAllText(output,"PASS startup stays hidden with tray visible\nPASS profile activation waits for Explorer\n");
        }
        internal void CheckStartup(string output) {
            var checks = new List<string>();
            if (active || Get<Button>("ActivateButton").Content.ToString() != "Ativar TaskBAR") throw new Exception("O aplicativo deveria iniciar pausado.");
            checks.Add("PASS starts paused");
            if (tray == null || !tray.Visible) throw new Exception("O ícone da bandeja não foi criado.");
            checks.Add("PASS tray icon created");
            if (Native.Conflict()) { Toggle(); if (active) throw new Exception("Conflito não bloqueou a ativação."); checks.Add("PASS TranslucentTB conflict blocks activation"); }
            Get<ListBox>("Profiles").SelectedIndex = 2;
            if (Get<TextBlock>("PageTitle").Text != "Janela maximizada") throw new Exception("Navegação do perfil falhou.");
            checks.Add("PASS profile navigation");
            string savedColor=Get<TextBox>("HexColor").Text;
            Get<TextBox>("HexColor").Text="#11223300"; Get<Slider>("OpacitySlider").Value=50;
            if (Get<TextBox>("HexColor").Text != "#11223380") throw new Exception("Controle de opacidade alterou os canais RGB.");
            checks.Add("PASS opacity slider preserves RGB");
            loading=true; Get<TextBox>("HexColor").Text=savedColor; loading=false; dirty=false; UpdatePreview();
            ShowPage("rules"); Get<TextBox>("RuleMatch").Text="Code.exe"; Get<ComboBox>("RuleEffect").SelectedIndex=4; Get<TextBox>("RuleColor").Text="#18233888"; SaveRule();
            if (settings.Rules[Settings.Keys[2]].Count != 1 || settings.Rules[Settings.Keys[2]][0].Active.Accent != "acrylic") throw new Exception("Editor de regras não salvou a aparência.");
            checks.Add("PASS visual rule editor persists rule");
            Get<ListBox>("RuleList").SelectedIndex=0; Get<Button>("RemoveRuleButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if (settings.Rules[Settings.Keys[2]].Count != 0) throw new Exception("Remoção de regra falhou.");
            checks.Add("PASS visual rule editor removes selected rule");
            string previous = settings.ToJson();
            Get<Button>("JsonButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Get<TextBox>("JsonEditor").Text = "{invalid";
            Get<Button>("ApplyJsonButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if (settings.ToJson() != previous || !Get<TextBlock>("JsonMessage").Text.StartsWith("Configuração preservada.")) throw new Exception("JSON inválido não preservou o estado.");
            checks.Add("PASS invalid JSON preserves active settings");
            int oldSelection=Get<ListBox>("Profiles").SelectedIndex; Get<ListBox>("Profiles").SelectedIndex=1;
            if (Get<ListBox>("Profiles").SelectedIndex != oldSelection || Get<Grid>("JsonPage").Visibility != Visibility.Visible) throw new Exception("Edição JSON pendente foi descartada pela navegação.");
            checks.Add("PASS pending JSON protected during navigation");
            checks.Add("Observed: " + Get<TextBlock>("MonitorStatus").Text);
            File.WriteAllLines(output,checks);
        }
        internal void PreviewPage(string page) { if (page == "rules") { RefreshRules(); ShowPage(page); } }
        internal void SettingsPage(string page) { if(page=="rules") { RefreshRules(); ResetRule(); ShowPage("rules"); } else if(page=="json") { LoadJson(); ShowPage("json"); } }
        internal void CheckEditorMode(string output) {
            if(tray!=null || renderer!=null || observer!=null || timer!=null) throw new Exception("A janela avançada criou um segundo controlador.");
            if(Get<Button>("ActivateButton").Visibility!=Visibility.Collapsed) throw new Exception("Controle duplicado de efeitos visível.");
            Get<TextBox>("HexColor").Text="#11223380";
            if(!SaveProfile() || Settings.Parse(File.ReadAllText(configPath)).Profiles[Settings.Keys[0]].Color!="#11223380") throw new Exception("Editor avançado não salvou a configuração.");
            File.WriteAllText(output,"PASS advanced editor has no tray, timer or renderer\nPASS advanced editor persists configuration for tray controller\n");
        }
        void SetStatus(string text, bool error) {
            Get<TextBlock>("Status").Text = text; Get<TextBlock>("Status").Foreground = new SolidColorBrush(error ? Color.FromRgb(240,195,133) : Color.FromRgb(125,217,196));
            if (text == lastStatus) return; lastStatus = text;
            if (settings.Verbosity == "off" || (!error && Array.IndexOf(new [] { "trace", "debug", "info", "warn", "err", "critical" },settings.Verbosity) > 2)) return;
            try { Directory.CreateDirectory(Path.GetDirectoryName(logPath)); if (File.Exists(logPath) && new FileInfo(logPath).Length > 1048576) File.WriteAllText(logPath,""); File.AppendAllText(logPath,DateTimeOffset.Now.ToString("o") + " " + text + Environment.NewLine); } catch { }
        }
        public void Open() { Window.Show(); Window.WindowState = WindowState.Normal; Window.Activate(); }
        public void Exit() { if (exiting) return; exiting = true; Dispose(); Window.Close(); Application.Current.Shutdown(); }
        public void Dispose() { if (timer != null) timer.Stop(); if (observer != null) { observer.Dispose(); observer = null; } if (renderer != null) { renderer.Dispose(); renderer = null; } if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; } }
    }
}

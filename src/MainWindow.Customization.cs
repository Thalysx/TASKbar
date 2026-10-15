using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TaskBAR {
    internal sealed partial class MainWindowController {
        static readonly string[] Effects = { "Padrão do Windows", "Opaco", "Transparente", "Desfoque", "Acrílico" };
        static readonly string[] EffectKeys = { "normal", "opaque", "clear", "blur", "acrylic" };
        static readonly string[] RuleKinds = { "process_name", "window_class", "window_title" };
        void SetupCustomization() {
            var startupToggle=new CheckBox { Content="Iniciar com o Windows e aplicar os perfis",Margin=new Thickness(0,0,0,18),ToolTip="Inicia pela bandeja quando você entra no Windows." };
            bool updatingStartup=true;
            try { startupToggle.IsChecked=WindowsStartup.Enabled; } catch { startupToggle.IsEnabled=false; }
            ((StackPanel)Get<StackPanel>("AboutPage")).Children.Insert(1,startupToggle);
            updatingStartup=false;
            RoutedEventHandler startupChanged=delegate {
                if (updatingStartup) return;
                try { WindowsStartup.SetEnabled(startupToggle.IsChecked == true); SetStatus(startupToggle.IsChecked == true ? "Inicialização com o Windows ativada" : "Inicialização com o Windows desativada",false); }
                catch(Exception ex) { updatingStartup=true; startupToggle.IsChecked=WindowsStartup.Enabled; updatingStartup=false; SetStatus("Não foi possível alterar a inicialização: " + ex.Message,true); }
            };
            startupToggle.Checked+=startupChanged; startupToggle.Unchecked+=startupChanged;
            var colorStack = (StackPanel)((Grid)Get<TextBox>("HexColor").Parent).Parent;
            var opacityHeading = new Grid { Margin=new Thickness(0,16,0,0) };
            opacityHeading.ColumnDefinitions.Add(new ColumnDefinition()); opacityHeading.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            opacityHeading.Children.Add(new TextBlock { Text="Opacidade",FontWeight=FontWeights.SemiBold });
            var percent = new TextBlock { Name="OpacityValue",Foreground=new SolidColorBrush(Color.FromRgb(114,228,210)) }; Grid.SetColumn(percent,1); opacityHeading.Children.Add(percent); Window.RegisterName(percent.Name,percent);
            var slider = new Slider { Name="OpacitySlider",Minimum=0,Maximum=100,TickFrequency=1,SmallChange=1,LargeChange=10,IsSnapToTickEnabled=true,Margin=new Thickness(0,10,0,0),Height=24 };
            Window.RegisterName(slider.Name,slider); colorStack.Children.Add(opacityHeading); colorStack.Children.Add(slider);
            slider.ValueChanged += delegate { if (loading || syncingOpacity) return; try { Get<TextBox>("HexColor").Text = Customization.WithOpacity(Get<TextBox>("HexColor").Text,slider.Value); } catch(Exception ex) { Get<TextBlock>("EditMessage").Text = ex.Message; } };
            var editorStack = (StackPanel)((ScrollViewer)Get<Grid>("EditorPage").Children[0]).Content;
            var preview = (Border)editorStack.Children[0]; preview.Padding=new Thickness(14); preview.Margin=new Thickness(0,0,0,14);
            var previewStack=(StackPanel)preview.Child; ((Grid)previewStack.Children[1]).Height=54; ((TextBlock)previewStack.Children[0]).Margin=new Thickness(0,0,0,8);
            var combo = new ComboBox { Name="PresetChoice",ItemsSource=Customization.Presets,SelectedIndex=0,Width=165,Margin=new Thickness(4,0,10,0),ToolTip="Preset para todos os perfis gerais. As regras por aplicativo são preservadas." }; Window.RegisterName(combo.Name,combo);
            var apply = new Button { Content="Aplicar preset",Margin=new Thickness(0),Padding=new Thickness(12,11,12,11) };
            var actions=(Panel)Get<Button>("SaveButton").Parent; actions.Children.Add(combo); actions.Children.Add(apply);
            apply.Click += delegate { if (!GuardJson() || (dirty && !SaveProfile())) return; try { Commit(Customization.Preset(settings,combo.SelectedIndex)); LoadProfile(); Get<TextBlock>("EditMessage").Text="Preset aplicado: " + Customization.Presets[combo.SelectedIndex]; Tick(true); } catch(Exception ex) { Get<TextBlock>("EditMessage").Text=ex.Message; } };
            var side = (StackPanel)Get<Button>("JsonButton").Parent;
            var rules = new Button { Content="Regras por aplicativo",Margin=new Thickness(0,18,0,0) }; side.Children.Insert(side.Children.IndexOf(Get<Button>("JsonButton")),rules);
            Get<Button>("JsonButton").Content="JSON e importação"; Get<Button>("JsonButton").Margin=new Thickness(0,12,0,0);
            var import = Get<Button>("ImportButton"); side.Children.Remove(import); import.Margin=new Thickness(0,0,10,0); import.Padding=new Thickness(12,10,12,10);
            ((Panel)Get<Button>("ExportButton").Parent).Children.Add(import);
            var discard = new Button { Content="Descartar edição",Margin=new Thickness(0,10,0,0),HorizontalAlignment=HorizontalAlignment.Left }; ((StackPanel)((Panel)Get<Button>("ExportButton").Parent).Parent).Children.Add(discard);
            discard.Click += delegate { LoadJson(); Get<TextBlock>("JsonMessage").Text="Editor recarregado com a configuração atual."; };
            rules.Click += delegate { if (!GuardJson() || (dirty && !SaveProfile())) return; RefreshRules(); ResetRule(); ShowPage("rules"); };
            Get<ComboBox>("RuleScope").ItemsSource = new [] { "Janela visível", "Janela maximizada" }; Get<ComboBox>("RuleScope").SelectedIndex=1;
            Get<ComboBox>("RuleKind").ItemsSource = new [] { "Processo (.exe)", "Classe da janela", "Trecho do título" }; Get<ComboBox>("RuleKind").SelectedIndex=0;
            Get<ComboBox>("RuleEffect").ItemsSource=Effects;
            Get<ComboBox>("InactiveEffect").ItemsSource=new [] { "Mesma aparência", "Padrão do Windows", "Opaco", "Transparente", "Desfoque", "Acrílico" };
            Get<ComboBox>("InactiveEffect").SelectionChanged += delegate { Get<TextBox>("InactiveColor").IsEnabled=Get<ComboBox>("InactiveEffect").SelectedIndex > 0; };
            Get<ListBox>("RuleList").SelectionChanged += delegate {
                editingRule = Get<ListBox>("RuleList").SelectedItem as RuleLocation; if (editingRule == null) return;
                var r = editingRule.Rule; Get<ComboBox>("RuleScope").SelectedIndex=editingRule.Scope == Settings.Keys[1] ? 0 : 1;
                Get<ComboBox>("RuleKind").SelectedIndex=Array.IndexOf(RuleKinds,r.Kind); Get<TextBox>("RuleMatch").Text=r.Match;
                FillRule(r.Active,r.Inactive); Get<TextBlock>("RuleMessage").Text="Editando " + r.Match;
            };
            Click("NewRuleButton",delegate { ResetRule(); });
            Click("SaveRuleButton",delegate { SaveRule(); });
            Click("RemoveRuleButton",delegate {
                if (!GuardJson()) return; if (editingRule == null) { Get<TextBlock>("RuleMessage").Text="Selecione uma regra para remover."; return; }
                try { var candidate=Settings.Parse(settings.ToJson()); var location=editingRule; candidate.Rules[location.Scope].RemoveAll(r=>r.Kind == location.Rule.Kind && r.Match == location.Rule.Match); Commit(candidate); RefreshRules(); ResetRule(); Get<TextBlock>("RuleMessage").Text="Regra removida."; Tick(true); } catch(Exception ex) { Get<TextBlock>("RuleMessage").Text=ex.Message; }
            });
            ResetRule();
        }
        bool GuardJson() {
            if (!jsonDirty) return true;
            ShowPage("json"); Get<TextBlock>("JsonMessage").Text="Há alterações no JSON. Valide e salve, ou descarte a edição antes de modificar outros perfis."; return false;
        }
        void LoadJson() { bool previous=loading; loading=true; Get<TextBox>("JsonEditor").Text=settings.ToJson(); jsonDirty=false; loading=previous; }
        void RefreshRules() {
            var items = new List<RuleLocation>(); foreach(var pair in settings.Rules) foreach(var rule in pair.Value) items.Add(new RuleLocation { Scope=pair.Key,Rule=rule });
            Get<ListBox>("RuleList").ItemsSource=items; editingRule=null;
        }
        void FillRule(Appearance p, Appearance inactive) {
            Get<ComboBox>("RuleEffect").SelectedIndex=p.AccentValue; Get<TextBox>("RuleColor").Text=p.Color; Get<TextBox>("RuleRadius").Text=p.BlurRadius.ToString(CultureInfo.InvariantCulture);
            Get<CheckBox>("RuleLine").IsChecked=p.ShowLine; Get<CheckBox>("RulePeek").IsChecked=p.ShowPeek;
            Get<ComboBox>("InactiveEffect").SelectedIndex=inactive == null ? 0 : inactive.AccentValue+1; Get<TextBox>("InactiveColor").Text=inactive == null ? p.Color : inactive.Color;
        }
        void ResetRule() { Get<ListBox>("RuleList").SelectedIndex=-1; editingRule=null; Get<TextBox>("RuleMatch").Text=""; FillRule(new Appearance(),null); Get<TextBlock>("RuleMessage").Text=""; }
        void SaveRule() {
            if (!GuardJson()) return;
            try {
                var candidate=Settings.Parse(settings.ToJson());
                int scope=Get<ComboBox>("RuleScope").SelectedIndex; int kind=Get<ComboBox>("RuleKind").SelectedIndex;
                float radius; if (!float.TryParse(Get<TextBox>("RuleRadius").Text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out radius)) throw new FormatException("Raio de desfoque inválido.");
                var p=new Appearance { Accent=EffectKeys[Get<ComboBox>("RuleEffect").SelectedIndex],Color=Get<TextBox>("RuleColor").Text,BlurRadius=radius,ShowLine=Get<CheckBox>("RuleLine").IsChecked == true,ShowPeek=Get<CheckBox>("RulePeek").IsChecked == true };
                Appearance inactive=null; int inactiveIndex=Get<ComboBox>("InactiveEffect").SelectedIndex;
                if (inactiveIndex>0) { inactive=editingRule != null && editingRule.Rule.Inactive != null ? editingRule.Rule.Inactive.Copy() : p.Copy(); inactive.Accent=EffectKeys[inactiveIndex-1]; inactive.Color=Get<TextBox>("InactiveColor").Text; }
                if (editingRule != null) candidate.Rules[editingRule.Scope].RemoveAll(r=>r.Kind == editingRule.Rule.Kind && r.Match == editingRule.Rule.Match);
                Customization.UpsertRule(candidate,Settings.Keys[scope+1],RuleKinds[kind],Get<TextBox>("RuleMatch").Text,p,inactive);
                Commit(candidate); RefreshRules(); Get<TextBlock>("RuleMessage").Text=settings.DisableSaving ? "Regra aplicada à sessão." : "Regra salva."; Tick(true);
            } catch(Exception ex) { Get<TextBlock>("RuleMessage").Text=ex.Message; }
        }
    }
}

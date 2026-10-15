using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TaskBAR {
    internal sealed class CompactMenuItem : ToolStripMenuItem {
        public bool Radio;
        public CompactMenuItem(string text) : base(text) { }
    }
    internal sealed class CompactTrayMenu : ContextMenuStrip {
        readonly Action<string,Action<Appearance>> changeProfile;
        readonly Action<int> preset;
        readonly Action<string> advanced;
        readonly Action toggle, startup, tips, exit;
        readonly List<Image> icons=new List<Image>();
        public CompactTrayMenu(Action<string,Action<Appearance>> changeProfile,Action<int> preset,Action<string> advanced,Action toggle,Action startup,Action tips,Action exit) {
            this.changeProfile=changeProfile; this.preset=preset; this.advanced=advanced; this.toggle=toggle; this.startup=startup; this.tips=tips; this.exit=exit;
            Font=new Font("Segoe UI",9.5f); ForeColor=Color.FromArgb(244,244,244); BackColor=Color.FromArgb(42,42,42);
            Renderer=new DarkMenuRenderer(); ShowImageMargin=true; ShowCheckMargin=false; Padding=new Padding(3,5,3,5);
            for(int i=0;i<10;i++) icons.Add(CreateIcon(i));
        }
        CompactMenuItem Item(string text, Action action, int icon=-1) {
            var item=new CompactMenuItem(text) { ForeColor=ForeColor,Padding=new Padding(8,7,8,7) };
            if(icon>=0) item.Image=icons[icon]; if(action!=null) item.Click+=delegate { action(); }; return item;
        }
        void ConfigureDropDown(ToolStripMenuItem item) { item.DropDown.Renderer=Renderer; item.DropDown.Font=Font; item.DropDown.ForeColor=ForeColor; item.DropDown.BackColor=BackColor; }
        public void Refresh(Settings settings,bool wanted,bool startsWithWindows,bool editorOpen,bool xaml) {
            while(Items.Count>0) { ToolStripItem old=Items[0]; Items.RemoveAt(0); old.Dispose(); }
            for(int index=0;index<6;index++) Items.Add(Profile(settings,index,editorOpen,xaml));
            Items.Add(new ToolStripSeparator());
            var extra=Item("Avançado",null,6); ConfigureDropDown(extra); Items.Add(extra);
            extra.DropDownItems.Add(Profile(settings,6,editorOpen,xaml));
            var presets=Item("Presets",null); ConfigureDropDown(presets); extra.DropDownItems.Add(presets);
            for(int i=0;i<Customization.Presets.Length;i++) { int selected=i; presets.DropDownItems.Add(Item(Customization.Presets[i],delegate { preset(selected); })); }
            presets.Enabled=!editorOpen;
            extra.DropDownItems.Add(new ToolStripSeparator());
            extra.DropDownItems.Add(Item("Regras por aplicativo…",delegate { advanced("rules"); }));
            extra.DropDownItems.Add(Item("Configuração completa…",delegate { advanced("editor"); }));
            extra.DropDownItems.Add(Item("Editar JSON…",delegate { advanced("json"); }));
            extra.DropDownItems.Add(new ToolStripSeparator());
            var paused=Item(wanted ? "Pausar e restaurar Windows" : "Ativar efeitos",toggle); extra.DropDownItems.Add(paused);
            var boot=Item("Iniciar com o Windows",startup); boot.Checked=startsWithWindows; Items.Add(boot);
            Items.Add(new ToolStripSeparator()); Items.Add(Item("Dicas e atalhos",tips,7)); Items.Add(Item("Sair",exit,8));
            PerformLayout();
        }
        ToolStripMenuItem Profile(Settings settings,int index,bool editorOpen,bool xaml) {
            string key=Settings.Keys[index]; Appearance p=settings.Profiles[key];
            var root=Item(Settings.Names[index],null,index<6?index:9); root.Enabled=!editorOpen;
            if(editorOpen) root.ToolTipText="Feche a configuração avançada para ajustar este perfil pelo menu.";
            ConfigureDropDown(root);
            if(index!=0) { var enabled=Item("Usar este perfil",delegate { changeProfile(key,delegate(Appearance a) { a.Enabled=!a.Enabled; }); }); enabled.Checked=p.Enabled; root.DropDownItems.Add(enabled); root.DropDownItems.Add(new ToolStripSeparator()); }
            string[] names={ "Padrão do Windows", "Opaco", "Transparente", "Desfoque", "Acrílico" };
            string[] accents={ "normal", "opaque", "clear", "blur", "acrylic" };
            for(int i=0;i<accents.Length;i++) { string accent=accents[i]; var effect=Item(names[i],delegate { changeProfile(key,delegate(Appearance a) { a.Accent=accent; }); }); effect.Checked=p.Accent==accent; effect.Radio=true; effect.Tag=accent; root.DropDownItems.Add(effect); }
            root.DropDownItems.Add(new ToolStripSeparator()); root.DropDownItems.Add(Item("Cor…",delegate { advanced("color:"+key); }));
            var opacity=Item("Opacidade",null); ConfigureDropDown(opacity); opacity.Enabled=p.Accent!="normal" && p.Accent!="opaque";
            int percentage=(int)Math.Round((p.Abgr >> 24)/255.0*100);
            foreach(int value in new [] {0,10,25,50,75,100}) { int amount=value; var choice=Item(value+"%",delegate { changeProfile(key,delegate(Appearance a) { a.Color=Customization.WithOpacity(a.Color,amount); }); }); choice.Checked=percentage==value; choice.Radio=true; opacity.DropDownItems.Add(choice); }
            root.DropDownItems.Add(opacity);
            var peek=Item("Mostrar botão do desktop",delegate { changeProfile(key,delegate(Appearance a) { a.ShowPeek=!a.ShowPeek; }); }); peek.Checked=p.ShowPeek; peek.Enabled=!xaml; root.DropDownItems.Add(peek);
            var line=Item("Mostrar linha",delegate { changeProfile(key,delegate(Appearance a) { a.ShowLine=!a.ShowLine; }); }); line.Checked=p.ShowLine; line.Enabled=xaml; root.DropDownItems.Add(line);
            var radius=Item("Raio de desfoque",null); ConfigureDropDown(radius); radius.Enabled=xaml && p.Accent=="blur";
            foreach(int value in new [] {3,9,18,30,60,90}) { int amount=value; var choice=Item(value.ToString(),delegate { changeProfile(key,delegate(Appearance a) { a.BlurRadius=amount; }); }); choice.Checked=Math.Abs(p.BlurRadius-value)<0.01; radius.DropDownItems.Add(choice); }
            root.DropDownItems.Add(radius); return root;
        }
        internal void SavePreview(string path,int profile=-1) {
            ToolStrip strip=this;
            if(profile>=0) strip=(ToolStrip)((ToolStripMenuItem)Items[profile]).DropDown;
            strip.Size=strip.GetPreferredSize(Size.Empty); strip.CreateControl(); strip.PerformLayout();
            using(var bitmap=new Bitmap(Math.Max(1,strip.Width),Math.Max(1,strip.Height))) { strip.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png); }
        }
        static Image CreateIcon(int index) {
            var image=new Bitmap(18,18); using(var g=Graphics.FromImage(image)) using(var pen=new Pen(Color.FromArgb(237,237,237),1.15f)) using(var brush=new SolidBrush(Color.FromArgb(237,237,237))) {
                g.SmoothingMode=SmoothingMode.AntiAlias;
                if(index==0) { g.DrawRectangle(pen,2,4,14,10); g.DrawLine(pen,6,15,12,15); }
                else if(index==1) { g.DrawRectangle(pen,2,3,14,12); g.DrawLine(pen,2,6,16,6); }
                else if(index==2) { g.DrawLine(pen,3,14,15,2); g.DrawLine(pen,10,2,15,2); g.DrawLine(pen,15,2,15,7); g.DrawLine(pen,3,9,3,14); g.DrawLine(pen,3,14,8,14); }
                else if(index==3) { g.FillRectangle(brush,2,3,6,5); g.FillRectangle(brush,10,3,6,5); g.FillRectangle(brush,2,10,6,5); g.FillRectangle(brush,10,10,6,5); }
                else if(index==4) { g.DrawEllipse(pen,6,2,9,9); g.DrawLine(pen,7,10,2,15); }
                else if(index==5) { g.DrawRectangle(pen,3,3,12,10); g.DrawLine(pen,1,3,1,16); g.DrawLine(pen,17,3,17,16); g.DrawLine(pen,3,7,15,7); }
                else if(index==6) { g.DrawEllipse(pen,3,3,12,12); g.DrawEllipse(pen,6,6,6,6); for(int i=0;i<8;i++) { double a=i*Math.PI/4; g.DrawLine(pen,9+(float)Math.Cos(a)*6,9+(float)Math.Sin(a)*6,9+(float)Math.Cos(a)*8,9+(float)Math.Sin(a)*8); } }
                else if(index==7) { g.DrawEllipse(pen,5,2,8,9); g.DrawLine(pen,7,11,7,15); g.DrawLine(pen,11,11,11,15); g.DrawLine(pen,7,15,11,15); }
                else if(index==8) { g.DrawArc(pen,3,3,12,12,310,280); g.DrawLine(pen,9,1,9,9); }
                else { g.DrawRectangle(pen,2,5,13,8); g.FillRectangle(brush,15,7,2,4); g.DrawLine(pen,5,9,11,9); }
            } return image;
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if(disposing) foreach(var image in icons) image.Dispose(); }
    }
    internal sealed class DarkMenuColors : ProfessionalColorTable {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(42,42,42); } }
        public override Color ImageMarginGradientBegin { get { return ToolStripDropDownBackground; } }
        public override Color ImageMarginGradientMiddle { get { return ToolStripDropDownBackground; } }
        public override Color ImageMarginGradientEnd { get { return ToolStripDropDownBackground; } }
        public override Color MenuItemSelected { get { return Color.FromArgb(62,62,62); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(62,62,62); } }
        public override Color MenuBorder { get { return Color.FromArgb(72,72,72); } }
        public override Color SeparatorDark { get { return Color.FromArgb(66,66,66); } }
        public override Color SeparatorLight { get { return SeparatorDark; } }
        public override Color CheckBackground { get { return ToolStripDropDownBackground; } }
        public override Color CheckSelectedBackground { get { return MenuItemSelected; } }
        public override Color CheckPressedBackground { get { return MenuItemSelected; } }
    }
    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer {
        public DarkMenuRenderer() : base(new DarkMenuColors()) { RoundedEdges=false; }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor=Color.FromArgb(205,205,205); base.OnRenderArrow(e); }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
            Rectangle r=e.ImageRectangle; var item=e.Item as CompactMenuItem;
            using(var pen=new Pen(Color.FromArgb(124,231,213),1.6f)) using(var brush=new SolidBrush(pen.Color)) {
                e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                if(item!=null && item.Radio) e.Graphics.FillEllipse(brush,r.Left+5,r.Top+5,6,6);
                else { e.Graphics.DrawLine(pen,r.Left+3,r.Top+8,r.Left+7,r.Top+12); e.Graphics.DrawLine(pen,r.Left+7,r.Top+12,r.Left+14,r.Top+4); }
            }
        }
    }
}

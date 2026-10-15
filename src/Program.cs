using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TaskBAR {
    internal static class Program {
        [STAThread] static int Main(string[] args) {
            try {
                if (args.Contains("--self-test")) return SelfTests.Run(args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt"));
                if (args.Contains("--write-default")) { new Settings().Save(args[1]); return 0; }
                if (args.Contains("--diagnose")) {
                    using(var observer = new WindowObserver(delegate {})) {
                        var d = observer.Capture();
                        var data = new Dictionary<string,object> { { "taskbars", Native.Taskbars().Count }, { "backends", Native.Taskbars().Select(bar => Native.TaskbarKind(bar)).ToArray() }, { "conflict", Native.Conflict() }, { "eligible_windows", d.Windows.Count }, { "battery_saver", d.BatterySaver }, { "start", d.Start }, { "search", d.Search }, { "task_view", d.TaskView }, { "bitness", IntPtr.Size * 8 } };
                        data["taskbar_details"] = Native.Taskbars().Select(bar => Native.DescribeTaskbar(bar)).ToArray();
                        File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(data));
                    }
                    return 0;
                }
                if (args.Contains("--benchmark")) {
                    using(var observer=new WindowObserver(delegate {})) {
                        int before=Native.ProcessQueries; var watch=System.Diagnostics.Stopwatch.StartNew(); int windows=0;
                        for(int i=0;i<30;i++) windows=observer.Capture().Windows.Count;
                        watch.Stop();
                        var data=new Dictionary<string,object> { { "captures",30 }, { "elapsed_ms",watch.Elapsed.TotalMilliseconds }, { "process_queries",Native.ProcessQueries-before }, { "windows_last_capture",windows }, { "idle_scan_seconds_hidden",5 }, { "fallback_scan_seconds_active",2 } };
                        File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(data));
                    }
                    return 0;
                }
                if(args.Contains("--tray-preview")) {
                    System.Windows.Forms.Application.EnableVisualStyles(); System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                    using(var menu=new CompactTrayMenu(delegate {},delegate {},delegate {},delegate {},delegate {},delegate {},delegate {})) {
                        menu.Refresh(new Settings(),true,true,false,false); menu.SavePreview(args[1],args.Length>2 ? 0 : -1);
                    }
                    return 0;
                }
                if(args.Contains("--tray-smoke")) {
                    System.Windows.Forms.Application.EnableVisualStyles(); System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                    string folder=Path.GetDirectoryName(Path.GetFullPath(args[1]));
                    using(var controller=new TrayController(Path.Combine(folder,"tray-smoke-data"),false)) controller.CheckMenu(args[1],Path.Combine(folder,"preview.png"),Path.Combine(folder,"profile-preview.png"));
                    return 0;
                }
                if(!args.Any(a=>a=="--settings" || a=="--settings-smoke" || a=="--native-smoke" || a=="--ui-smoke" || a=="--startup-smoke" || a=="--render-preview")) {
                    System.Windows.Forms.Application.EnableVisualStyles(); System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                    bool owned;
                    using(var mutex=new Mutex(true,"Local\\TaskBAR.ProfileController.v1",out owned)) {
                        if(!owned) return 0;
                        using(var controller=new TrayController()) System.Windows.Forms.Application.Run(controller);
                    }
                    return 0;
                }
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                if(args.Contains("--settings-smoke")) {
                    string folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])),"settings-smoke-data");
                    using(var controller=new MainWindowController(false,folder,true)) controller.CheckEditorMode(args[1]); return 0;
                }
                if(args.Contains("--settings")) {
                    bool owned;
                    using(var mutex=new Mutex(true,"Local\\TaskBAR.Settings.v1",out owned)) {
                        if(!owned) return 0;
                        using(var controller=new MainWindowController(false,null,true)) {
                            controller.SettingsPage(args.Length>1 ? args[1] : "editor"); app.ShutdownMode=ShutdownMode.OnMainWindowClose; app.Run(controller.Window);
                        }
                    }
                    return 0;
                }
                if (args.Contains("--native-smoke")) {
                    var window = new Window { Title = "TaskBAR native verification" };
                    IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle(); var results = new List<string>();
                    try { foreach(string effect in new [] { "clear", "opaque", "blur", "acrylic", "normal" }) { Renderer.SetLegacy(hwnd,new Appearance { Accent=effect,Color="#18233840" }); results.Add("PASS legacy policy ABI: " + effect); } }
                    finally { Renderer.SetLegacy(hwnd,new Appearance { Accent="normal" }); window.Close(); }
                    File.WriteAllLines(args[1],results); return 0;
                }
                if (args.Contains("--ui-smoke")) { string storage=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])),"ui-smoke-data"); using(var c = new MainWindowController(false,storage)) c.CheckStartup(args[1]); return 0; }
                if (args.Contains("--startup-smoke")) { string storage=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])),"startup-smoke-data"); using(var c = new MainWindowController(false,storage)) c.CheckAutomaticStartup(args[1]); return 0; }
                if (args.Contains("--render-preview")) {
                    using(var c = new MainWindowController(true)) {
                        if (args.Length > 2) c.PreviewPage(args[2]);
                        var root = (FrameworkElement)c.Window.Content; root.Measure(new Size(1080,750)); root.Arrange(new Rect(0,0,1080,750)); root.UpdateLayout();
                        var bitmap = new RenderTargetBitmap(1080,750,96,96,PixelFormats.Pbgra32); bitmap.Render(root);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using(var file = File.Create(args[1])) encoder.Save(file);
                    }
                    return 0;
                }
                bool acquired;
                using(var mutex = new Mutex(true,"Local\\TaskBAR.ProfileController.v1",out acquired)) {
                    if (!acquired) { if (!args.Contains("--startup")) MessageBox.Show("O TaskBAR já está aberto. Use o ícone na bandeja para acessar a janela.","TaskBAR"); return 0; }
                    var controller = new MainWindowController(false);
                    app.DispatcherUnhandledException += delegate(object sender,System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) { controller.Pause(); MessageBox.Show(e.Exception.Message,"TaskBAR · operação interrompida"); e.Handled = true; };
                    try { if (args.Contains("--startup")) { controller.PrepareAutomaticStartup(); app.Run(); } else app.Run(controller.Window); } finally { controller.Dispose(); }
                }
                return 0;
            } catch(Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"startup-error.txt"),ex.ToString()); return 1; }
        }
    }
}

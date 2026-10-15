using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace TaskBAR {
    internal static class WindowsStartup {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string EntryName = "TaskBAR";
        public static string CommandFor(string executable) {
            if (string.IsNullOrWhiteSpace(executable) || executable.IndexOf('"') >= 0) throw new ArgumentException("Caminho de aplicativo inválido.");
            return "\"" + Path.GetFullPath(executable) + "\" --startup";
        }
        static string CurrentCommand { get { return CommandFor(Assembly.GetExecutingAssembly().Location); } }
        public static bool Enabled {
            get { using(var key=Registry.CurrentUser.OpenSubKey(Key)) return key != null && string.Equals(key.GetValue(EntryName) as string,CurrentCommand,StringComparison.OrdinalIgnoreCase); }
        }
        public static void SetEnabled(bool enabled) {
            using(var key=Registry.CurrentUser.CreateSubKey(Key)) {
                if (enabled) key.SetValue(EntryName,CurrentCommand,RegistryValueKind.String);
                else if (string.Equals(key.GetValue(EntryName) as string,CurrentCommand,StringComparison.OrdinalIgnoreCase)) key.DeleteValue(EntryName,false);
            }
        }
    }
}

// metla.txt. Startup.cs — что запускается вместе с Windows и установленные программы.
// Автозапуск: запись никогда не удаляется. Переключатель делает то же, что Диспетчер задач, — пишет отметку
// «включено/отключено» в HKCU\…\Explorer\StartupApproved; вернуть можно и здесь, и в Диспетчере задач.
// Программы: metla.txt сама ничего не удаляет — только запускает собственную программу удаления (UninstallString).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace DevTxt.Metla
{
    // Отметка Диспетчера задач: 12 байт. Первый байт чётный (02, 06) — включено, нечётный (03, 07) — отключено;
    // у отключённой записи в байтах 4–11 — время отключения (FILETIME).
    static class Approved
    {
        public static byte[] Encode(bool enabled, DateTime whenUtc)
        {
            var b = new byte[12];
            b[0] = enabled ? (byte)2 : (byte)3;
            if (!enabled) BitConverter.GetBytes(whenUtc.ToFileTimeUtc()).CopyTo(b, 4);
            return b;
        }

        // Отметки нет или она непонятная — запись считается включённой: так же поступает Windows
        public static bool Decode(byte[] b)
        {
            return b == null || b.Length == 0 || (b[0] & 1) == 0;
        }

        public static DateTime? DisabledAt(byte[] b)
        {
            if (b == null || b.Length < 12 || Decode(b)) return null;
            try { long t = BitConverter.ToInt64(b, 4); return t <= 0 ? (DateTime?)null : DateTime.FromFileTimeUtc(t); }
            catch { return null; }
        }
    }

    class StartupEntry
    {
        public string Name, Command, Exe, Publisher;
        public bool Enabled = true;
        public bool Editable;         // запись этого пользователя — её можно отключить
        public bool Folder;           // из папки «Автозагрузка» (иначе — из реестра)
        public string ApprovedName;   // имя значения в StartupApproved
    }

    static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        // Путь к программе из строки запуска: в кавычках, до «.exe» или до первого пробела
        public static string ExeOf(string command)
        {
            string exe, args;
            Programs.Split(command, out exe, out args);
            return exe;
        }

        static bool ReadApproved(RegistryKey hive, string sub, string name)
        {
            try
            {
                using (RegistryKey k = hive.OpenSubKey(ApprovedKey + sub))
                    return Approved.Decode(k == null ? null : k.GetValue(name) as byte[]);
            }
            catch { return true; }
        }

        static void Describe(StartupEntry e)
        {
            try
            {
                if (string.IsNullOrEmpty(e.Exe) || !File.Exists(e.Exe)) return;
                FileVersionInfo v = FileVersionInfo.GetVersionInfo(e.Exe);
                if (!string.IsNullOrEmpty(v.CompanyName)) e.Publisher = v.CompanyName.Trim();
            }
            catch { }
        }

        static void FromRun(List<StartupEntry> list, RegistryKey hive, RegistryView view, bool editable, string approvedSub)
        {
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(hive == Registry.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, view))
                using (RegistryKey k = root.OpenSubKey(RunKey))
                {
                    if (k == null) return;
                    foreach (string name in k.GetValueNames())
                    {
                        string command = Convert.ToString(k.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames));
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(command)) continue;
                        var e = new StartupEntry { Name = name, Command = command, Editable = editable, ApprovedName = name };
                        e.Exe = ExeOf(Environment.ExpandEnvironmentVariables(command));
                        using (RegistryKey approvedRoot = RegistryKey.OpenBaseKey(hive == Registry.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Default))
                            e.Enabled = ReadApproved(approvedRoot, approvedSub, name);
                        Describe(e);
                        list.Add(e);
                    }
                }
            }
            catch { }
        }

        // Куда ведёт ярлык. Только чтение.
        static string LinkTarget(string lnk)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return null;
                object shell = Activator.CreateInstance(t);
                object link = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                return link.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, link, null) as string;
            }
            catch { return null; }
        }

        static void FromFolder(List<StartupEntry> list, string dir, RegistryKey approvedHive, bool editable)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                foreach (string file in Directory.GetFiles(dir))
                {
                    string name = Path.GetFileName(file);
                    if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    var e = new StartupEntry { Name = Path.GetFileNameWithoutExtension(file), Command = file, Editable = editable, Folder = true, ApprovedName = name };
                    e.Exe = name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? LinkTarget(file) : file;
                    e.Enabled = ReadApproved(approvedHive, "StartupFolder", name);
                    Describe(e);
                    list.Add(e);
                }
            }
            catch { }
        }

        // Только чтение реестра и папок
        public static List<StartupEntry> Read()
        {
            var list = new List<StartupEntry>();
            FromRun(list, Registry.CurrentUser, RegistryView.Default, true, "Run");
            FromFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), Registry.CurrentUser, true);
            FromRun(list, Registry.LocalMachine, RegistryView.Registry64, false, "Run");
            FromRun(list, Registry.LocalMachine, RegistryView.Registry32, false, "Run32");
            FromFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Registry.LocalMachine, false);
            return list;
        }

        // Единственное место, где программа пишет в реестр: отметка для записи этого пользователя.
        // Сама запись автозапуска остаётся на месте.
        public static void Set(StartupEntry e, bool on)
        {
            if (!e.Editable) throw new InvalidOperationException("общие записи не меняются");
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ApprovedKey + (e.Folder ? "StartupFolder" : "Run")))
                k.SetValue(e.ApprovedName, Approved.Encode(on, DateTime.UtcNow), RegistryValueKind.Binary);
            e.Enabled = on;
        }
    }

    class Prog
    {
        public string Name, Publisher, Version, Uninstall;
        public long Bytes = -1;          // −1 — размер неизвестен
        public DateTime? Installed;
    }

    static class Programs
    {
        const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        static string Str(Dictionary<string, object> v, string key)
        {
            object o;
            if (!v.TryGetValue(key, out o) || o == null) return null;
            string s = Convert.ToString(o, CultureInfo.InvariantCulture).Trim();
            return s.Length == 0 ? null : s;
        }

        static long Num(Dictionary<string, object> v, string key)
        {
            object o;
            if (!v.TryGetValue(key, out o) || o == null) return 0;
            try { return Convert.ToInt64(o, CultureInfo.InvariantCulture); } catch { return 0; }
        }

        // Запись реестра → программа; null — показывать не нужно: системная часть, обновление или нет своей программы удаления
        public static Prog FromValues(Dictionary<string, object> v)
        {
            string name = Str(v, "DisplayName"), uninstall = Str(v, "UninstallString");
            if (name == null || uninstall == null) return null;
            if (Num(v, "SystemComponent") == 1) return null;
            if (Str(v, "ParentKeyName") != null) return null;
            string release = Str(v, "ReleaseType") ?? "";
            if (release == "Update" || release == "Hotfix" || release == "Security Update" || release == "Update Rollup" || release == "Service Pack") return null;

            var p = new Prog { Name = name, Publisher = Str(v, "Publisher"), Version = Str(v, "DisplayVersion"), Uninstall = uninstall };
            // Программы на установщике Windows удаляет он сам: «/I{код}» открыл бы «изменить», поэтому, как и «Параметры», просим «/X»
            if (Num(v, "WindowsInstaller") == 1)
            {
                int at = uninstall.IndexOf("/I{", StringComparison.OrdinalIgnoreCase);
                if (at >= 0) p.Uninstall = uninstall.Substring(0, at) + "/X" + uninstall.Substring(at + 2);
            }
            long kb = Num(v, "EstimatedSize");
            if (kb > 0) p.Bytes = kb * 1024;
            string date = Str(v, "InstallDate");
            DateTime d;
            if (date != null && DateTime.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) p.Installed = d;
            return p;
        }

        // Строка запуска → программа и её параметры
        public static void Split(string command, out string exe, out string args)
        {
            exe = null; args = "";
            if (command == null) return;
            string c = command.Trim();
            if (c.Length == 0) return;
            if (c[0] == '"')
            {
                int end = c.IndexOf('"', 1);
                if (end < 0) { exe = c.Substring(1); return; }
                exe = c.Substring(1, end - 1);
                args = c.Substring(end + 1).Trim();
                return;
            }
            int dot = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            while (dot >= 0 && dot + 4 < c.Length && c[dot + 4] != ' ') dot = c.IndexOf(".exe", dot + 4, StringComparison.OrdinalIgnoreCase);
            if (dot >= 0) { exe = c.Substring(0, dot + 4); args = c.Substring(dot + 4).Trim(); return; }
            int space = c.IndexOf(' ');
            if (space < 0) { exe = c; return; }
            exe = c.Substring(0, space);
            args = c.Substring(space + 1).Trim();
        }

        static void FromKey(List<Prog> list, HashSet<string> seen, RegistryHive hive, RegistryView view)
        {
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey k = root.OpenSubKey(UninstallKey))
                {
                    if (k == null) return;
                    foreach (string sub in k.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey e = k.OpenSubKey(sub))
                            {
                                if (e == null) continue;
                                var v = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                                foreach (string name in e.GetValueNames()) v[name] = e.GetValue(name);
                                Prog p = FromValues(v);
                                if (p != null && seen.Add(p.Name + "|" + p.Version)) list.Add(p);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        // Только чтение реестра
        public static List<Prog> Read()
        {
            var list = new List<Prog>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            FromKey(list, seen, RegistryHive.LocalMachine, RegistryView.Registry64);
            FromKey(list, seen, RegistryHive.LocalMachine, RegistryView.Registry32);
            FromKey(list, seen, RegistryHive.CurrentUser, RegistryView.Default);
            return list;
        }

        public static List<Prog> View(List<Prog> all, string search, bool byDate)
        {
            var list = new List<Prog>();
            string q = (search ?? "").Trim();
            foreach (Prog p in all)
                if (q.Length == 0 || p.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 || (p.Publisher != null && p.Publisher.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)) list.Add(p);
            list.Sort(delegate(Prog a, Prog b)
            {
                int c = byDate ? (b.Installed ?? DateTime.MinValue).CompareTo(a.Installed ?? DateTime.MinValue) : b.Bytes.CompareTo(a.Bytes);
                return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        // Запускает собственную программу удаления — после вопроса в окне. Нужны ли ей права, решает она сама и Windows.
        public static void Launch(Prog p)
        {
            string exe, args;
            Split(p.Uninstall, out exe, out args);
            if (string.IsNullOrEmpty(exe)) throw new Exception("не удалось разобрать строку удаления");
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
        }
    }
}

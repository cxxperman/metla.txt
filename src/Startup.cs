// metla.txt. Startup.cs — что запускается вместе с Windows и установленные программы.
// Автозапуск: запись никогда не удаляется. Переключатель делает то же, что Диспетчер задач, — пишет отметку
// «включено/отключено» в HKCU\…\Explorer\StartupApproved; вернуть можно и здесь, и в Диспетчере задач.
// Программы: metla.txt сама ничего не удаляет — только запускает собственную программу удаления (UninstallString).
// Приложения dev.txt™ (DevApps) ставит, удаляет и включает в автозапуск core.txt: когда он установлен, metla.txt их не трогает.
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
        public string Description;    // FileDescription из файла программы — понятнее, чем имя значения в реестре
        public System.Windows.Media.ImageSource Icon;   // значок из файла; достаётся в фоне вместе со списком
        public bool Enabled = true;
        public bool Editable;         // запись этого пользователя — её можно отключить
        public bool Folder;           // из папки «Автозагрузка» (иначе — из реестра)
        public string ApprovedName;   // имя значения в StartupApproved
        public bool Family;           // приложение dev.txt: его автозапуском ведает core.txt
        public bool Missing;          // файла программы нет на месте: запись осталась от удалённой или переехавшей программы
    }

    // Приложения dev.txt™. Их ставит, удаляет и запускает вместе с Windows core.txt — metla.txt этого не делает,
    // а отправляет в core.txt (kit.txt, hub/PROTOCOL.md, «Кто за что отвечает»). Здесь только разбор строк: диск не трогается.
    static class DevApps
    {
        // Имена файлов приложений семьи — как в каталоге core.txt и в Family.cs
        static readonly string[] Names = { "Core", "Kapsula", "Aura", "Dock", "Smena", "Metla" };

        static string LocalAppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }

        // Общий дом: сюда core.txt ставит приложения, здесь лежит и он сам
        public static string Home { get { return Path.Combine(LocalAppData, "Programs", "dev.txt"); } }

        // aura.txt ставится своим установщиком — в эту папку (так записано в каталоге core.txt)
        public static string AuraHome { get { return Path.Combine(LocalAppData, "Programs", "aura-voice"); } }

        // Полный путь без кавычек и хвостового разделителя; null — это не путь
        static string Full(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                string p = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"').Trim());
                if (p.Length == 0 || p.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathRooted(p)) return null;
                return Path.GetFullPath(p).TrimEnd('\\', '/');
            }
            catch { return null; }
        }

        // path — сама папка root или что-то внутри неё
        public static bool Within(string path, string root)
        {
            string p = Full(path), r = Full(root);
            if (p == null || r == null) return false;
            return string.Equals(p, r, StringComparison.OrdinalIgnoreCase) || p.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase);
        }

        // Имя приложения семьи по файлу программы («Kapsula» для …\Kapsula.exe); null — файл называется иначе
        static string NameOf(string exe)
        {
            string f = Full(exe);
            if (f == null) return null;
            string file = Path.GetFileName(f);
            foreach (string n in Names) if (string.Equals(file, n + ".exe", StringComparison.OrdinalIgnoreCase)) return n;
            return null;
        }

        // Имя продукта для строки списка: …\Kapsula.exe → «kapsula.txt»; null — файл называется иначе
        public static string Title(string exe)
        {
            string n = NameOf(exe);
            return n == null ? null : n.ToLowerInvariant() + ".txt";
        }

        // Запись автозапуска ведёт к приложению dev.txt: файл лежит в общем доме или называется как одно из них.
        // company — «Компания» из свойств файла (null — пусто или файл не прочитать). У приложений семьи она пустая,
        // «dev.txt» или имя самого приложения; чужая программа с тем же именем файла (скажем, Aura.exe другой фирмы) не в счёт.
        public static bool Owns(string exe, string company) { return OwnsIn(exe, company, Home); }

        public static bool OwnsIn(string exe, string company, string home)
        {
            if (Within(exe, home)) return true;
            string name = NameOf(exe);
            if (name == null) return false;
            string c = (company ?? "").Trim();
            return c.Length == 0 || string.Equals(c, "dev.txt", StringComparison.OrdinalIgnoreCase) || string.Equals(c, name, StringComparison.OrdinalIgnoreCase);
        }

        // Установленная программа — приложение dev.txt: раздел реестра dev.txt.<имя> (его пишет core.txt), издатель dev.txt
        // или её программа удаления, значок либо папка лежат в общем доме или в папке aura.txt
        public static bool OwnsProgram(string key, string publisher, string[] paths) { return OwnsProgramIn(key, publisher, paths, Home, AuraHome); }

        public static bool OwnsProgramIn(string key, string publisher, string[] paths, string home, string auraHome)
        {
            if (key != null && key.Length > 8 && key.StartsWith("dev.txt.", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals((publisher ?? "").Trim(), "dev.txt", StringComparison.OrdinalIgnoreCase)) return true;
            if (paths != null) foreach (string p in paths) if (Within(p, home) || Within(p, auraHome)) return true;
            return false;
        }

        // Имя продукта для установленного приложения dev.txt: по разделу dev.txt.<имя> или по папке aura.txt
        // (её установщик пишет своё имя с номером версии); null — не узнать, остаётся имя из реестра
        public static string ProgramTitle(string key, string[] paths) { return ProgramTitleIn(key, paths, AuraHome); }

        public static string ProgramTitleIn(string key, string[] paths, string auraHome)
        {
            if (key != null && key.StartsWith("dev.txt.", StringComparison.OrdinalIgnoreCase))
            {
                string id = key.Substring(8).Trim().ToLowerInvariant();
                if (System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z][a-z0-9-]{0,31}$")) return id + ".txt";
            }
            if (paths != null) foreach (string p in paths) if (Within(p, auraHome)) return "aura.txt";
            return null;
        }
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

        // Файла программы нет там, куда ведёт запись. Имя без папки («rundll32.exe …») Windows ищет сама, а путь без
        // расширения мог обрезаться на пробеле («C:\Program») — такие не в счёт, как и ярлык, который ведёт на папку.
        public static bool Missing(string exe)
        {
            try { return !string.IsNullOrEmpty(exe) && Path.IsPathRooted(exe) && Path.HasExtension(exe) && !File.Exists(exe) && !Directory.Exists(exe); }
            catch { return false; }
        }

        static void Describe(StartupEntry e)
        {
            try
            {
                e.Missing = Missing(e.Exe);
                if (string.IsNullOrEmpty(e.Exe) || !File.Exists(e.Exe)) return;
                FileVersionInfo v = FileVersionInfo.GetVersionInfo(e.Exe);
                if (!string.IsNullOrEmpty(v.CompanyName)) e.Publisher = v.CompanyName.Trim();
                if (!string.IsNullOrEmpty(v.FileDescription) && v.FileDescription.Trim().Length > 0) e.Description = v.FileDescription.Trim();
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
                        e.Family = DevApps.Owns(e.Exe, e.Publisher);
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
                    e.Family = DevApps.Owns(e.Exe, e.Publisher);
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
        public string IconPath;          // DisplayIcon из реестра: файл значка (без «,номер»); null — не указан
        public System.Windows.Media.ImageSource Icon;   // достаётся в фоне после списка
        public long Bytes = -1;          // −1 — размер неизвестен
        public DateTime? Installed;
        public bool Family;              // приложение dev.txt: удаляет его core.txt
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

        // Запись реестра → программа; null — показывать не нужно: системная часть, обновление или нет своей программы удаления.
        // key — имя раздела в реестре: по нему узнаются приложения dev.txt (dev.txt.<имя>)
        public static Prog FromValues(Dictionary<string, object> v) { return FromValues(v, null); }

        public static Prog FromValues(Dictionary<string, object> v, string key)
        {
            string name = Str(v, "DisplayName"), uninstall = Str(v, "UninstallString");
            if (name == null || uninstall == null) return null;
            if (Num(v, "SystemComponent") == 1) return null;
            if (Str(v, "ParentKeyName") != null) return null;
            string release = Str(v, "ReleaseType") ?? "";
            if (release == "Update" || release == "Hotfix" || release == "Security Update" || release == "Update Rollup" || release == "Service Pack") return null;

            var p = new Prog { Name = name, Publisher = Str(v, "Publisher"), Version = Str(v, "DisplayVersion"), Uninstall = uninstall, IconPath = IconFile(Str(v, "DisplayIcon")) };
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
            // Приложение dev.txt: удаляет его core.txt; в списке — под именем продукта
            string uexe, uargs;
            Split(uninstall, out uexe, out uargs);
            string[] paths = { uexe, p.IconPath, Str(v, "InstallLocation") };
            p.Family = DevApps.OwnsProgram(key, p.Publisher, paths);
            string title = p.Family ? DevApps.ProgramTitle(key, paths) : null;
            if (title != null) p.Name = title;
            return p;
        }

        // DisplayIcon → путь к файлу: «"C:Appapp.exe",0» → C:Appapp.exe
        public static string IconFile(string display)
        {
            if (display == null) return null;
            string s = display.Trim();
            int comma = s.LastIndexOf(',');
            int n;
            if (comma > 0 && int.TryParse(s.Substring(comma + 1).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n)) s = s.Substring(0, comma);
            s = s.Trim().Trim('"').Trim();
            return s.Length == 0 ? null : s;
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
                                Prog p = FromValues(v, sub);
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

        public const int BySize = 0, ByDate = 1, ByName = 2;

        public static List<Prog> View(List<Prog> all, string search, bool byDate) { return View(all, search, byDate ? ByDate : BySize); }

        public static List<Prog> View(List<Prog> all, string search, int order)
        {
            var list = new List<Prog>();
            string q = (search ?? "").Trim();
            foreach (Prog p in all)
                if (q.Length == 0 || p.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 || (p.Publisher != null && p.Publisher.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)) list.Add(p);
            list.Sort(delegate(Prog a, Prog b)
            {
                int c = order == ByName ? 0 : order == ByDate ? (b.Installed ?? DateTime.MinValue).CompareTo(a.Installed ?? DateTime.MinValue) : b.Bytes.CompareTo(a.Bytes);
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

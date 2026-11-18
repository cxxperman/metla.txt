// metla.txt. Scan.cs — что занимает место: категории, проверка (только чтение) и очистка выбранного.
// Проверка ничего не меняет на диске. Удаляет только Cleaner, и только через Safe.Delete.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace DevTxt.Metla
{
    // Размеры и числа по-русски
    static class Fmt
    {
        static readonly CultureInfo Ru = MakeRu();
        static CultureInfo MakeRu()
        {
            var c = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            c.NumberFormat.NumberDecimalSeparator = ",";
            c.NumberFormat.NumberGroupSeparator = " ";
            return c;
        }

        public const long KB = 1024, MB = KB * 1024, GB = MB * 1024;

        public static string Size(long bytes)
        {
            if (bytes < 0) bytes = 0;
            if (bytes < KB) return bytes + " Б";
            if (bytes < MB) return Math.Round(bytes / (double)KB).ToString("0", Ru) + " КБ";
            if (bytes < GB)
            {
                double mb = bytes / (double)MB;
                return (mb < 100 ? mb.ToString("0.#", Ru) : Math.Round(mb).ToString("0", Ru)) + " МБ";
            }
            double gb = bytes / (double)GB;
            return (gb < 10 ? gb.ToString("0.##", Ru) : gb < 100 ? gb.ToString("0.#", Ru) : Math.Round(gb).ToString("0", Ru)) + " ГБ";
        }

        public static string Count(long n) { return n.ToString("#,0", Ru); }

        // 1 файл, 2 файла, 5 файлов
        public static string Plural(long n, string one, string few, string many)
        {
            long a = Math.Abs(n) % 100, b = a % 10;
            string word = a >= 11 && a <= 14 ? many : b == 1 ? one : b >= 2 && b <= 4 ? few : many;
            return Count(n) + " " + word;
        }

        public static string Files(long n) { return Plural(n, "файл", "файла", "файлов"); }
    }

    class Item
    {
        public string Path, Root;   // Root — разрешённая папка, внутри которой лежит Path
        public long Size;
        public DateTime When;       // последнее изменение или создание — что позже (UTC)
        public bool IsDir;
        public double MinAgeHours;  // правило возраста, по которому файл попал в список; перед удалением проверяется заново
        public string Owner;        // для кэша браузеров — чей он
    }

    class Category
    {
        public string Id, Title, Hint;
        public bool Selectable = true;     // false — только для сведения, удалить нельзя (загрузки)
        public bool On;                    // выбрана для очистки
        public bool Permanent = true;      // удаляется мимо корзины
        public bool IsRecycle;
        public bool NeedsAdmin;            // папку нельзя прочитать без прав администратора — пропускается
        public long Bytes;
        public long Count;
        public int Unreadable;             // вложенных папок, которые не удалось прочитать
        public string Note;                // что пропущено и почему
        public string OpenFolder;          // папка для кнопки «Открыть папку»
        public readonly List<Item> Items = new List<Item>();
        public readonly List<Item> Dirs = new List<Item>();   // папки, которые можно убрать, если после очистки они опустеют
        public readonly List<Item> Top = new List<Item>();    // самые большие — чтобы было видно, что это

        public bool Cleanable { get { return Selectable && !NeedsAdmin; } }
        public bool Chosen { get { return Cleanable && On && Bytes + Count > 0; } }

        public void Reset() { Bytes = 0; Count = 0; Unreadable = 0; Note = null; NeedsAdmin = false; Items.Clear(); Dirs.Clear(); Top.Clear(); }

        public void Add(Item it) { Items.Add(it); Bytes += it.Size; Count++; }

        public void MakeTop(int n)
        {
            Top.Clear();
            var sorted = new List<Item>(Items);
            sorted.Sort(delegate(Item a, Item b) { return b.Size.CompareTo(a.Size); });
            for (int i = 0; i < sorted.Count && i < n; i++) Top.Add(sorted[i]);
        }
    }

    // Где что лежит. В самопроверке всё указывает внутрь её временной папки.
    class Env
    {
        public string Temp, WinTemp, LocalAppData, ProgramData, Downloads;
        public DateTime Now = DateTime.UtcNow;
        public bool RealRecycle;                       // корзина — настоящая; в проверках false, и к ней не обращаются
        public Func<string, bool> BrowserRunning;      // имя браузера → запущен ли

        public static Env Real()
        {
            var e = new Env();
            e.Temp = Full(Path.GetTempPath());
            e.WinTemp = Full(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"));
            e.LocalAppData = Full(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            e.ProgramData = Full(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            e.Downloads = Full(Shell.DownloadsPath());
            e.RealRecycle = true;
            e.BrowserRunning = Browsers.IsRunning;
            return e;
        }

        static string Full(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            try { return Path.GetFullPath(p).TrimEnd('\\'); } catch { return null; }
        }
    }

    static class Browsers
    {
        public class Def { public string Name, Process, Dir; public bool Firefox; }

        // Только эти папки внутри профиля считаются кэшем. Куки, история, пароли и сам профиль не трогаются никогда.
        public static readonly string[] ChromiumCache = { "Cache", "Code Cache", "GPUCache" };
        public static readonly string[] FirefoxCache = { "cache2" };

        public static Def[] All(string local)
        {
            return new Def[]
            {
                new Def { Name = "Chrome", Process = "chrome", Dir = Path.Combine(local, @"Google\Chrome\User Data") },
                new Def { Name = "Edge", Process = "msedge", Dir = Path.Combine(local, @"Microsoft\Edge\User Data") },
                new Def { Name = "Brave", Process = "brave", Dir = Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data") },
                new Def { Name = "Яндекс Браузер", Process = "browser", Dir = Path.Combine(local, @"Yandex\YandexBrowser\User Data") },
                new Def { Name = "Firefox", Process = "firefox", Dir = Path.Combine(local, @"Mozilla\Firefox\Profiles"), Firefox = true }
            };
        }

        static bool IsProfile(string name)
        {
            return name == "Default" || name == "Guest Profile" || name.StartsWith("Profile ", StringComparison.Ordinal);
        }

        // Папки кэша во всех профилях браузера — и ничего кроме них
        public static List<string> CacheDirs(string dir, bool firefox)
        {
            var found = new List<string>();
            try
            {
                if (!Directory.Exists(dir)) return found;
                foreach (var profile in new DirectoryInfo(dir).GetDirectories())
                {
                    if ((profile.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (!firefox && !IsProfile(profile.Name)) continue;
                    foreach (string name in firefox ? FirefoxCache : ChromiumCache)
                    {
                        var cache = new DirectoryInfo(Path.Combine(profile.FullName, name));
                        if (cache.Exists && (cache.Attributes & FileAttributes.ReparsePoint) == 0) found.Add(cache.FullName);
                    }
                }
            }
            catch { }
            return found;
        }

        // Процессы только перечисляются по имени; ни один не трогается
        public static bool IsRunning(string process)
        {
            try
            {
                Process[] list = Process.GetProcessesByName(process);
                bool any = list.Length > 0;
                foreach (Process p in list) p.Dispose();
                return any;
            }
            catch { return true; }   // не удалось узнать — считаем, что запущен, и кэш не трогаем
        }
    }

    static class Shell
    {
        [StructLayout(LayoutKind.Sequential)]
        struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        public static bool QueryRecycle(out long bytes, out long count)
        {
            bytes = 0; count = 0;
            try
            {
                var info = new SHQUERYRBINFO();
                info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
                if (SHQueryRecycleBin(null, ref info) != 0) return false;
                bytes = info.i64Size; count = info.i64NumItems;
                return true;
            }
            catch { return false; }
        }

        // Стандартный вопрос Windows не показывается только потому, что свой вопрос с точными числами уже задан
        public static bool EmptyRecycle()
        {
            const uint NoConfirmation = 1, NoProgress = 2, NoSound = 4;
            try { return SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoProgress | NoSound) == 0; }
            catch { return false; }
        }

        public static string DownloadsPath()
        {
            try
            {
                IntPtr p;
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out p) == 0)
                {
                    string s = Marshal.PtrToStringUni(p);
                    Marshal.FreeCoTaskMem(p);
                    return s;
                }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    static class Scanner
    {
        public const double TempAgeHours = 24, DownloadsAgeDays = 90;
        public const int TopCount = 8;

        // Список категорий в том порядке, в каком он показывается. Выбор по умолчанию: временные файлы, дампы и эскизы.
        public static List<Category> Categories()
        {
            return new List<Category>
            {
                new Category { Id = "temp", Title = "Временные файлы пользователя", Hint = "Папка Temp: только файлы старше 24 часов, которые никем не открыты", On = true },
                new Category { Id = "wintemp", Title = "Временные файлы Windows", Hint = "Папка Windows\\Temp: то же правило — старше 24 часов и не заняты", On = true },
                new Category { Id = "dumps", Title = "Отчёты об ошибках и дампы", Hint = "Снимки памяти упавших программ и отчёты об ошибках Windows", On = true },
                new Category { Id = "thumbs", Title = "Кэш эскизов", Hint = "Уменьшенные картинки для Проводника. Windows создаст их заново", On = true },
                new Category { Id = "browsers", Title = "Кэш браузеров", Hint = "Только папки кэша. Куки, история, пароли и профили не трогаются", On = false },
                new Category { Id = "recycle", Title = "Корзина", Hint = "Всё, что лежит в корзине на всех дисках", On = false, IsRecycle = true },
                new Category { Id = "downloads", Title = "Загрузки старше 90 дней", Hint = "Только для сведения: программа их не удаляет. Разберите папку сами", On = false, Selectable = false, Permanent = false }
            };
        }

        public static Category Find(List<Category> list, string id) { return list.Find(delegate(Category c) { return c.Id == id; }); }

        // Сколько можно освободить всего: все категории, которые программа умеет чистить
        public static long Total(List<Category> list)
        {
            long sum = 0;
            foreach (var c in list) if (c.Cleanable) sum += c.Bytes;
            return sum;
        }

        public static long Chosen(List<Category> list, out long files)
        {
            long sum = 0; files = 0;
            foreach (var c in list) if (c.Chosen) { sum += c.Bytes; files += c.Count; }
            return sum;
        }

        static DateTime When(FileSystemInfo f)
        {
            DateTime w = f.LastWriteTimeUtc, c = f.CreationTimeUtc;
            return w > c ? w : c;
        }

        public static bool OldEnough(DateTime whenUtc, DateTime nowUtc, double minAgeHours)
        {
            return minAgeHours <= 0 || (nowUtc - whenUtc).TotalHours > minAgeHours;
        }

        // Обходит папку. По ссылкам и точкам соединения не ходит. Возвращает false, если саму папку прочитать нельзя.
        //   minAgeHours — брать только файлы старше; pattern — только файлы верхнего уровня с таким именем (без обхода вглубь);
        //   skipTop — что пропустить на верхнем уровне.
        static bool Walk(Category cat, Env env, string root, double minAgeHours, bool recurse, Func<FileInfo, bool> take, bool collectDirs, string owner)
        {
            if (string.IsNullOrEmpty(root)) return true;
            DirectoryInfo top;
            try
            {
                top = new DirectoryInfo(root);
                if (!top.Exists) return true;
                if ((top.Attributes & FileAttributes.ReparsePoint) != 0) return true;   // папка-ссылка: не наше дело
            }
            catch { return true; }

            var stack = new Stack<DirectoryInfo>();
            stack.Push(top);
            bool first = true, readable = true;
            while (stack.Count > 0)
            {
                DirectoryInfo dir = stack.Pop();
                FileSystemInfo[] entries;
                try { entries = dir.GetFileSystemInfos(); }
                catch
                {
                    if (first) readable = false; else cat.Unreadable++;
                    first = false;
                    continue;
                }
                first = false;
                foreach (FileSystemInfo e in entries)
                {
                    try
                    {
                        if ((e.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        var sub = e as DirectoryInfo;
                        if (sub != null)
                        {
                            if (!recurse) continue;
                            stack.Push(sub);
                            DateTime dw = When(sub);
                            if (collectDirs && OldEnough(dw, env.Now, minAgeHours)) cat.Dirs.Add(new Item { Path = sub.FullName, Root = root, IsDir = true, When = dw, MinAgeHours = minAgeHours });
                            continue;
                        }
                        var f = (FileInfo)e;
                        if (take != null && !take(f)) continue;
                        DateTime w = When(f);
                        if (!OldEnough(w, env.Now, minAgeHours)) continue;
                        cat.Add(new Item { Path = f.FullName, Root = root, Size = f.Length, When = w, MinAgeHours = minAgeHours, Owner = owner });
                    }
                    catch { cat.Unreadable++; }   // слишком длинный путь, файл исчез и т. п.
                }
            }
            return readable;
        }

        static bool SamePath(string a, string b) { return a != null && b != null && string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
        static bool IsDump(FileInfo f) { return f.Extension.Equals(".dmp", StringComparison.OrdinalIgnoreCase); }

        public static void Scan(Category c, Env env)
        {
            c.Reset();
            string local = env.LocalAppData ?? "", localTemp = local == "" ? null : Path.Combine(local, "Temp");
            if (c.Id == "temp")
            {
                // Дампы из этой папки считаются в своей категории — чтобы не посчитать дважды
                bool same = SamePath(env.Temp, localTemp);
                string root = env.Temp;
                Walk(c, env, root, TempAgeHours, true, delegate(FileInfo f) { return !(same && IsDump(f) && SamePath(f.DirectoryName, root)); }, true, null);
            }
            else if (c.Id == "wintemp")
            {
                if (!Walk(c, env, env.WinTemp, TempAgeHours, true, null, true, null)) { c.NeedsAdmin = true; c.Note = "Нужны права администратора — категория пропущена"; }
            }
            else if (c.Id == "dumps")
            {
                if (local != "") Walk(c, env, Path.Combine(local, "CrashDumps"), 0, true, null, true, null);
                if (!string.IsNullOrEmpty(env.ProgramData))
                {
                    Walk(c, env, Path.Combine(env.ProgramData, @"Microsoft\Windows\WER\ReportArchive"), 0, true, null, true, null);
                    Walk(c, env, Path.Combine(env.ProgramData, @"Microsoft\Windows\WER\ReportQueue"), 0, true, null, true, null);
                }
                if (localTemp != null) Walk(c, env, localTemp, 0, false, IsDump, false, null);
            }
            else if (c.Id == "thumbs")
            {
                if (local != "") Walk(c, env, Path.Combine(local, @"Microsoft\Windows\Explorer"), 0, false,
                    delegate(FileInfo f) { return f.Name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) && f.Extension.Equals(".db", StringComparison.OrdinalIgnoreCase); }, false, null);
                if (c.Count > 0) c.Note = "Файлы, которые сейчас держит Проводник, будут пропущены";
            }
            else if (c.Id == "browsers")
            {
                var running = new List<string>();
                int known = 0;
                if (local != "") foreach (var b in Browsers.All(local))
                {
                    List<string> dirs = Browsers.CacheDirs(b.Dir, b.Firefox);
                    if (dirs.Count == 0) continue;
                    known++;
                    if (env.BrowserRunning != null && env.BrowserRunning(b.Process)) { running.Add(b.Name); continue; }
                    foreach (string dir in dirs) Walk(c, env, dir, 0, true, null, false, b.Process);
                }
                if (known == 0) c.Note = "Chrome, Edge, Brave, Firefox и Яндекс Браузер на этом компьютере не найдены";
                if (running.Count > 0) c.Note = (running.Count == 1 ? "Запущен и пропущен: " : "Запущены и пропущены: ") + string.Join(", ", running.ToArray()) + ". Закройте браузер и проверьте снова";
            }
            else if (c.Id == "recycle")
            {
                long bytes, count;
                if (env.RealRecycle && Shell.QueryRecycle(out bytes, out count)) { c.Bytes = bytes; c.Count = count; }
            }
            else if (c.Id == "downloads")
            {
                c.OpenFolder = env.Downloads;
                Walk(c, env, env.Downloads, DownloadsAgeDays * 24, false, delegate(FileInfo f) { return !f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase); }, false, null);
            }
            if (c.Unreadable > 0 && !c.NeedsAdmin)
            {
                string more = "Не удалось прочитать: " + Fmt.Count(c.Unreadable) + " — пропущено";
                c.Note = c.Note == null ? more : c.Note + ". " + more;
            }
            c.MakeTop(TopCount);
        }

        public static void ScanAll(List<Category> list, Env env, Action<Category> progress)
        {
            foreach (var c in list)
            {
                if (progress != null) progress(c);
                Scan(c, env);
            }
        }
    }

    class CleanResult
    {
        public long Freed;
        public long Deleted, Skipped, Refused;
        public bool RecycleEmptied;
    }

    static class Cleaner
    {
        // Удаляет то, что нашла проверка в выбранных категориях. Каждый файл перед удалением проверяется заново.
        public static CleanResult Clean(List<Category> list, Env env, Action<double> progress)
        {
            var r = new CleanResult();
            long total = 0, done = 0;
            foreach (var c in list) if (c.Chosen) total += c.Count;
            DateTime now = DateTime.UtcNow > env.Now ? DateTime.UtcNow : env.Now;

            foreach (var c in list)
            {
                if (!c.Chosen) continue;          // не выбрана, только для сведения или нужны права
                if (c.IsRecycle)
                {
                    if (env.RealRecycle && Shell.EmptyRecycle()) { r.RecycleEmptied = true; r.Freed += c.Bytes; r.Deleted += c.Count; }
                    else r.Skipped += c.Count;
                    done += c.Count;
                    continue;
                }
                var busy = new Dictionary<string, bool>();   // браузер могли открыть уже после проверки
                foreach (Item it in c.Items)
                {
                    done++;
                    if (progress != null && (done & 63) == 0) progress(total == 0 ? 1 : done / (double)total);
                    if (it.Owner != null)
                    {
                        bool run;
                        if (!busy.TryGetValue(it.Owner, out run)) { run = env.BrowserRunning != null && env.BrowserRunning(it.Owner); busy[it.Owner] = run; }
                        if (run) { r.Skipped++; continue; }
                    }
                    long size;
                    try
                    {
                        var f = new FileInfo(it.Path);
                        if (!f.Exists) { r.Skipped++; continue; }
                        DateTime w = f.LastWriteTimeUtc > f.CreationTimeUtc ? f.LastWriteTimeUtc : f.CreationTimeUtc;
                        if (!Scanner.OldEnough(w, now, it.MinAgeHours)) { r.Skipped++; continue; }   // файл изменили после проверки
                        size = f.Length;
                    }
                    catch { r.Skipped++; continue; }
                    Del d = Safe.Delete(it.Root, it.Path);
                    if (d == Del.Deleted) { r.Deleted++; r.Freed += size; }
                    else if (d == Del.Refused) r.Refused++;
                    else r.Skipped++;
                }
                // Папки, которые после этого опустели: сначала самые глубокие. Непустая не удалится — так и задумано.
                var dirs = new List<Item>(c.Dirs);
                dirs.Sort(delegate(Item a, Item b) { return b.Path.Length.CompareTo(a.Path.Length); });
                foreach (Item d in dirs)
                {
                    try { if (Directory.GetFileSystemEntries(d.Path).Length > 0) continue; } catch { continue; }
                    Safe.Delete(d.Root, d.Path);
                }
            }
            if (progress != null) progress(1);
            return r;
        }
    }
}

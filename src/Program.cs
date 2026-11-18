// metla.txt — аккуратная уборка компьютера: показывает, что занимает место и что запускается вместе с Windows,
// и удаляет только то, что вы отметили и подтвердили. Ничего «оптимизирующего» здесь нет.
// Ключи для проверки:
//   --demo              выдуманное состояние; диск, реестр и сеть не трогаются, удаление выключено
//   --shot файл.png     нарисовать окно в файл и выйти (обычно вместе с --demo)
//   --page имя          какую страницу открыть: обзор | очистка | автозапуск | программы (или overview | cleanup | startup | programs)
//   --sheet             для снимка: показать вопрос перед очисткой;  --expand id — раскрыть категорию;  --scroll N — прокрутить
//   --scan-only         настоящая проверка только для чтения: печатает итоги по категориям и выходит, ничего не удаляя
//   --selftest          проверить правила удаления на собственной временной папке, настройки и работу без хаба; код выхода 0 — всё в порядке
//   --headless          запуститься без окна: только связь с хабом (для проверок протокола)
// Для проверок же: METLA_HUB_PORT — хаб на другом порту, METLA_DATA — другая папка для настроек.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;

namespace DevTxt.Metla
{
    static class Program
    {
        // Имя в хабе, в каталоге core.txt и в папке настроек — строчными латинскими; Name — имя файла и процесса
        public const string Id = "metla", Name = "Metla";

        public static string VersionText
        {
            get { Version v = typeof(Program).Assembly.GetName().Version; return v.Major + "." + v.Minor + "." + v.Build; }
        }

        // Имя страницы из ключа --page: по-русски или латиницей; −1 — такого нет
        public static int PageIndex(string name)
        {
            string n = (name ?? "").Trim().ToLowerInvariant();
            if (n == "обзор" || n == "overview" || n == "obzor") return MainWindow.POverview;
            if (n == "очистка" || n == "cleanup" || n == "clean" || n == "ochistka") return MainWindow.PClean;
            if (n == "автозапуск" || n == "startup" || n == "autostart" || n == "avtozapusk") return MainWindow.PStartup;
            if (n == "программы" || n == "programs" || n == "apps" || n == "programmy") return MainWindow.PPrograms;
            return -1;
        }

        static string Arg(string[] args, string key)
        {
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == key) return args[i + 1];
            return null;
        }

        [STAThread]
        static int Main(string[] args)
        {
            bool demo = Array.IndexOf(args, "--demo") >= 0, headless = Array.IndexOf(args, "--headless") >= 0;
            string shot = Arg(args, "--shot");
            // У оконной программы нет своей консоли: вывод проверок идёт в перенаправленный поток, всегда в UTF-8
            try
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError(), new System.Text.UTF8Encoding(false)) { AutoFlush = true });
            }
            catch { }
            if (Array.IndexOf(args, "--selftest") >= 0) return SelfTest.Run();
            if (Array.IndexOf(args, "--scan-only") >= 0) return ScanOnly();

            int page = MainWindow.POverview;
            string pageName = Arg(args, "--page");
            if (pageName != null)
            {
                page = PageIndex(pageName);
                if (page < 0) { Console.Error.WriteLine("Нет такой страницы: " + pageName); return 2; }
            }

            bool created = true;
            Mutex mutex = null;
            if (shot == null && !demo) mutex = new Mutex(true, "devtxt_" + Id + "_single_instance", out created);
            if (!created)
            {
                // Уже запущено: просим показать окно и выходим
                try { EventWaitHandle.OpenExisting("devtxt_" + Id + "_show").Set(); } catch { }
                return 0;
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            if (demo) Prefs.Persist = false; else Prefs.Load();
            Safe.Armed = !demo && shot == null;   // в --demo и на снимках удаление выключено совсем
            var w = new MainWindow(demo, page);
            if (shot != null)
            {
                double scroll;
                double.TryParse(Arg(args, "--scroll") ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture, out scroll);
                w.Shot(shot, Array.IndexOf(args, "--sheet") >= 0, Arg(args, "--expand"), scroll);
                return 0;
            }

            // В --demo хаб не трогается; исключение — проверочный хаб на своём порту (METLA_HUB_PORT)
            if (!demo || AppHub.TestPort > 0)
            {
                AppHub.Show = w.ShowWindow;
                AppHub.Start(w.Dispatcher);
            }
            if (!demo)
            {
                var show = new EventWaitHandle(false, EventResetMode.AutoReset, "devtxt_" + Id + "_show");
                new Thread(delegate() { while (true) { show.WaitOne(); w.Dispatcher.BeginInvoke(new Action(w.ShowWindow)); } }) { IsBackground = true }.Start();
            }
            w.Closed += delegate { AppHub.Stop(); app.Shutdown(); };
            if (!headless) w.Show();
            app.Run();
            GC.KeepAlive(mutex);
            return 0;
        }

        // Настоящая проверка без окна и без удаления: только итоги по категориям, без имён файлов
        static int ScanOnly()
        {
            Safe.Armed = false;
            var watch = Stopwatch.StartNew();
            List<Category> cats = Scanner.Categories();
            Env env = Env.Real();
            foreach (Category c in cats)
            {
                var one = Stopwatch.StartNew();
                Scanner.Scan(c, env);
                string state = c.NeedsAdmin ? "нужны права администратора" : !c.Selectable ? "только для сведения" : c.On ? "выбрано по умолчанию" : "не выбрано по умолчанию";
                Console.WriteLine(string.Format("{0,-32} {1,10}  {2,14}  {3}  [{4} мс]{5}", c.Title, Fmt.Size(c.Bytes), Fmt.Files(c.Count), state, one.ElapsedMilliseconds, c.Note == null ? "" : "  — " + c.Note));
            }
            long files;
            long chosen = Scanner.Chosen(cats, out files);
            Console.WriteLine("Можно освободить всего: " + Fmt.Size(Scanner.Total(cats)) + "; выбрано по умолчанию: " + Fmt.Size(chosen) + ", " + Fmt.Files(files));
            // Автозапуск и программы: только чтение реестра и папок, только количества
            List<StartupEntry> startup = Startup.Read();
            Console.WriteLine("Автозапуск: своих записей " + startup.FindAll(delegate(StartupEntry e) { return e.Editable; }).Count + ", общих " + startup.FindAll(delegate(StartupEntry e) { return !e.Editable; }).Count
                + ", отключено " + startup.FindAll(delegate(StartupEntry e) { return !e.Enabled; }).Count);
            Console.WriteLine("Программы: " + Programs.Read().Count);
            Console.WriteLine("Проверка заняла " + watch.ElapsedMilliseconds + " мс. Ничего не удалено.");
            return 0;
        }
    }

    // Настройки приложения
    static class Prefs
    {
        public static bool Remind;                 // напоминать, когда можно освободить больше 5 ГБ (тогда приложение остаётся в трее)
        public static DateTime LastAuto;           // когда была последняя ежедневная проверка (UTC)
        public static bool Persist = true;         // false — ничего не писать на диск (--demo)
        public static event Action Changed;        // вызывается в потоке окна

        public static string Dir
        {
            get
            {
                string test = Environment.GetEnvironmentVariable("METLA_DATA");
                if (!string.IsNullOrEmpty(test)) return test;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dev.txt", Program.Id);
            }
        }
        static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }

        public static void Load()
        {
            try
            {
                var d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(FilePath)) as Dictionary<string, object>;
                object v;
                if (d != null && d.TryGetValue("remind", out v) && v is bool) Remind = (bool)v;
                DateTime t;
                if (d != null && d.TryGetValue("lastAuto", out v) && v is string && DateTime.TryParse((string)v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t)) LastAuto = t.ToUniversalTime();
            }
            catch { }   // файла ещё нет или он испорчен — остаются значения по умолчанию
        }

        public static void Save()
        {
            if (!Persist) return;
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "remind", Remind }, { "lastAuto", LastAuto.ToString("o", CultureInfo.InvariantCulture) } }));
            }
            catch { }
        }

        // true — значение действительно поменялось
        public static bool SetRemind(bool on)
        {
            if (Remind == on) return false;
            Remind = on;
            Save();
            if (Changed != null) Changed();
            return true;
        }
    }
}

// metla.txt. AppHub.cs — связь с core.txt, лаунчером dev.txt™. Он держит на этом компьютере хаб (127.0.0.1:42080).
// Приложение отдаёт туда повседневную настройку (напоминание) и итог последней проверки («metla.found»),
// принимает оттуда изменения и общий вид: «Меньше движения» для всех приложений разом.
// Для дока dock.txt — корзина Windows: «metla.bin» (сколько в ней лежит) и команда bin.open (Bin.cs).
// Семья (Family.cs): пока core.txt держит общий значок в трее, своего у metla.txt нет — её пункты («Открыть», «Проверить диск»)
// стоят в меню core.txt (metla.tray); напоминание уходит на остров kapsula.txt; «Не беспокоить» и игру сообщает core.txt.
// core.txt может быть не запущен — это нормально: приложение работает само и тихо пробует подключиться снова.
// Протокол описан в kit.txt (hub/PROTOCOL.md); HubLink.cs и Family.cs скопированы оттуда как есть.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Threading;

namespace DevTxt.Metla
{
    static class AppHub
    {
        public const string Topic = Program.Id + ".settings", FoundTopic = Program.Id + ".found", BinTopic = Program.Id + ".bin";

        static HubLink link;
        static Family family;
        static Dispatcher ui;
        static int published;
        static Dictionary<string, object> found;   // итог последней проверки; до первой проверки состояния нет
        static BinWatch bin;                        // корзина для дока («metla.bin», bin.open); null — не следим (--demo)

        public static Action Quit;                  // закрыть metla.txt: «Закрыть» в трее core.txt, пока своего значка нет
        public static Action Show;                  // показать окно: команда «show» от другого приложения
        public static Action<string> ShowAt;        // то же, но на странице из args.page: "scan", "startup" или "bin" (см. PageArg)
        public static BinSource Bin;                // откуда числа о корзине; задаёт Program, в --demo — null
        public static Func<IList<TrayItem>> Tray;   // пункты metla.txt для общего меню трея core.txt — берутся при запуске связи
        public static event Action StatusChanged;   // связь появилась или пропала; вызывается в потоке окна
        public static event Action FamilyChanged;   // core.family или hub.apps поменялись, связь пропала; вызывается в потоке окна
        public static bool Online { get { return link != null && link.Online; } }

        // Что сообщает core.txt (core.family). Без связи всё «нет»: свой значок виден, игру проверяем сами.
        public static bool CoreOnline { get { return family != null && family.CoreOnline; } }
        public static bool HideOwnTray { get { return family != null && family.HideOwnTray; } }
        public static bool Focus { get { return family != null && family.Focus; } }
        public static bool Game { get { return family != null && family.Game; } }
        public static bool IslandOnline { get { return family != null && family.IslandOnline; } }

        // Напоминание на острове kapsula.txt; щелчок по карточке пришлёт команду show. false — острова нет.
        public static bool Notify(string text) { return family != null && family.Notify(Program.Id + ".txt", text, true); }

        // Пункты в общем меню трея core.txt; одно и то же второй раз не отправляется
        public static void SetTray(IList<TrayItem> items) { if (family != null) family.SetTray(items); }

        // Просьба к core.txt показать своё окно; page — какую страницу открыть (general, apps…), null — как он сам решит
        public static void ShowCore() { ShowCore(null); }
        public static void ShowCore(string page) { if (link != null) link.Command("core", "show", page == null ? null : HubLink.Msg("page", page)); }

        // Для проверок: хаб на другом порту, рядом с настоящим core.txt
        public static int TestPort
        {
            get { int p; return int.TryParse(Environment.GetEnvironmentVariable("METLA_HUB_PORT"), out p) && p > 0 ? p : 0; }
        }

        // Описание настроек — в том виде, в каком его ждёт core.txt: всегда целиком.
        public static Dictionary<string, object> Settings()
        {
            var items = new object[]
            {
                HubLink.Msg("key", "remind", "type", "switch", "label", "Напоминать, когда можно освободить больше 5 ГБ",
                    "hint", "Пока включено, metla.txt остаётся в фоне и раз в день сама проверяет диск. Удаляете всё равно только вы", "value", Prefs.Remind)
            };
            return HubLink.Msg("groups", new object[] { HubLink.Msg("title", "", "items", items) });
        }

        // Меняет настройку по её ключу. Вызывается в потоке окна; незнакомый ключ или не то значение — исключение,
        // его текст уйдёт в core.txt ответом «не получилось».
        public static void Apply(string key, object value)
        {
            if (key == "remind")
            {
                if (!(value is bool)) throw new Exception("нужно «да» или «нет»");
                Prefs.SetRemind((bool)value);
                return;
            }
            throw new Exception("такой настройки нет");
        }

        public static bool ReadReduced(object theme)
        {
            var d = theme as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue("reducedMotion", out v) && v is bool && (bool)v;
        }

        // Итог проверки в том виде, в каком он уходит в хаб: { bytes, at }
        public static Dictionary<string, object> FoundData(long bytes, DateTime atUtc)
        {
            return HubLink.Msg("bytes", bytes, "at", atUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        }

        // Страница из args.page команды show: "scan" — что занимает место, "startup" — автозапуск,
        // "bin" — вопрос про очистку корзины (обычный лист с числами; удаляет только «Удалить» в нём); иное — null (окно как обычно)
        public static string PageArg(Dictionary<string, object> args)
        {
            object v;
            if (args == null || !args.TryGetValue("page", out v)) return null;
            string p = v as string;
            return p == "scan" || p == "startup" || p == "bin" ? p : null;
        }

        // Для проверок: следить за подменённой корзиной вместо настоящей
        internal static BinWatch BinWatcher { get { return bin; } set { bin = value; } }

        // Корзину очистили в самом окне — пересчитать и прислать новое «metla.bin»
        public static void BinChanged() { if (bin != null) bin.Poke(); }

        // После каждой проверки: сколько можно освободить и когда это посчитано
        public static void Found(long bytes, DateTime atUtc)
        {
            found = FoundData(bytes, atUtc);
            if (link != null) link.State(FoundTopic, found);
        }

        // Связь, ещё не запущенная: сначала свои темы, команды и обработчики, потом Family — так требует Family.cs
        // (он дописывает core.family, hub.apps и tray.run и оборачивает обработчики). Отдельно — чтобы проверить без сети.
        internal static HubLink Wire(int port, out Family fam)
        {
            var l = new HubLink(Program.Id, Program.Name, Program.VersionText, port);
            l.Topics = new string[] { "hub.theme" };
            l.Commands = new string[] { "settings.set", "show", "bin.open" };   // команды, которая чистит, нет и не будет (CLAUDE.md)
            l.OnCommand = Command;
            l.OnState = delegate(string topic, object data, string from)
            {
                if (topic != "hub.theme") return;
                bool reduced = ReadReduced(data);
                OnUi(delegate { Ui.ForceReducedMotion = reduced; });
            };
            l.OnStatus = delegate(bool on) { OnUi(delegate { if (StatusChanged != null) StatusChanged(); }); };
            fam = new Family(l, Program.Id, delegate { OnUi(delegate { if (Quit != null) Quit(); }); });
            fam.Changed = delegate { OnUi(delegate { if (FamilyChanged != null) FamilyChanged(); }); };   // приходит в потоке хаба
            if (Tray != null) fam.SetTray(Tray());   // хаба ещё нет — клиент запомнит и отдаст после подключения
            return l;
        }

        static void OnUi(Action a)
        {
            Dispatcher d = ui;
            if (d != null) d.BeginInvoke(a);
        }

        // Выполнить в потоке окна и дождаться; окна нет (проверки) — прямо здесь
        static void OnUiNow(Action a)
        {
            Dispatcher d = ui;
            if (d == null || d.CheckAccess()) a(); else d.Invoke(a);
        }

        public static void Start(Dispatcher dispatcher)
        {
            if (link != null) return;
            ui = dispatcher;
            link = Wire(TestPort > 0 ? TestPort : HubLink.DefaultPort, out family);
            Prefs.Changed += Publish;   // настройку поменяли в самом окне — страница в core.txt тоже обновится
            link.Start();
            Publish();                  // хаба ещё нет — клиент запомнит и повторит после подключения
            if (found != null) link.State(FoundTopic, found);
            if (Bin != null && bin == null)
            {
                HubLink l = link;
                bin = new BinWatch(Bin, delegate(Dictionary<string, object> d) { l.State(BinTopic, d); });
                bin.Start(true);   // в потоке окна: уведомления оболочки приходят в его очередь
                BinWatch w = bin;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate { try { w.Recount(); } catch { } });   // первое число — сразу, но не в потоке окна
            }
        }

        public static void Stop()
        {
            if (bin != null) bin.Stop();
            if (link != null) link.Stop();
        }

        public static void Publish()
        {
            published++;
            if (link != null) link.State(Topic, Settings());
        }

        // Приходит в фоновом потоке
        static object Command(string name, Dictionary<string, object> args, string from)
        {
            // Открыть корзину — в этом же фоновом потоке: окно для этого не нужно
            if (name == "bin.open")
            {
                if (bin == null) throw new Exception("корзина недоступна");
                bin.Open();
                return null;
            }
            Exception failed = null;
            OnUiNow(delegate
            {
                try
                {
                    if (name == "show")
                    {
                        string page = PageArg(args);
                        if (page != null && ShowAt != null) ShowAt(page);
                        else if (Show != null) Show();
                        return;
                    }
                    object key, value;
                    args.TryGetValue("key", out key);
                    args.TryGetValue("value", out value);
                    int before = published;
                    try { Apply(key as string, value); }
                    finally { if (published == before) Publish(); }   // core.txt ждёт полную картину, даже если ничего не изменилось
                }
                catch (Exception e) { failed = e; }
            });
            if (failed != null) throw failed;
            return null;
        }
    }
}

// metla.txt. AppHub.cs — связь с core.txt, лаунчером dev.txt™. Он держит на этом компьютере хаб (127.0.0.1:42080).
// Приложение отдаёт туда повседневную настройку (напоминание) и итог последней проверки («metla.found»),
// принимает оттуда изменения и общий вид: «Меньше движения» для всех приложений разом.
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
        public const string Topic = Program.Id + ".settings", FoundTopic = Program.Id + ".found";

        static HubLink link;
        static Family family;
        static Dispatcher ui;
        static int published;
        static Dictionary<string, object> found;   // итог последней проверки; до первой проверки состояния нет

        public static Action Quit;                  // закрыть metla.txt: «Закрыть» в трее core.txt, пока своего значка нет
        public static Action Show;                  // показать окно: команда «show» от другого приложения
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
            l.Commands = new string[] { "settings.set", "show" };
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

        public static void Start(Dispatcher dispatcher)
        {
            if (link != null) return;
            ui = dispatcher;
            link = Wire(TestPort > 0 ? TestPort : HubLink.DefaultPort, out family);
            Prefs.Changed += Publish;   // настройку поменяли в самом окне — страница в core.txt тоже обновится
            link.Start();
            Publish();                  // хаба ещё нет — клиент запомнит и повторит после подключения
            if (found != null) link.State(FoundTopic, found);
        }

        public static void Stop()
        {
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
            Exception failed = null;
            ui.Invoke(new Action(delegate
            {
                try
                {
                    if (name == "show") { if (Show != null) Show(); return; }
                    object key, value;
                    args.TryGetValue("key", out key);
                    args.TryGetValue("value", out value);
                    int before = published;
                    try { Apply(key as string, value); }
                    finally { if (published == before) Publish(); }   // core.txt ждёт полную картину, даже если ничего не изменилось
                }
                catch (Exception e) { failed = e; }
            }));
            if (failed != null) throw failed;
            return null;
        }
    }
}

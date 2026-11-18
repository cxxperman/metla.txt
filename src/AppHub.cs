// metla.txt. AppHub.cs — связь с core.txt, лаунчером dev.txt™. Он держит на этом компьютере хаб (127.0.0.1:42080).
// Приложение отдаёт туда повседневную настройку (напоминание) и итог последней проверки («metla.found»),
// принимает оттуда изменения и общий вид: «Меньше движения» для всех приложений разом.
// core.txt может быть не запущен — это нормально: приложение работает само и тихо пробует подключиться снова.
// Протокол описан в kit.txt (hub/PROTOCOL.md); HubLink.cs скопирован оттуда как есть.
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
        static Dispatcher ui;
        static int published;
        static Dictionary<string, object> found;   // итог последней проверки; до первой проверки состояния нет

        public static Action Show;                  // показать окно: команда «show» от другого приложения
        public static event Action StatusChanged;   // связь появилась или пропала; вызывается в потоке окна
        public static bool Online { get { return link != null && link.Online; } }

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
                    "hint", "metla.txt остаётся в трее и раз в день сама проверяет диск. Удаляете всё равно только вы", "value", Prefs.Remind)
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

        public static void Start(Dispatcher dispatcher)
        {
            if (link != null) return;
            ui = dispatcher;
            link = new HubLink(Program.Id, Program.Name, Program.VersionText, TestPort > 0 ? TestPort : HubLink.DefaultPort);
            link.Topics = new string[] { "hub.theme" };
            link.Commands = new string[] { "settings.set", "show" };
            link.OnCommand = Command;
            link.OnState = delegate(string topic, object data, string from)
            {
                if (topic != "hub.theme") return;
                bool reduced = ReadReduced(data);
                ui.BeginInvoke(new Action(delegate { Ui.ForceReducedMotion = reduced; }));
            };
            link.OnStatus = delegate(bool on) { ui.BeginInvoke(new Action(delegate { if (StatusChanged != null) StatusChanged(); })); };
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

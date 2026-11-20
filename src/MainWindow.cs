// metla.txt. MainWindow.cs — окно: шапка с вкладками, четыре страницы, строка состояния с главной кнопкой
// и лист с вопросом поверх окна. Здесь же проверка, очистка и напоминание из трея.
// Всё собрано из общих элементов (Ui.cs) и токенов (Tokens.cs). Общее для семьи dev.txt (один значок в трее у core.txt,
// уведомления на острове kapsula.txt, «Не беспокоить» и игра) — через AppHub и Family.cs.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DevTxt.Metla
{
    class MainWindow : Window
    {
        const double W = 1120, H = 720;
        internal const int POverview = 0, PClean = 1, PStartup = 2, PPrograms = 3;
        internal const long RemindBytes = 5 * Fmt.GB;
        // Значок metla.txt — метла: черенок, головка с глазами, прутья. Тот же рисунок — в sprite.json.
        internal static readonly string[] Sprite = { ".......##.", "......##..", ".....##...", "....##....", "...##.....", "..####....", ".######...", ".##E###...", "###E####..", "########..", "#.#.#.#.#.", "#.#.#.#.#." };

        internal readonly bool DemoMode;
        internal List<Category> Cats = Scanner.Categories();
        internal int CatsVersion;                  // растёт, когда список категорий подменён или пересчитан: по нему страницы узнают о новых данных
        internal bool Scanned, Busy;
        internal bool Cleaning;                    // сейчас идёт удаление: ход известен точно (CleanDone из CleanTotal)
        internal long CleanDone, CleanTotal;
        internal string BusyText;
        internal DateTime ScanAt;
        internal CleanResult Last;                 // итог последней очистки — для строки состояния
        internal long DiskTotal, DiskFree;
        internal string DiskName = "C:";
        internal List<StartupEntry> StartupItems;  // null — ещё не читали
        internal List<Prog> Progs;
        internal string Notice;                    // последняя ошибка — показывается в строке состояния до следующего удачного действия
        internal bool LoadingStartup, LoadingProgs;

        readonly TextBlock footer = Ui.T("", Tokens.Size.Caption, Ui.Label3, FontWeights.Normal);
        readonly Btn mainBtn = new Btn(), secondBtn = new Btn();
        readonly TextBlock coreLink = Ui.T("", Tokens.Size.Caption, Ui.Label2, FontWeights.Normal);
        readonly Grid sheetLayer = new Grid { Visibility = Visibility.Collapsed };
        readonly DispatcherTimer daily = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        OverviewPage overview;
        CleanPage clean;
        StartupPage startup;
        ProgramsPage programs;
        NavShell shell;   // общий каркас окна dev.txt
        static readonly string[][] Titles =
        {
            new string[] { "Обзор", "Сколько места можно освободить и что занимает диск" },
            new string[] { "Очистка", "Выберите, что убрать. Удаляется только после подтверждения" },
            new string[] { "Автозапуск", "Программы, которые запускаются вместе с Windows" },
            new string[] { "Программы", "Установленные программы и сколько они занимают" }
        };
        int page;
        bool quitting, firstScanDone, sheetOpen;
        int sheetToken;               // номер открытого листа: по нему отложенное закрытие узнаёт, что лист уже другой
        Btn sheetCancel;
        FrameworkElement sheetCard;
        IInputElement sheetReturn;    // куда вернуть фокус после листа
        Prog pendingUninstall;        // программа, для которой запущено удаление: список обновится, когда вы вернётесь в окно
        DateTime pendingSince;        // когда его запустили: через четверть часа ждать перестаём
        System.Windows.Forms.NotifyIcon tray;
        readonly TrayHold trayHold = new TrayHold(TimeSpan.FromSeconds(3));   // только что запустились: свой значок ждёт слова core.txt, чтобы не мелькнуть
        bool trayNotice;              // свой значок виден только ради уведомления Windows: общий значок у core.txt, а острова нет
        readonly DispatcherTimer noticeEnd = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };   // запасной конец такого уведомления, если Windows о нём не сказала
        bool trayBusy;                // с каким «идёт работа» пункты ушли в общее меню трея core.txt
        bool remindPending;           // напоминание ждёт, когда его можно будет показать
        readonly DispatcherTimer remindRetry = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr icon);

        public MainWindow(bool demo, int startPage)
        {
            DemoMode = demo;
            page = startPage;
            Title = Program.Id + ".txt";
            // Окно меняет размер; начальный размер не больше рабочей области экрана
            Rect work = SystemParameters.WorkArea;
            MinWidth = Math.Min(900, work.Width); MinHeight = Math.Min(560, work.Height);
            Width = Math.Max(MinWidth, Math.Min(W, work.Width)); Height = Math.Max(MinHeight, Math.Min(H, work.Height));
            // Родное окно Windows 11: системная рамка и тень, Mica, тема Windows, прилипание к краям экрана
            Ui.ThemeFollows = !demo;
            Ui.Native(this, true);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Ui.Bg;
            FontFamily = Ui.TextFont;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            overview = new OverviewPage(this);
            clean = new CleanPage(this);
            startup = new StartupPage(this);
            programs = new ProgramsPage(this);
            Content = Build();
            if (demo) Demo.Fill(this); else ReadDisk();
            ShowPage(page);

            Loaded += delegate { Enter(); };
            // Пока открыт лист: Esc отменяет, а фокус с клавиатуры не уходит за его пределы
            PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (!sheetOpen) return;
                if (e.Key == Key.Escape) { e.Handled = true; CloseSheet(); return; }
                if (sheetCard != null && !sheetCard.IsKeyboardFocusWithin && sheetCancel != null)
                {
                    Keyboard.Focus(sheetCancel.Root);
                    if (e.Key == Key.Tab || e.Key == Key.Enter || e.Key == Key.Space) e.Handled = true;
                }
            };
            if (demo) return;

            // Первый показ окна: сразу начинается проверка — она только читает
            ContentRendered += delegate
            {
                if (firstScanDone) return;
                firstScanDone = true;
                if (!Scanned && !Busy) StartScan();
            };
            Activated += delegate
            {
                if (sheetOpen) return;
                // core.txt могли поставить или удалить, пока окно было в фоне: строки приложений dev.txt перестраиваются
                if (page == PStartup || page == PPrograms) RenderAll();
                // Вернулись в окно после запуска программы удаления — список программ перечитывается
                if (pendingUninstall == null || LoadingProgs) return;
                if (DateTime.UtcNow - pendingSince > TimeSpan.FromMinutes(15)) { pendingUninstall = null; return; }   // удаление отменили — хватит перечитывать
                LoadPrograms();
            };

            // Напоминание: приложение остаётся в фоне, только пока включена эта настройка. Свой значок в трее — только
            // когда общий не у core.txt: у семьи значок один (core.family.tray); связь пропала — свой появляется сразу.
            // Только что запустились, а core.txt установлен — до трёх секунд ждём его слова, чтобы значок не мелькнул.
            // Ожидание кончается по своему таймеру (TrayHold), а не по часам: без core.txt свой значок появляется всегда.
            trayHold.Over = SyncTray;
            if (CoreApp.Installed) trayHold.Start();
            Prefs.Changed += delegate { SyncTray(); RenderAll(); };
            AppHub.FamilyChanged += delegate { SyncTray(); TryRemind(); };
            AppHub.StatusChanged += delegate { if (!AppHub.Online) trayHold.Stop(); SyncTray(); Footer(); };
            SyncTray();
            daily.Tick += delegate { DailyCheck(); };
            daily.Start();
            remindRetry.Tick += delegate { TryRemind(); };
            noticeEnd.Tick += delegate { EndNotice(); };
            // Окно открыли — напоминание уже не нужно: числа и так перед глазами
            IsVisibleChanged += delegate { if (IsVisible) { remindPending = false; remindRetry.Stop(); } };
            Closing += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                if (!quitting && Prefs.Remind && tray != null) { e.Cancel = true; Hide(); return; }
                DropTray();
            };
        }

        // ───────── Разметка ─────────
        FrameworkElement Build()
        {
            // Общий каркас dev.txt: слева значок, имя и разделы, справа заголовок, страница и нижняя строка
            shell = new NavShell(this, Sprite, Program.Id, "версия " + Program.VersionText, Close);
            shell.Add("Обзор", Ui.Glyph("\uE80F"));
            shell.Add("Очистка", Ui.Glyph("\uE74D"));
            shell.Add("Автозапуск", Ui.Glyph("\uE7E8"));
            shell.Add("Программы", Ui.Glyph("\uE71D"));
            shell.Changed = delegate(int i) { ShowPage(i); };

            // Низ: строка состояния и кнопки
            mainBtn.Root.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(mainBtn.Root, Dock.Right);
            shell.Bottom.Children.Add(mainBtn.Root);
            secondBtn.Root.VerticalAlignment = VerticalAlignment.Center;
            secondBtn.Root.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(secondBtn.Root, Dock.Right);
            shell.Bottom.Children.Add(secondBtn.Root);
            // Дорога к core.txt: тихая ссылка внизу слева, под разделами — открыть, а если его нет — скачать
            coreLink.Cursor = Cursors.Hand; coreLink.Background = Brushes.Transparent; coreLink.ToolTip = CoreApp.Pitch;
            coreLink.Margin = new Thickness(20, 0, 20, 18);
            coreLink.MouseEnter += delegate { coreLink.Foreground = Ui.Label; };
            coreLink.MouseLeave += delegate { coreLink.Foreground = Ui.Label2; };
            coreLink.MouseLeftButtonUp += delegate { if (!DemoMode) CoreApp.Go(AppHub.Online, AppHub.ShowCore); };
            var pane = (DockPanel)shell.Status.Parent;
            DockPanel.SetDock(coreLink, Dock.Bottom);
            pane.Children.Insert(pane.Children.IndexOf(shell.Status), coreLink);
            footer.VerticalAlignment = VerticalAlignment.Center;
            footer.TextWrapping = TextWrapping.Wrap; footer.Margin = new Thickness(0, 0, 16, 0);
            shell.Bottom.Children.Add(footer);

            foreach (FrameworkElement p in new FrameworkElement[] { overview.Root, clean.Root, startup.Root, programs.Root }) shell.Body.Children.Add(p);
            shell.Root.Children.Add(sheetLayer);   // лист подтверждения — поверх всего окна
            return shell.Root;
        }

        internal void ShowPage(int i)
        {
            page = i;
            shell.Selected = i;
            shell.Title(Titles[i][0], Titles[i][1]);
            overview.Root.Visibility = i == POverview ? Visibility.Visible : Visibility.Collapsed;
            clean.Root.Visibility = i == PClean ? Visibility.Visible : Visibility.Collapsed;
            startup.Root.Visibility = i == PStartup ? Visibility.Visible : Visibility.Collapsed;
            programs.Root.Visibility = i == PPrograms ? Visibility.Visible : Visibility.Collapsed;
            if (i == PStartup && StartupItems == null) LoadStartup();
            if (i == PPrograms && Progs == null) LoadPrograms();
            RenderAll();
            Enter();
        }

        // Строки открытой страницы появляются волной (общий приём из Ui.cs); на снимках и без окна — сразу на месте
        void Enter()
        {
            if (!IsVisible) return;
            Page p = page == POverview ? (Page)overview : page == PClean ? (Page)clean : page == PStartup ? (Page)startup : programs;
            Motion.Enter(p.Root);   // страница поднимается снизу, как в «Параметрах» Windows 11
            Motion.Stagger(p.Entrance);
        }

        // Обновляет открытую страницу на месте (строки не пересоздаются), строку состояния и кнопки
        internal void RenderAll()
        {
            if (page == POverview) overview.Update();
            else if (page == PClean) clean.Update();
            else if (page == PStartup) startup.Update();
            else programs.Update();
            Footer();

            long files;
            long chosen = Scanner.Chosen(Cats, out files);
            if (page == PStartup) { secondBtn.Hide(); mainBtn.Set("Обновить список", null, !LoadingStartup, LoadStartup); }
            else if (page == PPrograms) { secondBtn.Hide(); mainBtn.Set("Обновить список", null, !LoadingProgs, LoadPrograms); }
            else if (!Scanned) { secondBtn.Hide(); mainBtn.Set(Busy ? "Проверяю…" : "Проверить", Tokens.Palette.Brand, !Busy, StartScan); }
            else
            {
                secondBtn.Set("Проверить снова", null, !Busy, StartScan);
                mainBtn.Set("Очистить выбранное", Tokens.Palette.Brand, !Busy && chosen + files > 0, AskClean);
            }
            // «Проверить диск» в общем меню трея core.txt недоступно, пока идёт работа
            if (Busy != trayBusy) { trayBusy = Busy; AppHub.SetTray(TrayItems()); }
        }

        // Что сейчас делается — одной строкой: для строки состояния и для «Обзора»
        internal string BusyLine()
        {
            if (Cleaning) return "Удаляю выбранное: " + Fmt.Count(CleanDone) + " из " + Fmt.Count(CleanTotal);
            return BusyText ?? "Работаю…";
        }

        void Footer()
        {
            long files;
            long chosen = Scanner.Chosen(Cats, out files);
            string line;
            if (Notice != null) line = Notice;
            else if (page == PStartup) line = LoadingStartup || StartupItems == null ? "Читаю список…" : "Отключённое можно включить обратно здесь или в Диспетчере задач" + (ManagedStartup ? ", а приложения dev.txt — в core.txt" : "");
            else if (page == PPrograms) line = Progs == null ? "Читаю список…" : "Программ: " + Fmt.Count(Progs.Count) + " · каждую удаляет её собственная программа удаления" + (ManagedProgs ? ", а приложения dev.txt — core.txt" : "");
            else if (Busy) line = BusyLine();
            else if (Last != null) line = "Освобождено " + Fmt.Size(Last.Freed) + " · удалено: " + Fmt.Count(Last.Deleted) + (Last.Skipped + Last.Refused > 0 ? " · пропущено: " + Fmt.Count(Last.Skipped + Last.Refused) + " (заняты или нет доступа)" : "");
            else if (Scanned) line = "Проверено в " + ScanAt.ToLocalTime().ToString("HH:mm") + " · выбрано " + Fmt.Size(chosen);
            else line = "Проверка только смотрит и ничего не удаляет";
            Motion.Swap(footer, line);
            footer.Foreground = Notice != null ? Ui.Solid(Tokens.Palette.Red) : Ui.Label3;
            coreLink.Text = CoreApp.Label(DemoMode || AppHub.Online);
        }

        // Шаг проверки или очистки: меняются только полоса хода на «Обзоре» и строка состояния
        void Tick()
        {
            if (page == POverview) overview.Tick();
            Footer();
        }

        // Открыть категорию на странице «Очистка» — раскрытой и на виду
        internal void OpenCategory(string id)
        {
            ShowPage(PClean);
            clean.Expand(id);
            clean.Reveal(id);
        }

        void ReadDisk()
        {
            try
            {
                var d = new System.IO.DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory));
                DiskName = d.Name.TrimEnd('\\'); DiskTotal = d.TotalSize; DiskFree = d.AvailableFreeSpace;
            }
            catch { }
        }

        // ───────── Проверка: только чтение ─────────
        // Проверка идёт в новые объекты категорий; готовый список подменяется здесь, в потоке окна
        void Swap(List<Category> fresh)
        {
            foreach (Category c in fresh)
            {
                Category was = Scanner.Find(Cats, c.Id);
                if (was != null) c.On = was.On;
            }
            Cats = fresh;
            CatsVersion++;
        }

        internal async void StartScan()
        {
            if (Busy) return;
            Notice = null;
            Last = null;
            if (DemoMode) { Demo.Scan(this); FinishScan(true); return; }
            Busy = true;
            BusyText = "Проверяю…";
            RenderAll();
            bool ok = false;
            try
            {
                List<Category> old = Cats;
                Env env = Env.Real();
                List<Category> fresh = await Task.Run(delegate
                {
                    return Scanner.ScanFresh(old, env, delegate(Category c)
                    {
                        string title = c.Title;
                        Dispatcher.BeginInvoke(new Action(delegate { if (!Busy || Cleaning) return; BusyText = "Проверяю: " + title.ToLowerInvariant() + "…"; Tick(); }));
                    });
                });
                Swap(fresh);
                ReadDisk();
                ok = true;
            }
            catch (Exception e) { Notice = "Проверка не удалась: " + e.Message; }
            finally { Busy = false; BusyText = null; }
            if (ok) FinishScan(true); else RenderAll();
        }

        void FinishScan(bool animate)
        {
            Scanned = true;
            ScanAt = DateTime.UtcNow;
            AppHub.Found(Scanner.Total(Cats), ScanAt);
            if (animate) overview.CountUp();
            RenderAll();
        }

        // ───────── Очистка: только после вопроса с точными числами ─────────
        void AskClean()
        {
            if (Busy || !Scanned) return;
            // Корзина могла измениться после проверки — в вопросе должны стоять числа на эту секунду
            Category bin = Scanner.Find(Cats, "recycle");
            if (!DemoMode && bin != null && bin.Chosen) { Scanner.Scan(bin, Env.Real()); RenderAll(); }

            // Снимок выбора: в листе показано ровно это, и удалено будет ровно это — что бы ни стало с переключателями потом
            List<Category> cats = Cats;
            HashSet<string> ids = Scanner.Snapshot(cats);
            long total = 0, files = 0;
            var body = new StackPanel();
            bool anyPermanent = false, recycle = false;
            foreach (Category c in cats)
            {
                if (!ids.Contains(c.Id)) continue;
                total += c.Bytes; files += c.Count;
                if (c.IsRecycle) recycle = true; else anyPermanent = true;
                body.Children.Add(SheetLine(c.Title, c.IsRecycle ? Fmt.Plural(c.Count, "объект", "объекта", "объектов") : Fmt.Files(c.Count), Fmt.Size(c.Bytes), false));
            }
            if (ids.Count == 0 || total + files == 0) { RenderAll(); return; }
            body.Children.Add(new Border { Height = 1, Background = Ui.Separator, Margin = new Thickness(0, 8, 0, 8) });
            body.Children.Add(SheetLine("Всего", Fmt.Plural(files, "объект", "объекта", "объектов"), Fmt.Size(total), true));
            string warn = "";
            if (anyPermanent) warn += "Временные файлы и кэши удаляются сразу и навсегда, мимо корзины. ";
            if (recycle) warn += "Корзина будет очищена целиком — вернуть из неё уже ничего не получится. ";
            warn += "Файлы, которые сейчас заняты или защищены, останутся на месте.";
            var note = Ui.T(warn, Tokens.Size.Caption, Ui.Label2, FontWeights.Normal);
            note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(0, 14, 0, 0);
            body.Children.Add(note);
            OpenSheet("Удалить выбранное?", body, "Удалить " + Fmt.Size(total), Tokens.Palette.Red, delegate { DoClean(cats, ids); });
        }

        static FrameworkElement SheetLine(string name, string count, string size, bool strong)
        {
            var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 3, 0, 3) };
            var s = Ui.T(size, Tokens.Size.Body, Ui.Label, strong ? FontWeights.SemiBold : FontWeights.Normal);
            s.MinWidth = 76; s.TextAlignment = TextAlignment.Right;
            DockPanel.SetDock(s, Dock.Right);
            line.Children.Add(s);
            var n = Ui.T(count, Tokens.Size.Caption, Ui.Label3, FontWeights.Normal);
            n.MinWidth = 110; n.TextAlignment = TextAlignment.Right; n.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(n, Dock.Right);
            line.Children.Add(n);
            line.Children.Add(Ui.T(name, Tokens.Size.Body, Ui.Label, strong ? FontWeights.SemiBold : FontWeights.Normal));
            return line;
        }

        // cats и ids — снимок, сделанный при открытии листа: чистится он, а не то, что выбрано сейчас
        async void DoClean(List<Category> cats, HashSet<string> ids)
        {
            if (Busy) return;
            Notice = null;
            if (DemoMode) { Last = Demo.Clean(this, ids); FinishScan(false); return; }
            Busy = true;
            Cleaning = true;
            CleanDone = 0; CleanTotal = 0;
            foreach (Category c in cats) if (ids.Contains(c.Id)) CleanTotal += c.Count;
            RenderAll();   // переключатели гаснут: выбор заморожен до конца работы
            CleanResult r = null;
            bool ok = false;
            try
            {
                Env env = Env.Real();
                try
                {
                    r = await Task.Run(delegate
                    {
                        return Cleaner.Clean(cats, ids, env, delegate(long done, long total)
                        {
                            Dispatcher.BeginInvoke(new Action(delegate { if (!Cleaning) return; CleanDone = done; CleanTotal = total; Tick(); }));
                        });
                    });
                }
                catch (Exception e) { Notice = "Очистка остановилась: " + e.Message; }
                // После очистки — новая проверка: числа на экране должны быть настоящими
                Cleaning = false;
                BusyText = "Проверяю, что осталось…";
                Tick();
                List<Category> old = Cats;
                Env again = Env.Real();
                List<Category> fresh = await Task.Run(delegate { return Scanner.ScanFresh(old, again, null); });
                Swap(fresh);
                ReadDisk();
                ok = true;
            }
            catch (Exception e) { if (Notice == null) Notice = "Проверка после очистки не удалась: " + e.Message; }
            finally { Busy = false; Cleaning = false; BusyText = null; }
            Last = r;
            if (ok) FinishScan(false); else RenderAll();
        }

        // ───────── Лист с вопросом поверх окна ─────────
        internal void OpenSheet(string title, FrameworkElement body, string confirm, Color tint, Action onConfirm)
        {
            var head = Ui.T(title, Tokens.Size.Title, Ui.Label, FontWeights.SemiBold);
            head.TextWrapping = TextWrapping.Wrap;
            head.Margin = new Thickness(0, 0, 0, 12);
            var yes = new Btn();
            yes.Set(confirm, tint, true, delegate { CloseSheet(); onConfirm(); });
            yes.Root.MinWidth = 140;
            var no = new Btn();
            no.Set("Отмена", null, true, CloseSheet);
            no.Root.Margin = new Thickness(0, 0, 8, 0);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), Children = { no.Root, yes.Root } };
            var glass = Ui.Block(new StackPanel { Children = { head, body, buttons } }, false);
            glass.Padding = new Thickness(22, 20, 22, 18);
            var card = new Border { Width = 520, CornerRadius = new CornerRadius(Tokens.Radius.Block), Background = Ui.Light ? Ui.Solid(Tokens.Light.Raised) : Ui.Solid(Tokens.Dark.Surface), Child = glass, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16) };   // audit:ok — цвет выбран по теме
            // Tab ходит по кругу внутри листа
            KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetControlTabNavigation(card, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetDirectionalNavigation(card, KeyboardNavigationMode.Cycle);
            var dim = new Border { Background = Ui.Solid(Ui.Alpha(Colors.Black, 0.4)) };   // «дымка» Windows 11 под диалогом — всегда полупрозрачный чёрный
            dim.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };   // щелчок мимо листа ничего не делает
            int token = ++sheetToken;
            if (!sheetOpen) sheetReturn = Keyboard.FocusedElement;
            sheetOpen = true;
            sheetCancel = no;
            sheetCard = card;
            sheetLayer.BeginAnimation(UIElement.OpacityProperty, null);
            sheetLayer.Opacity = 1;
            sheetLayer.IsHitTestVisible = true;
            sheetLayer.Children.Clear();
            sheetLayer.Children.Add(dim);
            sheetLayer.Children.Add(card);
            sheetLayer.Visibility = Visibility.Visible;
            if (IsVisible) Motion.GrowFrom(card, new Point(0.5, 0.5));
            // Фокус — на «Отмена»: случайный Enter ничего не удалит
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate { if (sheetOpen && token == sheetToken) Keyboard.Focus(no.Root); }));
        }

        void CloseSheet()
        {
            if (!sheetOpen) return;
            sheetOpen = false;
            sheetCancel = null;
            sheetCard = null;
            int token = ++sheetToken;
            IInputElement back = sheetReturn;
            sheetReturn = null;
            if (back != null) Keyboard.Focus(back); else Keyboard.ClearFocus();
            Action done = delegate
            {
                if (token != sheetToken) return;   // за это время открыли другой лист
                sheetLayer.Visibility = Visibility.Collapsed;
                sheetLayer.Children.Clear();
                sheetLayer.BeginAnimation(UIElement.OpacityProperty, null);
                sheetLayer.Opacity = 1;
                sheetLayer.IsHitTestVisible = true;
            };
            if (!Ui.Live(sheetLayer)) { done(); return; }
            // Лист растворяется и только потом убирается; щелчки в это время уже идут в окно
            sheetLayer.IsHitTestVisible = false;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(Tokens.Motion.FastMs)) { EasingFunction = Ui.EaseOut };
            fade.Completed += delegate { done(); };
            sheetLayer.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        // ───────── Автозапуск и программы ─────────
        internal async void LoadStartup()
        {
            if (LoadingStartup) return;
            if (DemoMode) { RenderAll(); return; }
            LoadingStartup = true;
            Notice = null;
            RenderAll();
            try
            {
                // Список и значки читаются в фоне; в окне остаётся только показать
                List<StartupEntry> list = await Task.Run(delegate
                {
                    List<StartupEntry> read = Startup.Read();
                    foreach (StartupEntry e in read) e.Icon = Icons.Of(e.Exe);
                    return read;
                });
                StartupItems = list;
            }
            catch (Exception e) { Notice = "Не удалось прочитать автозапуск: " + e.Message; }
            finally { LoadingStartup = false; }
            RenderAll();
            if (page == PStartup) Enter();
        }

        // core.txt установлен (в --demo — будто бы да): приложения dev.txt включает в автозапуск и удаляет он
        internal bool CoreInstalled { get { return DemoMode || CoreApp.Installed; } }

        // У приложений dev.txt здесь нет ни переключателя, ни «Удалить», пока есть core.txt: одна вещь — одно место.
        // Без core.txt они такие же, как остальные программы.
        internal static bool CanToggle(StartupEntry e, bool coreInstalled) { return e.Editable && !(coreInstalled && e.Family); }
        internal static bool CanUninstall(Prog p, bool coreInstalled) { return !(coreInstalled && p.Family); }

        // В списке есть строки, которыми ведает core.txt, — об этом говорит и подпись внизу
        bool ManagedStartup { get { bool core = CoreInstalled; return core && StartupItems != null && StartupItems.Exists(delegate(StartupEntry e) { return e.Family; }); } }
        bool ManagedProgs { get { bool core = CoreInstalled; return Progs != null && Progs.Exists(delegate(Prog p) { return !CanUninstall(p, core); }); } }

        // Открыть core.txt на нужной странице («general», «apps»): запущенный — просьбой через хаб; установленный, но не
        // запущенный — запуском сразу на этой странице (core.txt понимает --page, незнакомую страницу пропускает).
        // core.txt успели удалить — строки перестраиваются: у приложений dev.txt снова обычный переключатель и «Удалить».
        internal void OpenCore(string corePage)
        {
            if (DemoMode) return;
            if (AppHub.Online) AppHub.ShowCore(corePage);
            else if (CoreApp.Installed)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(CoreApp.Path, "--page " + corePage) { UseShellExecute = true }); Notice = null; }
                catch (Exception x) { Notice = "Не удалось открыть core.txt: " + x.Message; }
            }
            RenderAll();
        }

        internal void ToggleStartup(StartupEntry e, bool on)
        {
            Notice = null;
            if (e.Family && CoreInstalled) { RenderAll(); return; }   // им ведает core.txt: строка, построенная до его установки, перестроится без переключателя
            if (DemoMode) { e.Enabled = on; RenderAll(); return; }
            try { Startup.Set(e, on); }
            catch (Exception x) { Notice = "Не получилось изменить автозапуск: " + x.Message; }
            RenderAll();   // после ошибки переключатель возвращается назад
        }

        internal async void LoadPrograms()
        {
            if (LoadingProgs) return;
            if (DemoMode) { RenderAll(); return; }
            LoadingProgs = true;
            Notice = null;
            RenderAll();
            List<Prog> list = null;
            bool first = Progs == null;
            try
            {
                list = await Task.Run(delegate { return Programs.Read(); });
                // Программа, которую удаляли, исчезла из списка — больше перечитывать при возвращении в окно не нужно
                Prog gone = pendingUninstall;
                if (gone != null && !list.Exists(delegate(Prog p) { return p.Name == gone.Name && p.Version == gone.Version; })) pendingUninstall = null;
                Progs = list;
            }
            catch (Exception e) { Notice = "Не удалось прочитать список программ: " + e.Message; list = null; }
            finally { LoadingProgs = false; }
            RenderAll();
            if (page == PPrograms && first) Enter();
            if (list == null) return;
            // Значки — после списка и тоже в фоне: список не ждёт их
            try
            {
                await Task.Run(delegate { foreach (Prog p in list) p.Icon = Icons.Of(p.IconPath); });
                if (Progs == list) programs.IconsReady();
            }
            catch { }
        }

        internal void AskUninstall(Prog p)
        {
            if (!CanUninstall(p, CoreInstalled)) { OpenCore("apps"); return; }   // приложение dev.txt удаляет core.txt
            var text = Ui.T("Откроется собственная программа удаления «" + p.Name + "» — дальше всё происходит в её окне. metla.txt сама файлы программ не трогает. Windows может спросить разрешение, если программе удаления нужны права администратора.", Tokens.Size.Body, Ui.Label2, FontWeights.Normal);
            text.TextWrapping = TextWrapping.Wrap;
            OpenSheet("Удалить «" + p.Name + "»?", text, "Запустить удаление", Tokens.Palette.Red, delegate
            {
                Notice = null;
                if (!DemoMode)
                {
                    try { Programs.Launch(p); pendingUninstall = p; pendingSince = DateTime.UtcNow; }
                    catch (Exception x) { Notice = "Не удалось запустить программу удаления: " + x.Message; }
                }
                RenderAll();
            });
        }

        internal void OpenFolder(string dir)
        {
            if (DemoMode || string.IsNullOrEmpty(dir)) return;
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\""); Notice = null; }
            catch (Exception x) { Notice = "Не удалось открыть папку: " + x.Message; }
            RenderAll();
        }

        // Окно Windows «Защита системы»: точки восстановления создаются и настраиваются там, не здесь
        internal void OpenProtection()
        {
            if (DemoMode) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("SystemPropertiesProtection.exe") { UseShellExecute = true }); Notice = null; }
            catch (Exception x) { Notice = "Не удалось открыть «Защиту системы»: " + x.Message; }
            RenderAll();
        }

        // ───────── Трей и ежедневное напоминание ─────────
        // Свой значок есть, пока включено напоминание; виден — только когда общий значок семьи не у core.txt
        void SyncTray()
        {
            if (Prefs.Remind && tray == null)
            {
                try
                {
                    tray = new System.Windows.Forms.NotifyIcon { Icon = TrayIcon(), Text = "metla.txt", Visible = false };
                    // Меню Windows 11, а не серое меню WinForms
                    tray.MouseUp += delegate(object s, System.Windows.Forms.MouseEventArgs e)
                    {
                        if (e.Button != System.Windows.Forms.MouseButtons.Right) return;
                        var menu = new PopMenu();
                        FillTrayMenu(menu);
                        menu.ShowAtCursor();
                    };
                    tray.MouseClick += delegate(object s, System.Windows.Forms.MouseEventArgs e) { if (e.Button == System.Windows.Forms.MouseButtons.Left) ShowWindow(); };
                    // Уведомление закрыли или по нему щёлкнули: значок, показанный только ради него, снова прячется
                    tray.BalloonTipClicked += delegate { ShowWindow(); EndNotice(); };
                    tray.BalloonTipClosed += delegate { EndNotice(); };
                }
                catch { tray = null; }
            }
            else if (!Prefs.Remind && tray != null)
            {
                DropTray();
                if (!IsVisible) { quitting = true; Close(); }   // напоминание выключили из core.txt, а окна нет — в фоне делать нечего
                return;
            }
            if (tray == null) return;
            bool show = OwnTray(Prefs.Remind, AppHub.HideOwnTray, AppHub.CoreOnline, trayHold.Waiting, trayNotice);
            try { if (tray.Visible != show) tray.Visible = show; } catch { }
            if (show) TryRemind();   // значок вернулся (core.txt ушёл) — отложенное напоминание может показаться
        }

        // Виден ли свой значок. Только пока включено напоминание и только если общий значок не у core.txt (coreHolds);
        // в первые секунды после запуска (starting) ждём слова core.txt, но не дольше: без входа приложение не остаётся.
        // notice — идёт своё уведомление Windows: его показывает только видимый значок, поэтому на это время он виден всегда.
        internal static bool OwnTray(bool remind, bool coreHolds, bool coreHeard, bool starting, bool notice)
        {
            if (!remind) return false;
            if (notice) return true;
            if (coreHolds) return false;
            return !starting || coreHeard;
        }

        // Меню своего значка: «Открыть», «Проверить диск» (как в общем меню core.txt), дорога к core.txt и «Выход»
        void FillTrayMenu(PopMenu menu)
        {
            menu.Brand(Sprite, Program.Id);
            menu.Item("Открыть metla.txt", false, ShowWindow);
            menu.Item("Проверить диск", false, Busy ? null : new Action(ScanFromTray));
            menu.Line();
            menu.Item(CoreApp.Label(DemoMode || AppHub.Online), false, delegate { if (!DemoMode) CoreApp.Go(AppHub.Online, AppHub.ShowCore); });
            menu.Line();
            menu.Item("Выход", false, QuitApp);
        }

        // Пункты metla.txt в общем меню трея core.txt — самое частое: открыть окно и проверить диск (только чтение).
        // Очистки отсюда нет и не будет: удаляется только из окна, после вопроса с точными числами.
        internal static List<TrayItem> TrayMenu(bool busy, Action show, Action scan)
        {
            return new List<TrayItem>
            {
                new TrayItem("show", "Открыть", null, show),
                new TrayItem("scan", "Проверить диск", null, !busy, scan)
            };
        }

        // Пункты выполняются в потоке хаба — к окну только через Dispatcher
        internal IList<TrayItem> TrayItems()
        {
            return TrayMenu(Busy, delegate { Dispatcher.BeginInvoke(new Action(ShowWindow)); }, delegate { Dispatcher.BeginInvoke(new Action(ScanFromTray)); });
        }

        // «Проверить диск» из меню трея: окно на «Обзоре» и та же проверка только для чтения, что и по кнопке «Проверить».
        // Пока открыт вопрос перед очисткой, числа под ним не меняются — тогда только показываем окно.
        void ScanFromTray()
        {
            ShowWindow();
            if (Busy || sheetOpen) return;
            if (page != POverview) ShowPage(POverview);
            StartScan();
        }

        void DropTray()
        {
            noticeEnd.Stop();
            trayNotice = false;
            if (tray == null) return;
            try { tray.Visible = false; tray.Dispose(); } catch { }
            tray = null;
        }

        // Значок для трея рисуется из того же рисунка, что и значок в шапке
        static System.Drawing.Icon TrayIcon()
        {
            const int size = 32;
            int cols = Sprite[0].Length, rows = Sprite.Length, cell = size / Math.Max(cols, rows);
            int ox = (size - cell * cols) / 2, oy = (size - cell * rows) / 2;
            Color body = Tokens.Palette.Brand;
            using (var bmp = new System.Drawing.Bitmap(size, size))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                using (var orange = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(body.R, body.G, body.B)))
                using (var eye = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(Tokens.Dark.Bg.R, Tokens.Dark.Bg.G, Tokens.Dark.Bg.B)))   // audit:ok — глаз значка в трее всегда тёмный
                    for (int y = 0; y < rows; y++)
                        for (int x = 0; x < cols; x++)
                            if (Sprite[y][x] != '.') g.FillRectangle(Sprite[y][x] == 'E' ? eye : orange, ox + x * cell, oy + y * cell, cell, cell);
                IntPtr h = bmp.GetHicon();
                try { return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone(); }
                finally { DestroyIcon(h); }
            }
        }

        // Раз в день, пока включено напоминание: та же проверка только для чтения
        async void DailyCheck()
        {
            if (!Prefs.Remind || Busy || sheetOpen) return;
            if (DateTime.UtcNow - Prefs.LastAuto < TimeSpan.FromHours(24)) return;
            Prefs.LastAuto = DateTime.UtcNow;
            Prefs.Save();
            Busy = true;
            BusyText = "Ежедневная проверка…";
            RenderAll();
            bool ok = false;
            try
            {
                List<Category> old = Cats;
                Env env = Env.Real();
                List<Category> fresh = await Task.Run(delegate { return Scanner.ScanFresh(old, env, null); });
                Swap(fresh);
                ReadDisk();
                ok = true;
            }
            catch (Exception e) { Notice = "Ежедневная проверка не удалась: " + e.Message; }
            finally { Busy = false; BusyText = null; }
            if (!ok) { RenderAll(); return; }
            Notice = null;
            Last = null;
            FinishScan(false);
            if (Scanner.Total(Cats) > RemindBytes && !IsVisible) { remindPending = true; TryRemind(); }
        }

        // Напоминание «Можно освободить …»: сначала на остров kapsula.txt, без него — своим уведомлением Windows.
        // Пока включено «Не беспокоить» или впереди игра, оно ждёт: пробует раз в минуту и при каждой вести от core.txt.
        // Общий значок у core.txt, а острова нет — свой значок появляется только на время уведомления и снова прячется.
        void TryRemind()
        {
            if (!remindPending) return;
            long total = Scanner.Total(Cats);
            bool quiet = Reminder.Quiet(AppHub.CoreOnline, AppHub.Focus, AppHub.Game, FullScreen.Ahead);
            RemindWay way = Reminder.Way(IsVisible, Prefs.Remind && total > RemindBytes, quiet, AppHub.IslandOnline, tray != null);
            string text = "Можно освободить " + Fmt.Size(total) + ". Откройте окно, чтобы посмотреть, что именно.";
            if (way == RemindWay.Island && !AppHub.Notify(text)) way = RemindWay.Wait;   // остров ушёл в эту самую секунду
            if (way == RemindWay.Balloon && !Balloon(text)) way = RemindWay.Wait;
            if (way == RemindWay.Wait) { if (!remindRetry.IsEnabled) remindRetry.Start(); return; }
            remindPending = false;   // показано (остров сам решает, показать сразу или тихо положить в историю) или уже не нужно
            remindRetry.Stop();
        }

        // Своё уведомление Windows. Его показывает только видимый значок: пока общий значок у core.txt, свой появляется
        // на время уведомления и прячется, когда Windows скажет, что оно закрыто, а если не скажет — через минуту.
        bool Balloon(string text)
        {
            if (tray == null) return false;
            try
            {
                if (!tray.Visible) { trayNotice = true; noticeEnd.Stop(); noticeEnd.Start(); tray.Visible = true; }
                tray.ShowBalloonTip(8000, "metla.txt", text, System.Windows.Forms.ToolTipIcon.None);
                return true;
            }
            catch { EndNotice(); return false; }
        }

        // Уведомление закрылось: значок, показанный только ради него, снова прячется (если общий по-прежнему у core.txt)
        void EndNotice()
        {
            noticeEnd.Stop();
            if (!trayNotice) return;
            trayNotice = false;
            SyncTray();
        }

        // Выход: «Выход» в своём трее или «Закрыть» в трее core.txt
        public void QuitApp() { quitting = true; Close(); }

        public void ShowWindow()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        // Выдуманное состояние для снимка (только вместе с --demo): scan | clean | done | loading
        void DemoState(string state)
        {
            if (!DemoMode || state == null) return;
            if (state == "scan") { Scanned = false; Busy = true; BusyText = "Проверяю: кэш браузеров…"; }
            else if (state == "clean") { Busy = true; Cleaning = true; CleanDone = 2816; CleanTotal = 4264; }
            else if (state == "done") Last = Demo.Clean(this, Scanner.Snapshot(Cats));
            else if (state == "loading") { StartupItems = null; Progs = null; LoadingStartup = true; LoadingProgs = true; }
        }

        // Меню своего значка в трее — в файл PNG, не показывая его на экране (--shot вместе с --menu)
        public void MenuShot(string file, string state)
        {
            DemoState(state);
            var menu = new PopMenu();
            FillTrayMenu(menu);
            var box = (FrameworkElement)menu.Content;
            menu.Content = null;
            var stage = new Border { Background = Ui.Bg, Padding = new Thickness(12), Child = box };
            stage.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            stage.Arrange(new Rect(stage.DesiredSize));
            stage.UpdateLayout();
            Save(stage, file, (int)Math.Ceiling(stage.ActualWidth * 1.5), (int)Math.Ceiling(stage.ActualHeight * 1.5));
        }

        static void Save(FrameworkElement root, string file, int width, int height)
        {
            var bmp = new RenderTargetBitmap(width, height, 144, 144, PixelFormats.Pbgra32);
            bmp.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = System.IO.File.Create(file)) enc.Save(f);
        }

        // Рисует окно в файл PNG, не показывая его на экране. state — выдуманное состояние для снимка (см. DemoState)
        public void Shot(string file, bool sheet, string expand, double scroll, string state)
        {
            DemoState(state);
            if (expand != null) clean.Expand(expand);
            var root = (FrameworkElement)Content;
            Content = null;
            RenderAll();
            if (sheet) AskClean();
            root.Measure(new Size(W, H));
            root.Arrange(new Rect(0, 0, W, H));
            root.UpdateLayout();
            if (scroll > 0)
            {
                ScrollViewer view = page == POverview ? overview.Root : page == PClean ? clean.Root : page == PStartup ? startup.Root : programs.Scroll;
                view.ScrollToVerticalOffset(scroll);
                root.UpdateLayout();
            }
            Save(root, file, (int)(W * 1.5), (int)(H * 1.5));
        }
    }

    enum RemindWay { Drop, Wait, Island, Balloon }

    // Когда и как показывать напоминание — без окон и без хаба, проверяется в --selftest
    static class Reminder
    {
        // Сейчас не до напоминаний: «Не беспокоить» или впереди игра. С core.txt — по его общему правилу (core.family);
        // без него — своя проверка окна на весь экран (ahead зовётся только тогда).
        public static bool Quiet(bool coreOnline, bool focus, bool game, Func<bool> ahead)
        {
            if (coreOnline) return focus || game;
            return ahead != null && ahead();
        }

        // windowVisible — окно и так открыто; wanted — напоминание включено и есть что освободить;
        // island — остров kapsula.txt принимает уведомления; icon — есть свой значок, от которого Windows покажет
        // уведомление (виден он или спрятан, пока общий значок у core.txt: на время уведомления он появится)
        public static RemindWay Way(bool windowVisible, bool wanted, bool quiet, bool island, bool icon)
        {
            if (!wanted || windowVisible) return RemindWay.Drop;
            if (quiet) return RemindWay.Wait;
            if (island) return RemindWay.Island;
            if (icon) return RemindWay.Balloon;
            return RemindWay.Wait;   // своего значка нет совсем (не удалось создать): ждём острова
        }
    }

    // Ожидание слова core.txt сразу после запуска: свой значок не должен мелькнуть, пока не ясно, у кого общий значок.
    // Кончается только по своему таймеру или по Stop (связь пропала), а не по сравнению часов: DispatcherTimer считает
    // по Environment.TickCount и бывает, что срабатывает на несколько миллисекунд раньше срока по DateTime.UtcNow.
    // Тогда ожидание не кончилось бы никогда, и без запущенного core.txt своего значка не было бы совсем.
    class TrayHold
    {
        readonly DispatcherTimer timer;
        bool waiting;
        public Action Over;   // время вышло: пора решить, показывать ли свой значок

        public TrayHold(TimeSpan wait)
        {
            timer = new DispatcherTimer { Interval = wait };
            timer.Tick += delegate { Stop(); if (Over != null) Over(); };
        }

        public bool Waiting { get { return waiting; } }

        public void Start() { waiting = true; timer.Stop(); timer.Start(); }

        // Ждать больше нечего. Over при этом не зовётся: кто остановил, тот сам и решает про значок.
        public void Stop() { timer.Stop(); waiting = false; }
    }
}

// metla.txt. MainWindow.cs — окно: шапка с вкладками, четыре страницы, строка состояния с главной кнопкой
// и лист с вопросом поверх окна. Здесь же проверка, очистка и напоминание из трея.
// Всё собрано из общих элементов (Ui.cs) и токенов (Tokens.cs).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DevTxt.Metla
{
    class MainWindow : Window
    {
        const double W = 920, H = 640;
        internal const int POverview = 0, PClean = 1, PStartup = 2, PPrograms = 3;
        internal const long RemindBytes = 5 * Fmt.GB;
        // Значок metla.txt — метла: черенок, головка с глазами, прутья. Тот же рисунок — в sprite.json.
        internal static readonly string[] Sprite = { ".......##.", "......##..", ".....##...", "....##....", "...##.....", "..####....", ".######...", ".##E###...", "###E####..", "########..", "#.#.#.#.#.", "#.#.#.#.#." };

        internal readonly bool DemoMode;
        internal List<Category> Cats = Scanner.Categories();
        internal bool Scanned, Busy;
        internal string BusyText;
        internal DateTime ScanAt;
        internal CleanResult Last;                 // итог последней очистки — для строки состояния
        internal long DiskTotal, DiskFree;
        internal string DiskName = "C:";
        internal List<StartupEntry> StartupItems;  // null — ещё не читали
        internal List<Prog> Progs;
        internal string Notice;                    // последняя ошибка — показывается в строке состояния

        readonly TextBlock footer = Ui.T("", Tokens.Size.Caption, Ui.Label3, FontWeights.Normal);
        readonly Btn mainBtn = new Btn(), secondBtn = new Btn();
        readonly TextBlock coreLink = Ui.T("", Tokens.Size.Caption, Ui.Label2, FontWeights.Normal);
        readonly Grid sheetLayer = new Grid { Visibility = Visibility.Collapsed };
        readonly DispatcherTimer daily = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        OverviewPage overview;
        CleanPage clean;
        StartupPage startup;
        ProgramsPage programs;
        Seg tabs;
        int page;
        bool quitting, loadingStartup, loadingProgs;
        System.Windows.Forms.NotifyIcon tray;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr icon);

        public MainWindow(bool demo, int startPage)
        {
            DemoMode = demo;
            page = startPage;
            Title = Program.Id + ".txt";
            Width = W; Height = H;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanMinimize;
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

            SourceInitialized += delegate
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int round = 2, dark = 1;   // скруглённые углы и тёмная рамка окна в Windows 11
                DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
                DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            };
            Loaded += delegate { Enter(); };
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape && sheetLayer.Visibility == Visibility.Visible) CloseSheet(); };
            if (demo) return;

            // Напоминание: приложение остаётся в трее, только пока включена эта настройка
            Prefs.Changed += delegate { SyncTray(); RenderAll(); };
            SyncTray();
            daily.Tick += delegate { DailyCheck(); };
            daily.Start();
            Closing += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                if (!quitting && Prefs.Remind && tray != null) { e.Cancel = true; Hide(); return; }
                DropTray();
            };
        }

        // ───────── Разметка ─────────
        FrameworkElement Build()
        {
            var root = new Grid { Background = Ui.Bg, Width = W, Height = H, ClipToBounds = true };
            root.Children.Add(Ui.Backdrop());
            var layout = new DockPanel { LastChildFill = true };
            root.Children.Add(layout);

            // Шапка: за неё окно перетаскивается
            var header = new Grid { Height = 92, Background = Brushes.Transparent };
            header.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };
            var mark = Ui.Sprite(Sprite, 40);
            mark.VerticalAlignment = VerticalAlignment.Center;
            var title = Ui.Brand(Program.Id, Tokens.Size.Display, FontWeights.Bold);
            title.Margin = new Thickness(10, 0, 0, 2); title.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(32, 22, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { mark, title } });

            tabs = new Seg(new string[] { "Обзор", "Очистка", "Автозапуск", "Программы" }, page, delegate(int i) { ShowPage(i); });
            tabs.Root.HorizontalAlignment = HorizontalAlignment.Right; tabs.Root.VerticalAlignment = VerticalAlignment.Center;
            tabs.Root.Margin = new Thickness(0, 24, 32, 0);
            header.Children.Add(tabs.Root);

            var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 8, 0) };
            controls.Children.Add(WindowButton("", "Свернуть", false, delegate { WindowState = WindowState.Minimized; }));
            controls.Children.Add(WindowButton("", "Закрыть", true, delegate { Close(); }));
            header.Children.Add(controls);
            DockPanel.SetDock(header, Dock.Top);
            layout.Children.Add(header);

            // Низ: строка состояния и кнопки
            var bar = new DockPanel { Height = 64, Margin = new Thickness(32, 0, 32, 6) };
            mainBtn.Root.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(mainBtn.Root, Dock.Right);
            bar.Children.Add(mainBtn.Root);
            secondBtn.Root.VerticalAlignment = VerticalAlignment.Center;
            secondBtn.Root.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(secondBtn.Root, Dock.Right);
            bar.Children.Add(secondBtn.Root);
            // Дорога к core.txt: тихая ссылка — открыть, а если его нет — скачать
            coreLink.VerticalAlignment = VerticalAlignment.Center; coreLink.Margin = new Thickness(0, 0, 16, 0);
            coreLink.Cursor = Cursors.Hand; coreLink.Background = Brushes.Transparent; coreLink.ToolTip = CoreApp.Pitch;
            coreLink.MouseEnter += delegate { coreLink.Foreground = Ui.Label; };
            coreLink.MouseLeave += delegate { coreLink.Foreground = Ui.Label2; };
            coreLink.MouseLeftButtonUp += delegate { if (!DemoMode) CoreApp.Go(AppHub.Online, AppHub.ShowCore); };
            DockPanel.SetDock(coreLink, Dock.Right);
            bar.Children.Add(coreLink);
            footer.VerticalAlignment = VerticalAlignment.Center;
            footer.Margin = new Thickness(0, 0, 16, 0);
            bar.Children.Add(footer);
            DockPanel.SetDock(bar, Dock.Bottom);
            layout.Children.Add(bar);

            layout.Children.Add(new Grid { Children = { overview.Root, clean.Root, startup.Root, programs.Root } });
            root.Children.Add(sheetLayer);
            return root;
        }

        FrameworkElement WindowButton(string glyph, string tip, bool danger, Action click)
        {
            var t = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = Tokens.Size.Micro, Foreground = Ui.Label2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { Width = 36, Height = 26, CornerRadius = new CornerRadius(Tokens.Radius.Tip), Background = Brushes.Transparent, Child = t, ToolTip = tip };
            b.MouseEnter += delegate { b.Background = danger ? Ui.Solid(Tokens.Palette.Red) : Ui.Fill2; t.Foreground = Ui.Label; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; t.Foreground = Ui.Label2; };
            b.MouseLeftButtonUp += delegate { click(); };
            return b;
        }

        internal void ShowPage(int i)
        {
            page = i;
            tabs.Index = i;
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
            Motion.Stagger(p.Entrance);
        }

        internal void RenderAll()
        {
            if (page == POverview) overview.Render();
            else if (page == PClean) clean.Render();
            else if (page == PStartup) startup.Render();
            else programs.Render();

            long files;
            long chosen = Scanner.Chosen(Cats, out files);
            string line;
            if (Notice != null) line = Notice;
            else if (Busy) line = BusyText ?? "Работаю…";
            else if (page == PStartup) line = StartupItems == null ? "Читаю список…" : "Отключённое можно включить обратно здесь или в Диспетчере задач";
            else if (page == PPrograms) line = Progs == null ? "Читаю список…" : "Программ: " + Fmt.Count(Progs.Count) + " · каждую удаляет её собственная программа удаления";
            else if (Last != null) line = "Освобождено " + Fmt.Size(Last.Freed) + " · удалено: " + Fmt.Count(Last.Deleted) + (Last.Skipped + Last.Refused > 0 ? " · пропущено: " + Fmt.Count(Last.Skipped + Last.Refused) + " (заняты или нет доступа)" : "");
            else if (Scanned) line = "Проверено в " + ScanAt.ToLocalTime().ToString("HH:mm") + " · выбрано " + Fmt.Size(chosen);
            else line = "Проверка только смотрит и ничего не удаляет";
            Motion.Swap(footer, line + " · версия " + Program.VersionText + " · dev.txt™");
            coreLink.Text = CoreApp.Label(DemoMode || AppHub.Online);
            footer.Foreground = Notice != null ? Ui.Solid(Tokens.Palette.Red) : Ui.Label3;

            if (page == PStartup) { secondBtn.Hide(); mainBtn.Set("Обновить список", null, !loadingStartup, LoadStartup); }
            else if (page == PPrograms) { secondBtn.Hide(); mainBtn.Set("Обновить список", null, !loadingProgs, LoadPrograms); }
            else if (!Scanned) { secondBtn.Hide(); mainBtn.Set(Busy ? "Проверяю…" : "Проверить", Tokens.Palette.Brand, !Busy, StartScan); }
            else
            {
                secondBtn.Set("Проверить снова", null, !Busy, StartScan);
                mainBtn.Set("Очистить выбранное", Tokens.Palette.Brand, !Busy && chosen + files > 0, AskClean);
            }
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
        internal async void StartScan()
        {
            if (Busy) return;
            Notice = null;
            Last = null;
            if (DemoMode) { Demo.Scan(this); FinishScan(true); return; }
            Busy = true;
            BusyText = "Проверяю…";
            RenderAll();
            var cats = Cats;
            await Task.Run(delegate
            {
                Scanner.ScanAll(cats, Env.Real(), delegate(Category c) { Dispatcher.BeginInvoke(new Action(delegate { BusyText = "Проверяю: " + c.Title.ToLowerInvariant() + "…"; if (Busy) RenderAll(); })); });
            });
            ReadDisk();
            Busy = false;
            FinishScan(true);
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
            if (!DemoMode && bin != null && bin.Chosen) { Scanner.Scan(bin, Env.Real()); bin.On = true; }

            long files;
            long total = Scanner.Chosen(Cats, out files);
            if (total + files == 0) { RenderAll(); return; }
            var body = new StackPanel();
            bool anyPermanent = false, recycle = false;
            foreach (Category c in Cats)
            {
                if (!c.Chosen) continue;
                if (c.IsRecycle) recycle = true; else anyPermanent = true;
                body.Children.Add(SheetLine(c.Title, c.IsRecycle ? Fmt.Plural(c.Count, "объект", "объекта", "объектов") : Fmt.Files(c.Count), Fmt.Size(c.Bytes), false));
            }
            body.Children.Add(new Border { Height = 1, Background = Ui.Solid(Tokens.Dark.Separator), Margin = new Thickness(0, 8, 0, 8) });
            body.Children.Add(SheetLine("Всего", Fmt.Plural(files, "объект", "объекта", "объектов"), Fmt.Size(total), true));
            string warn = "";
            if (anyPermanent) warn += "Временные файлы и кэши удаляются сразу и навсегда, мимо корзины. ";
            if (recycle) warn += "Корзина будет очищена целиком — вернуть из неё уже ничего не получится. ";
            warn += "Файлы, которые сейчас заняты или защищены, останутся на месте.";
            var note = Ui.T(warn, Tokens.Size.Caption, Ui.Label2, FontWeights.Normal);
            note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(0, 14, 0, 0);
            body.Children.Add(note);
            OpenSheet("Удалить выбранное?", body, "Удалить " + Fmt.Size(total), Tokens.Palette.Red, DoClean);
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

        async void DoClean()
        {
            if (Busy) return;
            Notice = null;
            if (DemoMode) { Last = Demo.Clean(this); FinishScan(false); return; }
            Busy = true;
            BusyText = "Удаляю выбранное…";
            RenderAll();
            var cats = Cats;
            CleanResult r = null;
            try
            {
                r = await Task.Run(delegate
                {
                    return Cleaner.Clean(cats, Env.Real(), delegate(double p) { Dispatcher.BeginInvoke(new Action(delegate { BusyText = "Удаляю выбранное… " + Math.Round(p * 100) + "%"; if (Busy) RenderAll(); })); });
                });
            }
            catch (Exception e) { Notice = "Очистка остановилась: " + e.Message; }
            // После очистки — новая проверка: числа на экране должны быть настоящими
            BusyText = "Проверяю, что осталось…";
            RenderAll();
            await Task.Run(delegate { Scanner.ScanAll(cats, Env.Real(), null); });
            ReadDisk();
            Busy = false;
            Last = r;
            FinishScan(false);
        }

        // ───────── Лист с вопросом поверх окна ─────────
        internal void OpenSheet(string title, FrameworkElement body, string confirm, Color tint, Action onConfirm)
        {
            var head = Ui.T(title, Tokens.Size.Title, Ui.Label, FontWeights.SemiBold);
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
            var card = new Border { Width = 520, CornerRadius = new CornerRadius(Tokens.Radius.Block), Background = Ui.Solid(Tokens.Dark.Surface), Child = glass, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var dim = new Border { Background = Ui.Solid(Ui.Alpha(Tokens.Dark.Bg, 0.62)) };
            dim.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };   // щелчок мимо листа ничего не делает
            sheetLayer.Children.Clear();
            sheetLayer.Children.Add(dim);
            sheetLayer.Children.Add(card);
            sheetLayer.Visibility = Visibility.Visible;
            if (IsVisible) Motion.GrowFrom(card, new Point(0.5, 0.5));
        }

        void CloseSheet()
        {
            sheetLayer.Visibility = Visibility.Collapsed;
            sheetLayer.Children.Clear();
        }

        // ───────── Автозапуск и программы ─────────
        internal async void LoadStartup()
        {
            if (loadingStartup) return;
            if (DemoMode) { RenderAll(); return; }
            loadingStartup = true;
            Notice = null;
            RenderAll();
            List<StartupEntry> list = await Task.Run(delegate { return Startup.Read(); });
            StartupItems = list;
            loadingStartup = false;
            RenderAll();
            if (page == PStartup) Enter();
        }

        internal void ToggleStartup(StartupEntry e, bool on)
        {
            Notice = null;
            if (DemoMode) { e.Enabled = on; RenderAll(); return; }
            try { Startup.Set(e, on); }
            catch (Exception x) { Notice = "Не получилось изменить автозапуск: " + x.Message; }
            RenderAll();
        }

        internal async void LoadPrograms()
        {
            if (loadingProgs) return;
            if (DemoMode) { RenderAll(); return; }
            loadingProgs = true;
            Notice = null;
            RenderAll();
            List<Prog> list = await Task.Run(delegate { return Programs.Read(); });
            Progs = list;
            loadingProgs = false;
            RenderAll();
            if (page == PPrograms) Enter();
        }

        internal void AskUninstall(Prog p)
        {
            var text = Ui.T("Откроется собственная программа удаления «" + p.Name + "» — дальше всё происходит в её окне. metla.txt сама файлы программ не трогает. Windows может спросить разрешение, если программе удаления нужны права администратора.", Tokens.Size.Body, Ui.Label2, FontWeights.Normal);
            text.TextWrapping = TextWrapping.Wrap;
            OpenSheet("Удалить «" + p.Name + "»?", text, "Запустить удаление", Tokens.Palette.Red, delegate
            {
                Notice = null;
                if (!DemoMode)
                {
                    try { Programs.Launch(p); }
                    catch (Exception x) { Notice = "Не удалось запустить программу удаления: " + x.Message; }
                }
                RenderAll();
            });
        }

        internal void OpenFolder(string dir)
        {
            if (DemoMode || string.IsNullOrEmpty(dir)) return;
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\""); }
            catch (Exception x) { Notice = "Не удалось открыть папку: " + x.Message; RenderAll(); }
        }

        // Окно Windows «Защита системы»: точки восстановления создаются и настраиваются там, не здесь
        internal void OpenProtection()
        {
            if (DemoMode) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("SystemPropertiesProtection.exe") { UseShellExecute = true }); }
            catch (Exception x) { Notice = "Не удалось открыть «Защиту системы»: " + x.Message; RenderAll(); }
        }

        // ───────── Трей и ежедневное напоминание ─────────
        void SyncTray()
        {
            if (Prefs.Remind && tray == null)
            {
                try
                {
                    var menu = new System.Windows.Forms.ContextMenuStrip();
                    menu.Items.Add("Открыть metla.txt", null, delegate { ShowWindow(); });
                    var coreItem = new System.Windows.Forms.ToolStripMenuItem("core.txt", null, delegate { CoreApp.Go(AppHub.Online, AppHub.ShowCore); });
                    menu.Items.Add(coreItem);
                    menu.Opening += delegate { coreItem.Text = CoreApp.Label(AppHub.Online); };
                    menu.Items.Add("Выход", null, delegate { quitting = true; Close(); });
                    tray = new System.Windows.Forms.NotifyIcon { Icon = TrayIcon(), Text = "metla.txt", ContextMenuStrip = menu, Visible = true };
                    tray.MouseClick += delegate(object s, System.Windows.Forms.MouseEventArgs e) { if (e.Button == System.Windows.Forms.MouseButtons.Left) ShowWindow(); };
                    tray.BalloonTipClicked += delegate { ShowWindow(); };
                }
                catch { tray = null; }
            }
            else if (!Prefs.Remind && tray != null)
            {
                DropTray();
                if (!IsVisible) { quitting = true; Close(); }   // напоминание выключили из core.txt, а окна нет — в фоне делать нечего
            }
        }

        void DropTray()
        {
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
                using (var eye = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(Tokens.Dark.Bg.R, Tokens.Dark.Bg.G, Tokens.Dark.Bg.B)))
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
            if (!Prefs.Remind || Busy || sheetLayer.Visibility == Visibility.Visible) return;
            if (DateTime.UtcNow - Prefs.LastAuto < TimeSpan.FromHours(24)) return;
            Prefs.LastAuto = DateTime.UtcNow;
            Prefs.Save();
            Busy = true;
            BusyText = "Ежедневная проверка…";
            RenderAll();
            var cats = Cats;
            await Task.Run(delegate { Scanner.ScanAll(cats, Env.Real(), null); });
            ReadDisk();
            Busy = false;
            Last = null;
            FinishScan(false);
            long total = Scanner.Total(Cats);
            if (total > RemindBytes && tray != null && !IsVisible)
                try { tray.ShowBalloonTip(8000, "metla.txt", "Можно освободить " + Fmt.Size(total) + ". Откройте окно, чтобы посмотреть, что именно.", System.Windows.Forms.ToolTipIcon.None); } catch { }
        }

        public void ShowWindow()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        // Рисует окно в файл PNG, не показывая его на экране
        public void Shot(string file, bool sheet, string expand, double scroll)
        {
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
            var bmp = new RenderTargetBitmap((int)(W * 1.5), (int)(H * 1.5), 144, 144, PixelFormats.Pbgra32);
            bmp.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = System.IO.File.Create(file)) enc.Save(f);
        }
    }
}

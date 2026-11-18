// metla.txt. Pages.cs — четыре страницы: «Обзор», «Очистка», «Автозапуск», «Программы».
// Страница — прокручиваемый столбец из заголовков групп и стеклянных строк, как в core.txt.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DevTxt.Metla
{
    class Page
    {
        public readonly ScrollViewer Root;
        protected readonly StackPanel stack = new StackPanel { Margin = new Thickness(7, 0, 7, 12) };
        public virtual Panel Entrance { get { return stack; } }   // что появляется волной при открытии страницы
        protected readonly MainWindow w;
        protected static readonly CultureInfo Ru = new CultureInfo("ru-RU");

        protected Page(MainWindow w)
        {
            this.w = w;
            Root = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Padding = new Thickness(25, 0, 25, 0), Content = stack, Focusable = false, Visibility = Visibility.Collapsed };
        }

        protected void Title(Panel to, string text)
        {
            var t = Ui.T(text, Tokens.Size.Heading, Ui.Label2, FontWeights.SemiBold);
            t.Margin = new Thickness(4, to.Children.Count == 0 ? 2 : 20, 0, 8);
            to.Children.Add(t);
        }

        protected static StackPanel Text(string label, string hint, bool wrap)
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            text.Children.Add(Ui.T(label, Tokens.Size.Body, Ui.Label, FontWeights.Normal));
            if (!string.IsNullOrEmpty(hint))
            {
                var h = Ui.T(hint, Tokens.Size.Micro, Ui.Label3, FontWeights.Normal);
                if (wrap) h.TextWrapping = TextWrapping.Wrap;
                h.Margin = new Thickness(0, 1, 0, 0);
                text.Children.Add(h);
            }
            return text;
        }

        // Строка: слева название и пояснение, справа — что-то одно или несколько (первый в списке — у самого края)
        protected Border Row(Panel to, FrameworkElement left, params FrameworkElement[] right)
        {
            var line = new DockPanel { LastChildFill = true, MinHeight = 30 };
            foreach (FrameworkElement control in right)
            {
                if (control == null) continue;
                control.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(control, Dock.Right);
                line.Children.Add(control);
            }
            line.Children.Add(left);
            var row = Ui.Row(line);
            row.Margin = new Thickness(0, 0, 0, 8);
            to.Children.Add(row);
            return row;
        }

        protected static TextBlock Side(string text, double width, Brush color, double size)
        {
            var t = Ui.T(text, size, color, FontWeights.Normal);
            t.Width = width; t.TextAlignment = TextAlignment.Right; t.Margin = new Thickness(0, 0, 14, 0);
            return t;
        }

        protected static string StateOf(Category c)
        {
            if (c.NeedsAdmin) return "нужны права администратора";
            if (!c.Selectable) return "только для сведения";
            return c.On ? "выбрано" : "не выбрано";
        }
    }

    // «Обзор»: одно большое число, диск и короткий список категорий
    class OverviewPage : Page
    {
        bool count;   // после проверки число «набегает» от нуля (Motion.Count); при «Меньше движения» сразу стоит на месте

        public OverviewPage(MainWindow w) : base(w) { }

        public void CountUp() { count = w.IsVisible; }
        public void Render()
        {
            double offset = Root.VerticalOffset;
            stack.Children.Clear();
            long files;
            long total = Scanner.Total(w.Cats), chosen = Scanner.Chosen(w.Cats, out files);

            // Главный блок: слева число, справа диск
            var caption = Ui.T(w.Scanned ? "Можно освободить" : "Сколько можно освободить", Tokens.Size.Heading, Ui.Label2, FontWeights.SemiBold);
            var big = new TextBlock { Text = w.Scanned ? Fmt.Size(total) : "—", FontSize = Tokens.Size.Display * 2, FontWeight = FontWeights.Bold, FontFamily = Ui.DisplayFont, Foreground = Ui.Label, Margin = new Thickness(0, 0, 0, 2) };
            string under;
            if (w.Busy) under = w.BusyText ?? "Работаю…";
            else if (!w.Scanned) under = "Нажмите «Проверить». Проверка только смотрит и ничего не удаляет";
            else if (total == 0) under = "Убирать нечего";
            else under = "Выбрано для очистки: " + Fmt.Size(chosen) + " · " + Fmt.Plural(files, "объект", "объекта", "объектов");
            var sub = Ui.T(under, Tokens.Size.Body, Ui.Label2, FontWeights.Normal);
            if (count && w.Scanned) { count = false; Motion.Count(big, 0, total, delegate(double v) { return Fmt.Size((long)v); }); }
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { caption, big, sub } };

            var right = new StackPanel { Width = 300, VerticalAlignment = VerticalAlignment.Center };
            if (w.DiskTotal > 0)
            {
                long used = w.DiskTotal - w.DiskFree;
                right.Children.Add(Ui.T("Диск " + w.DiskName, Tokens.Size.Heading, Ui.Label2, FontWeights.SemiBold));
                var free = Ui.T("Свободно " + Fmt.Size(w.DiskFree), Tokens.Size.Title, Ui.Label, FontWeights.SemiBold);
                free.Margin = new Thickness(0, 4, 0, 10);
                right.Children.Add(free);
                var bar = new Progress(Tokens.Palette.Brand);
                bar.Value = used / (double)w.DiskTotal;
                right.Children.Add(bar.Root);
                var legend = Ui.T("Занято " + Fmt.Size(used) + " из " + Fmt.Size(w.DiskTotal), Tokens.Size.Caption, Ui.Label3, FontWeights.Normal);
                legend.Margin = new Thickness(0, 8, 0, 0);
                right.Children.Add(legend);
            }
            var hero = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(right, Dock.Right);
            hero.Children.Add(right);
            hero.Children.Add(left);
            var block = Ui.Block(hero, true);
            block.Padding = new Thickness(22, 16, 22, 18);
            stack.Children.Add(block);

            // Категории: коротко, одной стеклянной плашкой
            Title(stack, w.Scanned ? "Что нашлось" : "Что будет проверено");
            var list = new StackPanel();
            for (int i = 0; i < w.Cats.Count; i++)
            {
                Category c = w.Cats[i];
                if (i > 0) list.Children.Add(new Border { Height = 1, Background = Ui.Separator });
                var line = new DockPanel { LastChildFill = true, Height = 27 };
                var size = Ui.T(!w.Scanned || c.NeedsAdmin ? "—" : Fmt.Size(c.Bytes), Tokens.Size.Body, c.Chosen ? Ui.Label : Ui.Label2, c.Chosen ? FontWeights.SemiBold : FontWeights.Normal);
                size.Width = 86; size.TextAlignment = TextAlignment.Right; size.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(size, Dock.Right);
                line.Children.Add(size);
                var state = Ui.T(StateOf(c), Tokens.Size.Caption, Ui.Label3, FontWeights.Normal);
                state.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(state, Dock.Right);
                line.Children.Add(state);
                var name = Ui.T(c.Title, Tokens.Size.Body, Ui.Label, FontWeights.Normal);
                name.VerticalAlignment = VerticalAlignment.Center;
                line.Children.Add(name);
                list.Children.Add(line);
            }
            var listRow = Ui.Row(list);
            listRow.Padding = new Thickness(14, 4, 14, 4);
            listRow.Margin = new Thickness(0, 0, 0, 8);
            stack.Children.Add(listRow);

            var open = new Btn();
            open.Set("Защита системы", null, true, w.OpenProtection);
            Row(stack, Text("Перед большой уборкой создайте точку восстановления", "Это делает сама Windows в окне «Защита системы» — metla.txt точками восстановления не управляет", false), open.Root);
            Root.ScrollToVerticalOffset(offset);
        }
    }

    // «Очистка»: категории с переключателями; каждую можно раскрыть и увидеть самые большие файлы
    class CleanPage : Page
    {
        readonly HashSet<string> open = new HashSet<string>();

        public CleanPage(MainWindow w) : base(w) { }

        public void Expand(string id) { open.Add(id); }

        public void Render()
        {
            double offset = Root.VerticalOffset;
            stack.Children.Clear();
            var intro = Ui.T("Удаляется только то, что включено здесь, и только после вопроса с точными числами. Раскройте строку, чтобы увидеть самые большие файлы.", Tokens.Size.Body, Ui.Label2, FontWeights.Normal);
            intro.TextWrapping = TextWrapping.Wrap; intro.Margin = new Thickness(4, 2, 4, 12);
            stack.Children.Add(intro);

            foreach (Category cat in w.Cats)
            {
                Category c = cat;
                bool canOpen = w.Scanned && c.Top.Count > 0, isOpen = canOpen && open.Contains(c.Id);
                string hint = c.Hint;
                if (w.Scanned && c.Note != null && !c.NeedsAdmin) hint += ". " + c.Note;

                var arrow = new TextBlock { Text = isOpen ? "\uE70D" : "\uE76C", FontFamily = Ui.IconFont, FontSize = Tokens.Size.Micro, Foreground = canOpen ? Ui.Label2 : Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                var text = Text(c.Title, hint, true);
                var left = new DockPanel { LastChildFill = true, Background = Brushes.Transparent };
                DockPanel.SetDock(arrow, Dock.Left);
                left.Children.Add(arrow);
                left.Children.Add(text);
                if (canOpen)
                {
                    left.Cursor = Cursors.Hand;
                    left.MouseLeftButtonDown += delegate { if (!open.Remove(c.Id)) open.Add(c.Id); Render(); };
                }

                var numbers = new StackPanel { Width = 104, Margin = new Thickness(0, 0, 14, 0) };
                var size = Ui.T(!w.Scanned || c.NeedsAdmin ? "—" : Fmt.Size(c.Bytes), Tokens.Size.Body, Ui.Label, FontWeights.SemiBold);
                size.TextAlignment = TextAlignment.Right;
                numbers.Children.Add(size);
                if (w.Scanned && !c.NeedsAdmin)
                {
                    var count = Ui.T(c.IsRecycle ? Fmt.Plural(c.Count, "объект", "объекта", "объектов") : Fmt.Files(c.Count), Tokens.Size.Micro, Ui.Label3, FontWeights.Normal);
                    count.TextAlignment = TextAlignment.Right;
                    numbers.Children.Add(count);
                }

                FrameworkElement control;
                if (!c.Selectable)
                {
                    var b = new Btn();
                    b.Set("Открыть папку", null, true, delegate { w.OpenFolder(c.OpenFolder); });
                    control = b.Root;
                }
                else if (c.NeedsAdmin)
                {
                    var t = Ui.T("нужны права\nадминистратора", Tokens.Size.Micro, Ui.Solid(Tokens.Palette.Orange), FontWeights.Normal);
                    t.TextAlignment = TextAlignment.Right;
                    control = t;
                }
                else control = new Switch(c.On, delegate(bool on) { c.On = on; w.RenderAll(); }).Root;

                Border row = Row(stack, left, control, numbers);
                if (!isOpen) continue;

                // Самые большие — чтобы было видно, что это за файлы
                var items = new StackPanel { Margin = new Thickness(20, 8, 0, 0) };
                items.Children.Add(new Border { Height = 1, Background = Ui.Separator, Margin = new Thickness(0, 0, 0, 6) });
                foreach (Item it in c.Top)
                {
                    var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 2) };
                    var s = Ui.T(Fmt.Size(it.Size), Tokens.Size.Caption, Ui.Label2, FontWeights.Normal);
                    s.Width = 80; s.TextAlignment = TextAlignment.Right;
                    DockPanel.SetDock(s, Dock.Right);
                    line.Children.Add(s);
                    string dir = "", file = it.Path;
                    try { dir = System.IO.Path.GetDirectoryName(it.Path); file = System.IO.Path.GetFileName(it.Path); } catch { }
                    var name = Ui.T(file, Tokens.Size.Caption, Ui.Label, FontWeights.Normal);
                    name.MaxWidth = 300; name.Margin = new Thickness(0, 0, 10, 0);
                    DockPanel.SetDock(name, Dock.Left);
                    line.Children.Add(name);
                    var where = Ui.T(dir, Tokens.Size.Micro, Ui.Label3, FontWeights.Normal);
                    where.VerticalAlignment = VerticalAlignment.Center;
                    where.ToolTip = it.Path;
                    line.Children.Add(where);
                    items.Children.Add(line);
                }
                if (c.Count > c.Top.Count)
                {
                    var more = Ui.T("и ещё " + Fmt.Files(c.Count - c.Top.Count) + " поменьше", Tokens.Size.Micro, Ui.Label3, FontWeights.Normal);
                    more.Margin = new Thickness(0, 4, 0, 0);
                    items.Children.Add(more);
                }
                var line0 = (UIElement)row.Child;
                row.Child = null;
                row.Child = new StackPanel { Children = { line0, items } };
            }
            Root.ScrollToVerticalOffset(offset);
        }
    }

    // «Автозапуск»: что запускается вместе с Windows. Свои записи можно отключить, общие — только посмотреть.
    class StartupPage : Page
    {
        readonly Dictionary<string, ImageSource> icons = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public StartupPage(MainWindow w) : base(w) { }

        // Значок из файла программы; не получилось — будет буква
        ImageSource Icon(string exe)
        {
            if (w.DemoMode || string.IsNullOrEmpty(exe)) return null;
            ImageSource src;
            if (icons.TryGetValue(exe, out src)) return src;
            try
            {
                if (System.IO.File.Exists(exe))
                    using (System.Drawing.Icon ico = System.Drawing.Icon.ExtractAssociatedIcon(exe))
                    {
                        src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        src.Freeze();
                    }
            }
            catch { src = null; }
            icons[exe] = src;
            return src;
        }

        void Entry(StartupEntry entry)
        {
            StartupEntry e = entry;
            FrameworkElement mark;
            ImageSource src = Icon(e.Exe);
            if (src != null)
            {
                var img = new Image { Source = src, Width = 22, Height = 22 };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                mark = img;
            }
            else mark = new TextBlock { Text = e.Name.Substring(0, 1).ToUpperInvariant(), FontSize = Tokens.Size.Title, FontWeight = FontWeights.Bold, FontFamily = Ui.DisplayFont, Foreground = Ui.Label2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Border tile = Ui.Tile(mark);
            tile.Width = 36; tile.Height = 36; tile.CornerRadius = new CornerRadius(Tokens.Radius.Control); tile.Margin = new Thickness(0, 0, 12, 0);

            string hint = (string.IsNullOrEmpty(e.Publisher) ? "" : e.Publisher + " · ") + e.Command;
            var left = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(tile, Dock.Left);
            left.Children.Add(tile);
            left.Children.Add(Text(e.Name, hint, false));

            FrameworkElement control;
            if (e.Editable) control = new Switch(e.Enabled, delegate(bool on) { w.ToggleStartup(e, on); }).Root;
            else control = Ui.T(e.Enabled ? "включено" : "отключено", Tokens.Size.Caption, Ui.Label3, FontWeights.Normal);
            Border row = Row(stack, left, control);
            if (!e.Editable) row.Opacity = 0.6;
        }

        public void Render()
        {
            double offset = Root.VerticalOffset;
            stack.Children.Clear();
            if (w.StartupItems == null) { Root.ScrollToVerticalOffset(offset); return; }
            List<StartupEntry> mine = w.StartupItems.FindAll(delegate(StartupEntry e) { return e.Editable; });
            List<StartupEntry> shared = w.StartupItems.FindAll(delegate(StartupEntry e) { return !e.Editable; });

            Title(stack, "Запускается для вас");
            if (mine.Count == 0) Row(stack, Text("Здесь пусто", "Для вашей учётной записи вместе с Windows ничего не запускается", false));
            foreach (StartupEntry e in mine) Entry(e);

            if (shared.Count > 0)
            {
                Title(stack, "Общие для всех пользователей");
                var note = Ui.T("Только для просмотра: они меняются в Диспетчере задач с правами администратора.", Tokens.Size.Caption, Ui.Label3, FontWeights.Normal);
                note.Margin = new Thickness(4, -4, 4, 10);
                stack.Children.Add(note);
                foreach (StartupEntry e in shared) Entry(e);
            }
            Root.ScrollToVerticalOffset(offset);
        }
    }

    // «Программы»: список установленного, поиск и порядок. «Удалить» запускает собственную программу удаления.
    class ProgramsPage : Page
    {
        public readonly ScrollViewer Scroll;
        readonly StackPanel list = new StackPanel();
        readonly TextBox search;
        readonly TextBlock placeholder;
        bool byDate;

        public ProgramsPage(MainWindow w) : base(w)
        {
            // Поле поиска: общего элемента для ввода в kit.txt пока нет — собрано из тех же токенов и стекла
            search = new TextBox
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Ui.Label, CaretBrush = Ui.Label, FontFamily = Ui.TextFont, FontSize = Tokens.Size.Body,
                VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0), SelectionBrush = Ui.Solid(Tokens.Palette.Brand)
            };
            placeholder = Ui.T("Поиск по названию или издателю", Tokens.Size.Body, Ui.Label3, FontWeights.Normal);
            placeholder.IsHitTestVisible = false; placeholder.VerticalAlignment = VerticalAlignment.Center; placeholder.Margin = new Thickness(2, 0, 0, 1);
            search.TextChanged += delegate { placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; RenderList(); };
            var field = new Border
            {
                Height = 32, Width = 320, CornerRadius = new CornerRadius(16), Background = Ui.GlassFill, BorderBrush = Ui.Rim, BorderThickness = new Thickness(1), Padding = new Thickness(14, 0, 14, 0),
                Child = new Grid { Children = { placeholder, search } }, HorizontalAlignment = HorizontalAlignment.Left, SnapsToDevicePixels = true
            };
            var order = new Seg(new string[] { "По размеру", "По дате" }, 0, delegate(int i) { byDate = i == 1; RenderList(); });
            order.Root.HorizontalAlignment = HorizontalAlignment.Right;
            var tools = new Grid { Margin = new Thickness(32, 2, 32, 12), Children = { field, order.Root } };

            // Поиск и порядок стоят на месте, прокручивается только список
            Scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Padding = new Thickness(25, 0, 25, 0), Content = list, Focusable = false };
            list.Margin = new Thickness(7, 0, 7, 12);
            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(tools, Dock.Top);
            dock.Children.Add(tools);
            dock.Children.Add(Scroll);
            Root.Padding = new Thickness(0);
            Root.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Root.Content = dock;
        }

        public override Panel Entrance { get { return list; } }

        public void Render() { RenderList(); }

        void RenderList()
        {
            double offset = Scroll.VerticalOffset;
            list.Children.Clear();
            if (w.Progs == null) return;
            List<Prog> view = Programs.View(w.Progs, search.Text, byDate);
            if (view.Count == 0) Row(list, Text("Ничего не нашлось", "Попробуйте другое слово", false));
            foreach (Prog prog in view)
            {
                Prog p = prog;
                string hint = p.Publisher ?? "";
                if (!string.IsNullOrEmpty(p.Version)) hint += (hint == "" ? "" : " · ") + "версия " + p.Version;
                var b = new Btn();
                b.Set("Удалить", null, true, delegate { w.AskUninstall(p); });
                b.Root.MinWidth = 84;
                TextBlock date = Side(p.Installed.HasValue ? p.Installed.Value.ToString("d MMM yyyy", Ru) : "—", 96, Ui.Label3, Tokens.Size.Caption);
                TextBlock size = Side(p.Bytes >= 0 ? Fmt.Size(p.Bytes) : "—", 76, Ui.Label2, Tokens.Size.Body);
                Row(list, Text(p.Name, hint, false), b.Root, date, size);
            }
            Scroll.ScrollToVerticalOffset(offset);
        }
    }

    // Выдуманное состояние для снимков и проверки вида: диск, реестр и сеть не трогаются
    static class Demo
    {
        static Item It(string path, long size) { return new Item { Path = path, Size = size, Root = @"C:\Users\вы" }; }

        static void Set(Category c, long bytes, long count, params Item[] top)
        {
            c.Reset();
            c.Bytes = bytes; c.Count = count;
            c.Top.AddRange(top);
        }

        public static void Scan(MainWindow w)
        {
            const string temp = @"C:\Users\вы\AppData\Local\Temp\";
            Set(Scanner.Find(w.Cats, "temp"), 1932735283, 4218,
                It(temp + @"установка-игры\data1.cab", 734003200), It(temp + @"nvidia-driver\setup.bin", 412316860), It(temp + "render_cache_0142.tmp", 268435456),
                It(temp + @"Обновление\patch-2.4.1.zip", 157286400), It(temp + "video_export_7f3a.mp4.part", 96468992), It(temp + "~DF3A91C2.tmp", 41943040));
            Category win = Scanner.Find(w.Cats, "wintemp");
            Set(win, 0, 0);
            win.NeedsAdmin = true; win.Note = "Нужны права администратора — категория пропущена";
            Set(Scanner.Find(w.Cats, "dumps"), 891289600, 37, It(@"C:\Users\вы\AppData\Local\CrashDumps\game.exe.11204.dmp", 512753664), It(@"C:\Users\вы\AppData\Local\CrashDumps\editor.exe.6032.dmp", 221249536));
            Category thumbs = Scanner.Find(w.Cats, "thumbs");
            Set(thumbs, 187695104, 9, It(@"C:\Users\вы\AppData\Local\Microsoft\Windows\Explorer\thumbcache_1280.db", 96468992), It(@"C:\Users\вы\AppData\Local\Microsoft\Windows\Explorer\thumbcache_256.db", 58720256));
            thumbs.Note = "Файлы, которые сейчас держит Проводник, будут пропущены";
            Category browsers = Scanner.Find(w.Cats, "browsers");
            Set(browsers, 1503238554, 12840, It(@"C:\Users\вы\AppData\Local\Microsoft\Edge\User Data\Default\Cache\Cache_Data\data_3", 268435456), It(@"C:\Users\вы\AppData\Local\Mozilla\Firefox\Profiles\x1.default\cache2\entries\9A31F0", 73400320));
            browsers.Note = "Запущен и пропущен: Chrome. Закройте браузер и проверьте снова";
            Set(Scanner.Find(w.Cats, "recycle"), 2362232013, 164);
            Category downloads = Scanner.Find(w.Cats, "downloads");
            Set(downloads, 9771050598, 58, It(@"C:\Users\вы\Downloads\windows-11.iso", 5798205850), It(@"C:\Users\вы\Downloads\отпуск-2025.zip", 734003200));
            downloads.OpenFolder = @"C:\Users\вы\Downloads";
        }

        public static void Fill(MainWindow w)
        {
            Scan(w);
            w.Scanned = true;
            w.ScanAt = new DateTime(2026, 11, 18, 9, 30, 0, DateTimeKind.Local).ToUniversalTime();
            AppHub.Found(Scanner.Total(w.Cats), w.ScanAt);
            w.DiskName = "C:"; w.DiskTotal = 511101108224; w.DiskFree = 62277025792;

            w.StartupItems = new List<StartupEntry>
            {
                new StartupEntry { Name = "core.txt", Publisher = "dev.txt", Command = @"""C:\Users\вы\AppData\Local\dev.txt\Core.exe"" --tray", Editable = true, Enabled = true },
                new StartupEntry { Name = "Discord", Publisher = "Discord Inc.", Command = @"""C:\Users\вы\AppData\Local\Discord\Update.exe"" --processStart Discord.exe", Editable = true, Enabled = true },
                new StartupEntry { Name = "Steam", Publisher = "Valve Corporation", Command = @"""C:\Program Files (x86)\Steam\steam.exe"" -silent", Editable = true, Enabled = false },
                new StartupEntry { Name = "Заметки", Command = @"C:\Users\вы\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\Заметки.lnk", Editable = true, Enabled = true, Folder = true },
                new StartupEntry { Name = "SecurityHealth", Publisher = "Microsoft Corporation", Command = @"%windir%\system32\SecurityHealthSystray.exe", Enabled = true },
                new StartupEntry { Name = "RtkAudUService", Publisher = "Realtek Semiconductor", Command = @"""C:\Windows\System32\RtkAudUService64.exe"" -background", Enabled = true }
            };
            w.Progs = new List<Prog>
            {
                new Prog { Name = "Большая игра", Publisher = "Студия «Север»", Version = "1.12.0", Bytes = 68719476736, Installed = new DateTime(2026, 3, 14), Uninstall = "demo" },
                new Prog { Name = "Видеоредактор", Publisher = "Монтаж", Version = "19.1", Bytes = 4617089843, Installed = new DateTime(2026, 9, 2), Uninstall = "demo" },
                new Prog { Name = "Офисный пакет", Publisher = "Документы и таблицы", Version = "24.8.2", Bytes = 2040109465, Installed = new DateTime(2025, 12, 20), Uninstall = "demo" },
                new Prog { Name = "Браузер", Publisher = "Открытый веб", Version = "131.0", Bytes = 734003200, Installed = new DateTime(2026, 11, 10), Uninstall = "demo" },
                new Prog { Name = "Мессенджер", Publisher = "Связь", Version = "5.6.3", Bytes = 412316860, Installed = new DateTime(2026, 10, 28), Uninstall = "demo" },
                new Prog { Name = "Архиватор", Publisher = "Сжатие", Version = "24.08", Bytes = 5662310, Installed = new DateTime(2025, 6, 1), Uninstall = "demo" },
                new Prog { Name = "Драйвер принтера", Publisher = "Печать", Version = "3.2", Bytes = -1, Installed = null, Uninstall = "demo" }
            };
        }

        // В --demo ничего не удаляется: выбранные категории просто обнуляются на экране
        public static CleanResult Clean(MainWindow w)
        {
            var r = new CleanResult();
            foreach (Category c in w.Cats)
            {
                if (!c.Chosen) continue;
                r.Freed += c.Bytes; r.Deleted += c.Count;
                Set(c, 0, 0);
            }
            r.Skipped = 12;
            return r;
        }
    }
}

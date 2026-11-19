// metla.txt. Pages.cs — четыре страницы: «Обзор», «Очистка», «Автозапуск», «Программы».
// Страница — прокручиваемый столбец из заголовков групп и стеклянных строк, как в core.txt.
// Строки строятся один раз на набор данных; дальше Update() только меняет надписи и состояния на месте —
// поэтому переключатель доезжает, а фокус с клавиатуры остаётся там, где был.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace DevTxt.Metla
{
    // Значки программ из их файлов. Достаются в фоне; готовая картинка заморожена и годится для потока окна.
    static class Icons
    {
        static readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        // null — файла нет или значок достать не получилось: тогда на плитке будет буква
        public static ImageSource Of(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            ImageSource src = null;
            lock (cache) if (cache.TryGetValue(file, out src)) return src;
            try
            {
                string path = Environment.ExpandEnvironmentVariables(file);
                if (System.IO.File.Exists(path))
                    using (System.Drawing.Icon ico = System.Drawing.Icon.ExtractAssociatedIcon(path))
                    {
                        src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        src.Freeze();
                    }
            }
            catch { src = null; }
            lock (cache) cache[file] = src;
            return src;
        }
    }

    // «Читаю список…»: полоса хода и подпись, пока список загружается
    class LoadBox
    {
        public readonly StackPanel Root = new StackPanel { Visibility = Visibility.Collapsed };
        readonly Progress bar = new Progress(Tokens.Palette.Brand);
        readonly TextBlock label = Ui.T("Читаю список…", Tokens.Size.Body, Ui.Label2, FontWeights.Normal);

        public LoadBox(Thickness margin)
        {
            Root.Margin = margin;
            label.Margin = new Thickness(0, 10, 0, 0);
            Root.Children.Add(bar.Root);
            Root.Children.Add(label);
        }

        // first — списка ещё нет совсем: тогда кроме полосы показывается и подпись
        public void Set(bool loading, bool first)
        {
            Root.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            label.Visibility = first ? Visibility.Visible : Visibility.Collapsed;
            bar.Busy = loading;
        }
    }

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
            Root = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(25, 0, 25, 0), Content = stack, Focusable = false, Visibility = Visibility.Collapsed };
        }

        protected void Title(Panel to, string text)
        {
            var t = Ui.T(text, Tokens.Size.Heading, Ui.Label2, FontWeights.SemiBold);
            t.Margin = new Thickness(4, to.Children.Count == 0 ? 2 : 20, 0, 8);
            to.Children.Add(t);
        }

        // Подсказка с полным текстом — только когда надпись не поместилась и обрезана
        protected static void Tip(TextBlock t, string full)
        {
            if (string.IsNullOrEmpty(full)) return;
            t.ToolTip = full;
            t.ToolTipOpening += delegate(object s, ToolTipEventArgs e)
            {
                var probe = new TextBlock { Text = t.Text, FontSize = t.FontSize, FontFamily = t.FontFamily, FontWeight = t.FontWeight };
                probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (probe.DesiredSize.Width <= t.ActualWidth + 0.5) e.Handled = true;
            };
        }

        protected static StackPanel Text(string label, string hint, bool wrap)
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            var l = Ui.T(label, Tokens.Size.Body, Ui.Label, FontWeights.Normal);
            Tip(l, label);
            text.Children.Add(l);
            if (!string.IsNullOrEmpty(hint))
            {
                var h = Ui.T(hint, Tokens.Size.Micro, Ui.Label3, FontWeights.Normal);
                if (wrap) h.TextWrapping = TextWrapping.Wrap; else Tip(h, hint);
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

        // Плитка со значком программы; пока значка нет — первая буква названия
        protected static Border IconTile(string name, ImageSource src, double side)
        {
            Border tile = Ui.Tile(null);
            tile.Width = side; tile.Height = side; tile.CornerRadius = new CornerRadius(Tokens.Radius.Control); tile.Margin = new Thickness(0, 0, 12, 0);
            SetIcon(tile, name, src);
            return tile;
        }

        protected static void SetIcon(Border tile, string name, ImageSource src)
        {
            if (src != null)
            {
                var img = new Image { Source = src, Width = Math.Round(tile.Width * 0.6), Height = Math.Round(tile.Width * 0.6) };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                tile.Child = img;
                return;
            }
            string letter = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
            tile.Child = new TextBlock { Text = letter, FontSize = tile.Width >= 36 ? Tokens.Size.Title : Tokens.Size.Body, FontWeight = FontWeights.SemiBold, FontFamily = Ui.DisplayFont, Foreground = Ui.Label2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        protected static bool IsEmpty(MainWindow w, Category c) { return w.Scanned && c.Cleanable && c.Bytes + c.Count == 0; }

        protected static string StateOf(MainWindow w, Category c)
        {
            if (c.NeedsAdmin) return "нужны права администратора";
            if (!c.Selectable) return "только для сведения";
            if (IsEmpty(w, c)) return "пусто";
            return c.On ? "выбрано" : "не выбрано";
        }
    }

    // «Обзор»: одно большое число, диск и короткий список категорий
    class OverviewPage : Page
    {
        bool count;                // после проверки число «набегает» от нуля (Motion.Count); при «Меньше движения» сразу стоит на месте
        DateTime countingUntil;    // пока число набегает, Update его не трогает
        CleanResult shownLast;     // для какого итога построена разбивка по категориям

        readonly TextBlock caption = Ui.T("", Tokens.Size.Heading, Ui.Label2, FontWeights.SemiBold);
        readonly TextBlock big = new TextBlock { Text = "—", FontSize = Tokens.Size.Display * 2, FontWeight = FontWeights.SemiBold, FontFamily = Ui.DisplayFont, Foreground = Ui.Label, Margin = new Thickness(0, 0, 0, 2) };
        readonly TextBlock sub = Ui.T("", Tokens.Size.Body, Ui.Label2, FontWeights.Normal);
        readonly TextBlock sub2 = Ui.T("", Tokens.Size.Caption, Ui.Label3, FontWeights.Normal);
        readonly Progress bar = new Progress(Tokens.Palette.Brand);
        readonly StackPanel parts = new StackPanel { Margin = new Thickness(0, 12, 32, 0), Visibility = Visibility.Collapsed };
        readonly StackPanel disk = new StackPanel { Width = 300, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock diskTitle = Ui.T("", Tokens.Size.Heading, Ui.Label2, FontWeights.SemiBold);
        readonly TextBlock diskFree = Ui.T("", Tokens.Size.Title, Ui.Label, FontWeights.SemiBold);
        readonly TextBlock diskLegend = Ui.T("", Tokens.Size.Caption, Ui.Label3, FontWeights.Normal);
        readonly Progress diskBar = new Progress(Tokens.Palette.Brand);
        readonly TextBlock listTitle;
        readonly List<string> ids = new List<string>();
        readonly List<TextBlock> sizes = new List<TextBlock>(), states = new List<TextBlock>();

        public OverviewPage(MainWindow w) : base(w)
        {
            // Главный блок: слева число и ход работы, справа диск
            sub.TextWrapping = TextWrapping.Wrap;
            sub2.Margin = new Thickness(0, 2, 0, 0);
            bar.Root.Margin = new Thickness(0, 12, 32, 0); bar.Root.Visibility = Visibility.Hidden;
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { caption, big, sub, sub2, bar.Root, parts } };

            diskFree.Margin = new Thickness(0, 4, 0, 10);
            diskLegend.Margin = new Thickness(0, 8, 0, 0);
            disk.Children.Add(diskTitle); disk.Children.Add(diskFree); disk.Children.Add(diskBar.Root); disk.Children.Add(diskLegend);
            var hero = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(disk, Dock.Right);
            hero.Children.Add(disk);
            hero.Children.Add(left);
            var block = Ui.Block(hero, true);
            block.Padding = new Thickness(22, 16, 22, 18);
            stack.Children.Add(block);

            // Категории: коротко, одной стеклянной плашкой; щелчок по строке открывает её на странице «Очистка»
            Title(stack, "Что будет проверено");
            listTitle = (TextBlock)stack.Children[stack.Children.Count - 1];
            var list = new StackPanel();
            for (int i = 0; i < w.Cats.Count; i++)
            {
                string id = w.Cats[i].Id;
                if (i > 0) list.Children.Add(new Border { Height = 1, Background = Ui.Separator, Margin = new Thickness(8, 0, 8, 0) });
                var line = new DockPanel { LastChildFill = true, Height = 30 };
                var go = new TextBlock { Text = "\uE76C", FontFamily = Ui.IconFont, FontSize = Tokens.Size.Micro, Foreground = Ui.Label3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
                DockPanel.SetDock(go, Dock.Right);
                line.Children.Add(go);
                var size = Ui.T("—", Tokens.Size.Body, Ui.Label2, FontWeights.Normal);
                size.Width = 86; size.TextAlignment = TextAlignment.Right; size.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(size, Dock.Right);
                line.Children.Add(size);
                var state = Ui.T("", Tokens.Size.Caption, Ui.Label3, FontWeights.Normal);
                state.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(state, Dock.Right);
                line.Children.Add(state);
                var name = Ui.T(w.Cats[i].Title, Tokens.Size.Body, Ui.Label, FontWeights.Normal);
                name.VerticalAlignment = VerticalAlignment.Center; name.Margin = new Thickness(0, 0, 12, 0);
                Tip(name, w.Cats[i].Title);
                line.Children.Add(name);
                var item = new Border { CornerRadius = new CornerRadius(Tokens.Radius.Control), Background = Brushes.Transparent, Padding = new Thickness(8, 0, 8, 0), Child = line, Cursor = Cursors.Hand, ToolTip = "Открыть в «Очистке»" };
                item.MouseEnter += delegate { item.Background = Ui.Fill; };
                item.MouseLeave += delegate { item.Background = Brushes.Transparent; };
                item.MouseLeftButtonUp += delegate { w.OpenCategory(id); };
                Ui.Keyboard(item, delegate { w.OpenCategory(id); });
                list.Children.Add(item);
                ids.Add(id); sizes.Add(size); states.Add(state);
            }
            var listRow = Ui.Row(list);
            listRow.Padding = new Thickness(6, 4, 6, 4);
            listRow.Margin = new Thickness(0, 0, 0, 8);
            stack.Children.Add(listRow);

            var open = new Btn();
            open.Set("Защита системы", null, true, w.OpenProtection);
            Row(stack, Text("Перед большой уборкой создайте точку восстановления", "Это делает сама Windows в окне «Защита системы» — metla.txt точками восстановления не управляет", false), open.Root);
        }

        public void CountUp() { count = w.IsVisible; }

        // Только ход работы: подпись под числом и полоса. Вызывается на каждый шаг проверки и очистки.
        public void Tick()
        {
            if (w.Busy)
            {
                Motion.Swap(sub, w.BusyLine());
                sub2.Visibility = Visibility.Collapsed;
                bar.Root.Visibility = Visibility.Visible;
                if (w.Cleaning) { bar.Busy = false; bar.Value = w.CleanTotal <= 0 ? 0 : w.CleanDone / (double)w.CleanTotal; }
                else bar.Busy = true;
                return;
            }
            bar.Busy = false;
            bar.Value = 0;
            bar.Root.Visibility = Visibility.Hidden;
        }

        public void Update()
        {
            long files;
            long total = Scanner.Total(w.Cats), chosen = Scanner.Chosen(w.Cats, out files);
            CleanResult last = w.Last;
            string cap, num, under, under2 = null;
            if (last != null)
            {
                // Итог очистки стоит здесь до следующей проверки
                long skipped = last.Skipped + last.Refused;
                cap = "Освобождено";
                num = Fmt.Size(last.Freed);
                under = "Удалено: " + Fmt.Count(last.Deleted) + (skipped > 0 ? " · пропущено: " + Fmt.Count(skipped) + " (заняты или нет доступа)" : "");
                if (total > 0) under2 = "ещё можно освободить " + Fmt.Size(total);
            }
            else if (w.Scanned)
            {
                cap = "Выбрано для очистки";
                num = Fmt.Size(chosen);
                under = total == 0 ? "Убирать нечего" : chosen + files == 0 ? "Ничего не выбрано — отметьте категории на странице «Очистка»" : Fmt.Plural(files, "объект", "объекта", "объектов");
                if (total > 0) under2 = "всего можно освободить " + Fmt.Size(total);
            }
            else
            {
                cap = "Сколько можно освободить";
                num = "—";
                under = "Нажмите «Проверить». Проверка только смотрит и ничего не удаляет";
            }
            caption.Text = cap;
            if (count && w.Scanned && last == null && !w.Busy)
            {
                count = false;
                countingUntil = DateTime.UtcNow.AddMilliseconds(Tokens.Motion.RevealMs * 2 + 60);
                Motion.Count(big, 0, chosen, delegate(double v) { return Fmt.Size((long)v); });
            }
            else if (DateTime.UtcNow >= countingUntil) big.Text = num;

            if (!w.Busy)
            {
                Motion.Swap(sub, under);
                sub2.Text = under2 ?? "";
                sub2.Visibility = under2 == null ? Visibility.Collapsed : Visibility.Visible;
            }
            Tick();

            // Разбивка по категориям — один раз на итог
            if (shownLast != last)
            {
                shownLast = last;
                parts.Children.Clear();
                if (last != null)
                    foreach (CleanPart p in last.Parts)
                    {
                        var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 2) };
                        var s = Ui.T(Fmt.Size(p.Freed), Tokens.Size.Caption, Ui.Label, FontWeights.Normal);
                        s.MinWidth = 70; s.TextAlignment = TextAlignment.Right;
                        DockPanel.SetDock(s, Dock.Right);
                        line.Children.Add(s);
                        var n = Ui.T("удалено " + Fmt.Count(p.Deleted) + (p.Skipped > 0 ? ", пропущено " + Fmt.Count(p.Skipped) : ""), Tokens.Size.Micro, Ui.Label3, FontWeights.Normal);
                        n.Margin = new Thickness(12, 0, 0, 0); n.VerticalAlignment = VerticalAlignment.Center;
                        DockPanel.SetDock(n, Dock.Right);
                        line.Children.Add(n);
                        var t = Ui.T(p.Title, Tokens.Size.Caption, Ui.Label2, FontWeights.Normal);
                        Tip(t, p.Title);
                        line.Children.Add(t);
                        parts.Children.Add(line);
                    }
                parts.Visibility = parts.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            // Диск
            disk.Visibility = w.DiskTotal > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (w.DiskTotal > 0)
            {
                long used = w.DiskTotal - w.DiskFree;
                diskTitle.Text = "Диск " + w.DiskName;
                diskFree.Text = "Свободно " + Fmt.Size(w.DiskFree);
                diskBar.Value = used / (double)w.DiskTotal;
                diskLegend.Text = "Занято " + Fmt.Size(used) + " из " + Fmt.Size(w.DiskTotal);
            }

            // Категории
            listTitle.Text = w.Scanned ? "Что нашлось" : "Что будет проверено";
            for (int i = 0; i < ids.Count; i++)
            {
                Category c = Scanner.Find(w.Cats, ids[i]);
                if (c == null) continue;
                sizes[i].Text = !w.Scanned || c.NeedsAdmin ? "—" : Fmt.Size(c.Bytes);
                sizes[i].Foreground = c.Chosen ? Ui.Label : Ui.Label2;
                sizes[i].FontWeight = c.Chosen ? FontWeights.SemiBold : FontWeights.Normal;
                states[i].Text = StateOf(w, c);
            }
        }
    }

    // «Очистка»: категории с переключателями; каждую можно раскрыть и увидеть самые большие файлы
    class CleanPage : Page
    {
        class CatRow
        {
            public string Id;
            public Border Row;
            public TextBlock Arrow, Hint, Size, Count, Empty;
            public RotateTransform Turn;
            public Switch Sw;
            public Btn Folder;
            public StackPanel Items;
            public int ItemsVersion = -1;   // для какого набора данных построен список файлов
            public bool CanOpen;
        }

        const string AdminLine = "Нужны права администратора — metla.txt их не запрашивает";
        readonly HashSet<string> open = new HashSet<string>();
        readonly List<CatRow> rows = new List<CatRow>();

        public CleanPage(MainWindow w) : base(w)
        {
            var intro = Ui.T("Удаляется только то, что включено здесь, и только после вопроса с точными числами. Раскройте строку, чтобы увидеть самые большие файлы.", Tokens.Size.Body, Ui.Label2, FontWeights.Normal);
            intro.TextWrapping = TextWrapping.Wrap; intro.Margin = new Thickness(4, 2, 4, 12);
            stack.Children.Add(intro);
            foreach (Category c in w.Cats) rows.Add(Make(c));
        }

        Category Cat(string id) { return Scanner.Find(w.Cats, id); }

        CatRow Make(Category c0)
        {
            string id = c0.Id;
            var r = new CatRow { Id = id, Turn = new RotateTransform(0) };
            r.Arrow = new TextBlock { Text = "\uE76C", FontFamily = Ui.IconFont, FontSize = Tokens.Size.Micro, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), RenderTransform = r.Turn, RenderTransformOrigin = new Point(0.5, 0.5) };
            var title = Ui.T(c0.Title, Tokens.Size.Body, Ui.Label, FontWeights.Normal);
            Tip(title, c0.Title);
            r.Hint = Ui.T("", Tokens.Size.Micro, Ui.Label3, FontWeights.Normal);
            r.Hint.TextWrapping = TextWrapping.Wrap; r.Hint.Margin = new Thickness(0, 1, 0, 0);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), Children = { title, r.Hint } };

            r.Size = Ui.T("—", Tokens.Size.Body, Ui.Label, FontWeights.SemiBold);
            r.Size.TextAlignment = TextAlignment.Right;
            r.Count = Ui.T("", Tokens.Size.Micro, Ui.Label3, FontWeights.Normal);
            r.Count.TextAlignment = TextAlignment.Right;
            var numbers = new StackPanel { Width = 104, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, Children = { r.Size, r.Count } };

            // Справа — что-то одно: переключатель, «пусто», кнопка папки или ничего (нужны права)
            r.Sw = new Switch(c0.On, delegate(bool on)
            {
                Category c = Cat(id);
                // Пока идёт проверка или очистка, выбор не меняется: чистится ровно то, что было в вопросе
                if (c == null || w.Busy) { if (c != null) r.Sw.Value = c.On; return; }
                c.On = on;
                w.RenderAll();
            });
            r.Sw.Root.HorizontalAlignment = HorizontalAlignment.Right;
            r.Empty = Ui.T("пусто", Tokens.Size.Caption, Ui.Label3, FontWeights.Normal);
            r.Empty.HorizontalAlignment = HorizontalAlignment.Right; r.Empty.VerticalAlignment = VerticalAlignment.Center; r.Empty.Visibility = Visibility.Collapsed;
            r.Folder = new Btn();
            r.Folder.Set("Открыть папку", null, true, delegate { Category c = Cat(id); if (c != null) w.OpenFolder(c.OpenFolder); });
            r.Folder.Root.Visibility = Visibility.Collapsed;
            var cell = new Grid { MinWidth = 40, VerticalAlignment = VerticalAlignment.Center, Children = { r.Sw.Root, r.Empty, r.Folder.Root } };

            var head = new DockPanel { LastChildFill = true, MinHeight = 30, Background = Brushes.Transparent };
            DockPanel.SetDock(cell, Dock.Right); head.Children.Add(cell);
            DockPanel.SetDock(numbers, Dock.Right); head.Children.Add(numbers);
            DockPanel.SetDock(r.Arrow, Dock.Left); head.Children.Add(r.Arrow);
            head.Children.Add(text);

            r.Items = new StackPanel { Margin = new Thickness(22, 8, 0, 0), Visibility = Visibility.Collapsed };
            r.Row = Ui.Row(new StackPanel { Children = { head, r.Items } });
            r.Row.Margin = new Thickness(0, 0, 0, 8);
            // Вся строка раскрывается щелчком, Enter или пробелом; переключатель и кнопка щелчок до строки не пропускают
            r.Row.MouseEnter += delegate { if (r.CanOpen) r.Row.Background = Ui.GlassHover; };
            r.Row.MouseLeave += delegate { r.Row.Background = Ui.CardFill; };
            r.Row.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if (!r.CanOpen) return; e.Handled = true; Toggle(r); };
            Ui.Keyboard(r.Row, delegate { Toggle(r); });
            r.Row.Focusable = false;
            stack.Children.Add(r.Row);
            return r;
        }

        void Toggle(CatRow r)
        {
            if (!r.CanOpen) return;
            if (!open.Remove(r.Id)) open.Add(r.Id);
            Apply(r, Cat(r.Id));
        }

        public void Expand(string id) { open.Add(id); Update(); }

        // Показать строку категории: страница прокручивается к ней
        public void Reveal(string id)
        {
            CatRow r = rows.Find(delegate(CatRow x) { return x.Id == id; });
            if (r == null) return;
            r.Row.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(delegate { r.Row.BringIntoView(); }));
        }

        // Самые большие — чтобы было видно, что это за файлы
        void Fill(CatRow r, Category c)
        {
            r.ItemsVersion = w.CatsVersion;
            r.Items.Children.Clear();
            r.Items.Children.Add(new Border { Height = 1, Background = Ui.Separator, Margin = new Thickness(0, 0, 0, 6) });
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
                Tip(name, file);
                DockPanel.SetDock(name, Dock.Left);
                line.Children.Add(name);
                var where = Ui.T(dir, Tokens.Size.Micro, Ui.Label3, FontWeights.Normal);
                where.VerticalAlignment = VerticalAlignment.Center;
                where.ToolTip = it.Path;
                line.Children.Add(where);
                r.Items.Children.Add(line);
            }
            if (c.Count > c.Top.Count)
            {
                var more = Ui.T("и ещё " + Fmt.Files(c.Count - c.Top.Count) + " поменьше", Tokens.Size.Micro, Ui.Label3, FontWeights.Normal);
                more.Margin = new Thickness(0, 4, 0, 0);
                r.Items.Children.Add(more);
            }
        }

        // Надписи, переключатель и раскрытие одной строки — на месте
        void Apply(CatRow r, Category c)
        {
            if (c == null) return;
            bool scanned = w.Scanned;
            r.CanOpen = scanned && !c.NeedsAdmin && c.Top.Count > 0;
            bool isOpen = r.CanOpen && open.Contains(r.Id);

            string hint = c.Hint;
            if (c.NeedsAdmin) hint = AdminLine;
            else if (scanned && c.Note != null) hint += ". " + c.Note;
            r.Hint.Text = hint;
            r.Size.Text = !scanned || c.NeedsAdmin ? "—" : Fmt.Size(c.Bytes);
            r.Count.Text = c.IsRecycle ? Fmt.Plural(c.Count, "объект", "объекта", "объектов") : Fmt.Files(c.Count);
            r.Count.Visibility = scanned && !c.NeedsAdmin ? Visibility.Visible : Visibility.Collapsed;

            bool empty = IsEmpty(w, c), sw = c.Selectable && !c.NeedsAdmin && !empty;
            r.Folder.Root.Visibility = !c.Selectable ? Visibility.Visible : Visibility.Collapsed;
            r.Empty.Visibility = c.Selectable && !c.NeedsAdmin && empty ? Visibility.Visible : Visibility.Collapsed;
            r.Sw.Root.Visibility = sw ? Visibility.Visible : Visibility.Collapsed;
            r.Sw.Value = c.On;
            bool live = sw && !w.Busy;   // пока идёт работа, выбор заморожен
            r.Sw.Root.IsHitTestVisible = live;
            r.Sw.Root.Focusable = live;
            r.Sw.Root.Opacity = live ? 1 : 0.5;

            r.Arrow.Foreground = r.CanOpen ? Ui.Label2 : Brushes.Transparent;
            r.Row.Cursor = r.CanOpen ? Cursors.Hand : null;
            r.Row.Focusable = r.CanOpen;
            if (!r.CanOpen || !r.Row.IsMouseOver) r.Row.Background = Ui.CardFill;

            if (isOpen && r.ItemsVersion != w.CatsVersion) Fill(r, c);
            double angle = isOpen ? 90 : 0;
            if (Ui.Live(r.Arrow)) r.Turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(angle, TimeSpan.FromMilliseconds(Tokens.Motion.FastMs)) { EasingFunction = Ui.EaseOut });
            else { r.Turn.BeginAnimation(RotateTransform.AngleProperty, null); r.Turn.Angle = angle; }
            Motion.Show(r.Items, isOpen);
        }

        public void Update()
        {
            foreach (CatRow r in rows) Apply(r, Cat(r.Id));
        }
    }

    // «Автозапуск»: что запускается вместе с Windows. Свои записи можно отключить, общие — только посмотреть.
    class StartupPage : Page
    {
        readonly LoadBox load = new LoadBox(new Thickness(4, 2, 4, 14));
        readonly StackPanel body = new StackPanel();
        readonly List<KeyValuePair<StartupEntry, Switch>> switches = new List<KeyValuePair<StartupEntry, Switch>>();
        List<StartupEntry> built;   // для какого списка построены строки

        public StartupPage(MainWindow w) : base(w)
        {
            stack.Children.Add(load.Root);
            stack.Children.Add(body);
        }

        public override Panel Entrance { get { return body; } }

        void Entry(StartupEntry entry)
        {
            StartupEntry e = entry;
            // Понятное имя из файла программы; имя значения в реестре остаётся в подсказке
            string name = string.IsNullOrEmpty(e.Description) ? e.Name : e.Description;
            Border tile = IconTile(name, e.Icon, 36);
            string hint = string.IsNullOrEmpty(e.Publisher) ? e.Command : e.Publisher;
            var left = new DockPanel { LastChildFill = true, Background = Brushes.Transparent };
            DockPanel.SetDock(tile, Dock.Left);
            left.Children.Add(tile);
            StackPanel text = Text(name, hint, false);
            foreach (UIElement t in text.Children) ((TextBlock)t).ToolTip = null;   // подсказка у строки одна — строка запуска целиком
            left.Children.Add(text);
            left.ToolTip = (name != e.Name ? "Запись: " + e.Name + "\n" : "") + e.Command;

            FrameworkElement control;
            if (e.Editable)
            {
                var sw = new Switch(e.Enabled, delegate(bool on) { w.ToggleStartup(e, on); });
                switches.Add(new KeyValuePair<StartupEntry, Switch>(e, sw));
                control = sw.Root;
            }
            else
            {
                control = Ui.T(e.Enabled ? "включено" : "отключено", Tokens.Size.Caption, Ui.Label3, FontWeights.Normal);
                left.Opacity = 0.6;
            }
            Row(body, left, control);
        }

        void Build()
        {
            built = w.StartupItems;
            body.Children.Clear();
            switches.Clear();
            if (built == null) return;
            List<StartupEntry> mine = built.FindAll(delegate(StartupEntry e) { return e.Editable; });
            List<StartupEntry> shared = built.FindAll(delegate(StartupEntry e) { return !e.Editable; });

            Title(body, "Запускается для вас");
            if (mine.Count == 0) Row(body, Text("Здесь пусто", "Для вашей учётной записи вместе с Windows ничего не запускается", false));
            foreach (StartupEntry e in mine) Entry(e);

            if (shared.Count > 0)
            {
                Title(body, "Общие для всех пользователей");
                var note = Ui.T("Только для просмотра: они меняются в Диспетчере задач с правами администратора.", Tokens.Size.Caption, Ui.Label3, FontWeights.Normal);
                note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(4, -4, 4, 10);
                body.Children.Add(note);
                foreach (StartupEntry e in shared) Entry(e);
            }
        }

        public void Update()
        {
            load.Set(w.LoadingStartup, w.StartupItems == null);
            if (built != w.StartupItems) Build();
            // Переключатель показывает то, что получилось на самом деле (после ошибки возвращается назад)
            foreach (KeyValuePair<StartupEntry, Switch> p in switches) p.Value.Value = p.Key.Enabled;
        }
    }

    // «Программы»: список установленного, поиск и порядок. «Удалить» запускает собственную программу удаления.
    class ProgramsPage : Page
    {
        public readonly ScrollViewer Scroll;
        readonly StackPanel list = new StackPanel();
        readonly Field search = new Field("Поиск программ");
        readonly TextBlock found = Ui.T("", Tokens.Size.Caption, Ui.Label3, FontWeights.Normal);
        readonly LoadBox load = new LoadBox(new Thickness(36, 0, 36, 12));
        readonly Dictionary<Prog, Border> rows = new Dictionary<Prog, Border>();
        readonly Dictionary<Prog, Border> tiles = new Dictionary<Prog, Border>();
        Border nothing;
        List<Prog> built;   // для какого списка построены строки
        int order = Programs.BySize;

        public ProgramsPage(MainWindow w) : base(w)
        {
            search.Changed = delegate { Arrange(); };
            search.Root.MaxWidth = 320;
            found.VerticalAlignment = VerticalAlignment.Center; found.Margin = new Thickness(14, 0, 14, 0);
            var seg = new Seg(new string[] { "По размеру", "По дате", "По имени" }, 0, delegate(int i) { order = i == 1 ? Programs.ByDate : i == 2 ? Programs.ByName : Programs.BySize; Arrange(); });
            var tools = new Grid { Margin = new Thickness(32, 2, 32, 12) };
            tools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 320 });
            tools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(found, 1); Grid.SetColumn(seg.Root, 2);
            tools.Children.Add(search.Root); tools.Children.Add(found); tools.Children.Add(seg.Root);

            // Поиск и порядок стоят на месте, прокручивается только список
            Scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(25, 0, 25, 0), Content = list, Focusable = false };
            list.Margin = new Thickness(7, 0, 7, 12);
            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(tools, Dock.Top);
            dock.Children.Add(tools);
            DockPanel.SetDock(load.Root, Dock.Top);
            dock.Children.Add(load.Root);
            dock.Children.Add(Scroll);
            Root.Padding = new Thickness(0);
            Root.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Root.Content = dock;
        }

        public override Panel Entrance { get { return list; } }

        Border MakeRow(Prog p)
        {
            string hint = p.Publisher ?? "";
            if (!string.IsNullOrEmpty(p.Version)) hint += (hint == "" ? "" : " · ") + "версия " + p.Version;
            var b = new Btn();
            b.Set("Удалить", null, true, delegate { w.AskUninstall(p); });
            b.Root.MinWidth = 84;
            b.Root.Visibility = Visibility.Hidden;   // место занято всегда, кнопка видна только на строке под курсором или в фокусе
            TextBlock date = Side(p.Installed.HasValue ? p.Installed.Value.ToString("d MMM yyyy", Ru) : "—", 96, Ui.Label3, Tokens.Size.Caption);
            TextBlock size = Side(p.Bytes >= 0 ? Fmt.Size(p.Bytes) : "—", 76, Ui.Label2, Tokens.Size.Body);
            Border tile = IconTile(p.Name, p.Icon, 32);
            tiles[p] = tile;
            var left = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(tile, Dock.Left);
            left.Children.Add(tile);
            left.Children.Add(Text(p.Name, hint, false));
            var holder = new StackPanel();
            Border row = Row(holder, left, b.Root, date, size);
            holder.Children.Clear();
            Action sync = delegate
            {
                bool on = row.IsMouseOver || row.IsKeyboardFocusWithin;
                b.Root.Visibility = on ? Visibility.Visible : Visibility.Hidden;
                row.Background = row.IsMouseOver ? Ui.GlassHover : Ui.CardFill;
            };
            row.MouseEnter += delegate { sync(); };
            row.MouseLeave += delegate { sync(); };
            row.IsKeyboardFocusWithinChanged += delegate { sync(); };
            Ui.Keyboard(row, delegate { w.AskUninstall(p); });
            return row;
        }

        void Build()
        {
            built = w.Progs;
            rows.Clear();
            tiles.Clear();
            if (built != null) foreach (Prog p in built) rows[p] = MakeRow(p);
            Arrange();
        }

        // Поиск и порядок: готовые строки переставляются, заново не строятся
        void Arrange()
        {
            list.Children.Clear();
            if (built == null) { found.Text = ""; return; }
            List<Prog> view = Programs.View(built, search.Text, order);
            if (view.Count == 0)
            {
                if (nothing == null) { var holder = new StackPanel(); nothing = Row(holder, Text("Ничего не нашлось", "Попробуйте другое слово", false)); holder.Children.Clear(); }
                list.Children.Add(nothing);
            }
            foreach (Prog p in view) list.Children.Add(rows[p]);
            found.Text = search.Text.Trim().Length == 0 ? "" : "Найдено " + Fmt.Count(view.Count) + " из " + Fmt.Count(built.Count);
            Scroll.ScrollToVerticalOffset(0);
        }

        // Значки достались в фоне — ставим их на плитки на месте
        public void IconsReady()
        {
            foreach (KeyValuePair<Prog, Border> p in tiles) if (p.Key.Icon != null) SetIcon(p.Value, p.Key.Name, p.Key.Icon);
        }

        public void Update()
        {
            load.Set(w.LoadingProgs, w.Progs == null);
            if (built != w.Progs) Build();
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
            w.CatsVersion++;
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
                new StartupEntry { Name = "Steam", Description = "Steam Client Bootstrapper", Publisher = "Valve Corporation", Command = @"""C:\Program Files (x86)\Steam\steam.exe"" -silent", Editable = true, Enabled = false },
                new StartupEntry { Name = "Заметки", Command = @"C:\Users\вы\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\Заметки.lnk", Editable = true, Enabled = true, Folder = true },
                new StartupEntry { Name = "SecurityHealth", Description = "Windows Security notification icon", Publisher = "Microsoft Corporation", Command = @"%windir%\system32\SecurityHealthSystray.exe", Enabled = true },
                new StartupEntry { Name = "RtkAudUService", Description = "Realtek HD Audio Universal Service", Publisher = "Realtek Semiconductor", Command = @"""C:\Windows\System32\RtkAudUService64.exe"" -background", Enabled = true }
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

        // В --demo ничего не удаляется: категории из снимка просто обнуляются на экране
        public static CleanResult Clean(MainWindow w, ICollection<string> ids)
        {
            var r = new CleanResult();
            foreach (Category c in w.Cats)
            {
                if (!ids.Contains(c.Id) || !c.Cleanable || c.Bytes + c.Count == 0) continue;
                long skipped = c.Id == "thumbs" ? 2 : c.Id == "temp" ? 10 : 0;
                r.Parts.Add(new CleanPart { Id = c.Id, Title = c.Title, Freed = c.Bytes, Deleted = c.Count - skipped, Skipped = skipped });
                r.Freed += c.Bytes; r.Deleted += c.Count - skipped; r.Skipped += skipped;
                Set(c, 0, 0);
            }
            w.CatsVersion++;
            return r;
        }
    }
}

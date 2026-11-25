// metla.txt. Bin.cs — корзина Windows для дока dock.txt: сколько в ней лежит («metla.bin») и открыть её (bin.open).
// Очистить корзину из дока можно только через окно metla.txt и его обычный вопрос (show { page: "bin" }, MainWindow.AskBin).
// Число и размер берутся у Windows (SHQueryRecycleBin, см. Shell в Scan.cs) и уходят в хаб, только когда изменились.
// О переменах говорит сама оболочка Windows (SHChangeNotifyRegister на корзину); уведомления идут пачками,
// поэтому пересчёт — через полсекунды тишины и в фоновом потоке: на большой корзине Windows считает не сразу.
// Если уведомления почему-то не пришли — раз в минуту пересчёт всё равно идёт.
// В проверках вместо настоящей корзины — подмена (BinSource), к настоящей они не обращаются.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;

namespace DevTxt.Metla
{
    // Откуда числа и кто открывает корзину: настоящая или подмена в проверках. Чистить отсюда нельзя:
    // команды хаба, которая чистит, нет (CLAUDE.md) — док открывает metla.txt с вопросом (show { page: "bin" })
    class BinSource
    {
        public Func<long[]> Query;   // { объектов, байт }; null — узнать не удалось
        public Action Open;

        public static BinSource Real()
        {
            var s = new BinSource();
            s.Query = delegate
            {
                long bytes, count;
                return Shell.QueryRecycle(out bytes, out count) ? new long[] { count, bytes } : null;
            };
            s.Open = delegate
            {
                Process p = Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });
                if (p != null) p.Dispose();
            };
            return s;
        }
    }

    // Отложенный вызов: сколько бы поводов ни пришло подряд, вызов один — через quiet мс после последнего
    class Debounce : IDisposable
    {
        readonly Timer timer;
        readonly int quiet;

        public Debounce(int quietMs, Action fire)
        {
            quiet = quietMs;
            timer = new Timer(delegate { try { fire(); } catch { } }, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Poke() { try { timer.Change(quiet, Timeout.Infinite); } catch (ObjectDisposedException) { } }
        public void Dispose() { timer.Dispose(); }
    }

    class BinWatch
    {
        public const int QuietMs = 500, BackupMs = 60000;
        const uint WmBin = 0x0400 + 0x51;   // WM_USER + своё: так оболочка сообщает о переменах в корзине

        readonly BinSource src;
        readonly Action<Dictionary<string, object>> publish;
        readonly object gate = new object();
        long lastCount = -1, lastBytes = -1;   // что ушло в хаб последним; −1 — ещё ничего
        Debounce debounce;
        Timer backup;
        HwndSource window;
        uint reg;
        IntPtr pidl;
        int recounts;

        public BinWatch(BinSource source, Action<Dictionary<string, object>> publish) { src = source; this.publish = publish; }

        public int Recounts { get { return recounts; } }   // для проверок: сколько раз корзину пересчитали

        // Состояние «metla.bin»: всегда оба поля, целые; пустая корзина — нули
        public static Dictionary<string, object> Data(long count, long bytes)
        {
            return HubLink.Msg("count", Math.Max(0L, count), "bytes", Math.Max(0L, bytes));
        }

        // shell — подписаться на уведомления оболочки; только в потоке окна (нужна очередь сообщений)
        public void Start(bool shell)
        {
            if (debounce != null) return;
            debounce = new Debounce(QuietMs, delegate { Recount(); });
            backup = new Timer(delegate { try { Recount(); } catch { } }, null, BackupMs, BackupMs);
            if (shell) Listen();
        }

        public void Stop()
        {
            try { if (reg != 0) SHChangeNotifyDeregister(reg); } catch { }
            reg = 0;
            if (pidl != IntPtr.Zero) { Marshal.FreeCoTaskMem(pidl); pidl = IntPtr.Zero; }
            try { if (window != null) window.Dispose(); } catch { }
            window = null;
            if (debounce != null) debounce.Dispose();
            if (backup != null) backup.Dispose();
        }

        // Корзина, похоже, изменилась: пересчёт после тишины
        public void Poke() { if (debounce != null) debounce.Poke(); }

        // Пересчитать сейчас, в вызывающем потоке; в хаб — только если число или размер изменились
        public void Recount()
        {
            lock (gate)
            {
                Interlocked.Increment(ref recounts);
                long[] now = src.Query();
                if (now == null) return;   // Windows не ответила — остаётся то, что ушло раньше
                long count = Math.Max(0L, now[0]), bytes = Math.Max(0L, now[1]);
                if (count == lastCount && bytes == lastBytes) return;
                lastCount = count; lastBytes = bytes;
                publish(Data(count, bytes));
            }
        }

        // Команда bin.open
        public void Open()
        {
            try { src.Open(); }
            catch { throw new Exception("не удалось открыть корзину"); }
        }

        // ───────── Уведомления оболочки Windows о корзине ─────────
        void Listen()
        {
            try
            {
                const int CsidlBitbucket = 0x000a;
                const int InterruptLevel = 0x0001, ShellLevel = 0x0002, NewDelivery = 0x8000;
                const int AllEvents = 0x7FFFFFFF;
                if (SHGetSpecialFolderLocation(IntPtr.Zero, CsidlBitbucket, out pidl) != 0) { pidl = IntPtr.Zero; return; }
                // Окно только для сообщений: его не видно и в панели задач его нет
                window = new HwndSource(new HwndSourceParameters("metla.bin") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
                window.AddHook(Hook);
                var entry = new SHChangeNotifyEntry { pidl = pidl, fRecursive = true };
                reg = SHChangeNotifyRegister(window.Handle, ShellLevel | InterruptLevel | NewDelivery, AllEvents, WmBin, 1, ref entry);
            }
            catch { reg = 0; }   // без уведомлений остаётся пересчёт раз в минуту
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if ((uint)msg != WmBin) return IntPtr.Zero;
            handled = true;
            // При NewDelivery сообщение надо «взять» и отпустить, иначе память уведомления не освободится
            try
            {
                IntPtr pidls; int ev;
                IntPtr lk = SHChangeNotification_Lock(wParam, (uint)lParam.ToInt64(), out pidls, out ev);
                if (lk != IntPtr.Zero) SHChangeNotification_Unlock(lk);
            }
            catch { }
            Poke();
            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SHChangeNotifyEntry { public IntPtr pidl; [MarshalAs(UnmanagedType.Bool)] public bool fRecursive; }

        [DllImport("shell32.dll")]
        static extern uint SHChangeNotifyRegister(IntPtr hwnd, int sources, int events, uint msg, int count, ref SHChangeNotifyEntry entry);
        [DllImport("shell32.dll")]
        static extern bool SHChangeNotifyDeregister(uint id);
        [DllImport("shell32.dll")]
        static extern int SHGetSpecialFolderLocation(IntPtr hwnd, int csidl, out IntPtr pidl);
        [DllImport("shell32.dll")]
        static extern IntPtr SHChangeNotification_Lock(IntPtr change, uint procId, out IntPtr pidls, out int ev);
        [DllImport("shell32.dll")]
        static extern bool SHChangeNotification_Unlock(IntPtr lk);
    }
}

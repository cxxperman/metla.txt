// metla.txt. SelfTest.cs — самопроверка (--selftest). Правила удаления проверяются на собственном выдуманном
// дереве во временной папке; настоящие файлы, корзина и реестр не трогаются. Всё, что удаляет проверка, лежит
// внутри «ограды» (Safe.Fence) — её собственной папки.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Threading;

namespace DevTxt.Metla
{
    static class SelfTest
    {
        static int failed, passed;
        static void Check(bool ok, string what) { if (ok) passed++; else { failed++; Console.Error.WriteLine("FAIL  " + what); } }

        static bool Throws(Action a) { try { a(); return false; } catch { return true; } }

        static Dictionary<string, object> FirstItem(Dictionary<string, object> settings)
        {
            var groups = settings["groups"] as object[];
            var items = ((Dictionary<string, object>)groups[0])["items"] as object[];
            return (Dictionary<string, object>)items[0];
        }

        public static int Run()
        {
            Prefs.Persist = false;
            Prefs.Remind = false;

            Check(System.Text.RegularExpressions.Regex.IsMatch(Program.Id, "^[a-z][a-z0-9-]{0,31}$") && Program.Id != "hub", "app id fits the hub rule");
            foreach (string row in MainWindow.Sprite) Check(row.Length == MainWindow.Sprite[0].Length, "sprite rows are of equal length");
            Check(string.Join("", MainWindow.Sprite).Contains("E"), "the sprite has an eye");

            Formatting();
            Pages();
            try { Tree(); } catch (Exception e) { Check(false, "the fake tree test threw: " + e); }
            Safe.Armed = false; Safe.Fence = null;
            StartupApproved();
            UninstallEntries();
            HubAndPrefs();
            DevAppsChecks();
            TrayAndFamily();
            BinChecks();
            TrayHoldChecks();
            Reminders();
            Check(!Safe.Armed && Safe.Fence == null, "deletion is switched off again after every check");

            Console.WriteLine(failed == 0 ? "all passed (" + passed + " checks)" : failed + " failed, " + passed + " passed");
            return failed == 0 ? 0 : 1;
        }

        static void Formatting()
        {
            Check(Fmt.Size(0) == "0 Б" && Fmt.Size(512) == "512 Б" && Fmt.Size(1023) == "1023 Б", "bytes: Б");
            Check(Fmt.Size(1024) == "1 КБ" && Fmt.Size(300 * 1024) == "300 КБ", "kilobytes: КБ, got " + Fmt.Size(1024));
            Check(Fmt.Size(1024 * 1024 * 3 / 2) == "1,5 МБ" && Fmt.Size(250L * 1024 * 1024) == "250 МБ", "megabytes: МБ with a comma, got " + Fmt.Size(1024 * 1024 * 3 / 2));
            Check(Fmt.Size(5 * Fmt.GB) == "5 ГБ" && Fmt.Size(Fmt.GB * 5 / 4) == "1,25 ГБ" && Fmt.Size(Fmt.GB * 124 / 10) == "12,4 ГБ", "gigabytes: ГБ, got " + Fmt.Size(Fmt.GB * 124 / 10));
            Check(Fmt.Size(-5) == "0 Б", "a negative size is shown as zero");
            Check(Fmt.Files(1) == "1 файл" && Fmt.Files(2) == "2 файла" && Fmt.Files(5) == "5 файлов" && Fmt.Files(11) == "11 файлов" && Fmt.Files(21) == "21 файл" && Fmt.Files(1234) == "1 234 файла", "plural forms, got " + Fmt.Files(1234));
        }

        static void Pages()
        {
            Check(Program.PageIndex("обзор") == 0 && Program.PageIndex("очистка") == 1 && Program.PageIndex("автозапуск") == 2 && Program.PageIndex("программы") == 3, "--page understands Russian names");
            Check(Program.PageIndex("overview") == 0 && Program.PageIndex("cleanup") == 1 && Program.PageIndex("startup") == 2 && Program.PageIndex("programs") == 3 && Program.PageIndex("Autostart") == 2, "--page understands ASCII names");
            Check(Program.PageIndex("nope") < 0 && Program.PageIndex(null) < 0, "--page refuses an unknown name");

            List<Category> cats = Scanner.Categories();
            Check(Scanner.Find(cats, "temp").On && Scanner.Find(cats, "wintemp").On && Scanner.Find(cats, "dumps").On && Scanner.Find(cats, "thumbs").On, "temp, dumps and thumbnails are selected by default");
            Check(!Scanner.Find(cats, "browsers").On && !Scanner.Find(cats, "recycle").On, "browser cache and recycle bin are not selected by default");
            Check(!Scanner.Find(cats, "downloads").Selectable && !Scanner.Find(cats, "downloads").Cleanable, "downloads can never be selected");
        }

        // ───────── Выдуманное дерево ─────────
        static string basePath;
        static void Make(string rel, int size, double ageDays)
        {
            string p = Path.Combine(basePath, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllBytes(p, new byte[size]);
            if (ageDays > 0) Age(p, ageDays);
        }
        static void Age(string p, double days)
        {
            DateTime t = DateTime.UtcNow.AddDays(-days);
            File.SetCreationTimeUtc(p, t);
            File.SetLastWriteTimeUtc(p, t);
        }
        static bool Has(string rel) { return File.Exists(Path.Combine(basePath, rel)); }
        static bool HasDir(string rel) { return Directory.Exists(Path.Combine(basePath, rel)); }

        // Все файлы дерева, не заходя в ссылки
        static void ListFiles(string dir, List<string> into)
        {
            foreach (string f in Directory.GetFiles(dir)) into.Add(f.Substring(basePath.Length + 1));
            foreach (string d in Directory.GetDirectories(dir))
                if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0) ListFiles(d, into);
        }

        static void Tree()
        {
            string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\');
            basePath = Path.Combine(tempRoot, "metla-selftest-" + Process.GetCurrentProcess().Id);
            if (Directory.Exists(basePath)) throw new Exception("test folder already exists");
            Directory.CreateDirectory(basePath);
            const string T = @"user\AppData\Local\Temp\", L = @"user\AppData\Local\", Chrome = @"user\AppData\Local\Google\Chrome\User Data\";
            string link = Path.Combine(basePath, T + "link");
            FileStream held = null;
            try
            {
                // Временные файлы пользователя
                Make(T + "old.tmp", 1000, 3);
                Make(T + "fresh.tmp", 2000, 0);
                Make(T + @"sub\old2.log", 300, 3);
                Make(T + @"sub\fresh2.log", 400, 0);
                Make(T + "locked.tmp", 500, 3);
                Make(T + "readonly.tmp", 600, 3);
                Make(T + "touched.tmp", 700, 3);
                Make(T + "almost.tmp", 50, 23.0 / 24);
                Make(T + "justover.tmp", 60, 25.0 / 24);
                Make(T + "crash.dmp", 800, 3);
                Directory.CreateDirectory(Path.Combine(basePath, T + "oldempty"));
                Directory.SetCreationTimeUtc(Path.Combine(basePath, T + "oldempty"), DateTime.UtcNow.AddDays(-3));
                Directory.SetLastWriteTimeUtc(Path.Combine(basePath, T + "oldempty"), DateTime.UtcNow.AddDays(-3));
                Make(@"user\AppData\Local\Temp2\x.txt", 10, 3);     // соседняя папка с похожим именем
                Make(@"outside\keep.txt", 900, 3);                    // то, до чего очистка добраться не должна
                // Точка соединения внутри Temp, ведущая наружу
                var mk = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + Path.Combine(basePath, "outside") + "\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
                mk.WaitForExit(10000);
                bool junction = Directory.Exists(link) && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0;
                Check(junction, "a junction pointing outside was created for the test");
                // Остальные категории
                Make(@"win\Temp\wold.tmp", 1100, 3);
                Make(@"win\Temp\wfresh.tmp", 1200, 0);
                Make(L + @"CrashDumps\app.dmp", 1300, 0);
                Make(@"programdata\Microsoft\Windows\WER\ReportArchive\r1\Report.wer", 1400, 0);
                Make(@"programdata\Microsoft\Windows\WER\ReportQueue\q1\Report.wer", 1500, 0);
                Make(L + @"Microsoft\Windows\Explorer\thumbcache_96.db", 1600, 0);
                Make(L + @"Microsoft\Windows\Explorer\iconcache_16.db", 1700, 0);
                Make(L + @"Microsoft\Windows\Explorer\other.txt", 10, 0);
                Make(Chrome + @"Default\Cookies", 11, 0);
                Make(Chrome + @"Default\History", 12, 0);
                Make(Chrome + @"Default\Login Data", 13, 0);
                Make(Chrome + @"Default\Cache\data_1", 2100, 0);
                Make(Chrome + @"Default\Code Cache\js\a", 2200, 0);
                Make(Chrome + @"Default\GPUCache\b", 2300, 0);
                Make(Chrome + @"Default\Service Worker\CacheStorage\c", 14, 0);
                Make(Chrome + @"Default\Local Storage\leveldb\d", 15, 0);
                Make(Chrome + @"Default\Cache Backup\z", 16, 0);
                Make(Chrome + @"Profile 1\Cache\data_2", 2400, 0);
                Make(Chrome + @"ShaderCache\x", 17, 0);
                Make(Chrome + @"NotAProfile\Cache\y", 18, 0);
                Make(L + @"Mozilla\Firefox\Profiles\abc.default\cache2\entries\e1", 2500, 0);
                Make(L + @"Mozilla\Firefox\Profiles\abc.default\places.sqlite", 19, 0);
                Make(L + @"Microsoft\Edge\User Data\Default\Cache\e", 2600, 0);
                Make(L + @"Microsoft\Edge\User Data\Default\Cookies", 20, 0);
                Make(@"user\Downloads\ancient.zip", 3000, 200);
                Make(@"user\Downloads\recent.zip", 3100, 30);
                File.SetAttributes(Path.Combine(basePath, T + "readonly.tmp"), FileAttributes.ReadOnly);
                held = new FileStream(Path.Combine(basePath, T + "locked.tmp"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);   // «занятый» файл: даже если его разрешили удалять

                var env = new Env
                {
                    Temp = Path.Combine(basePath, @"user\AppData\Local\Temp"), WinTemp = Path.Combine(basePath, @"win\Temp"), LocalAppData = Path.Combine(basePath, @"user\AppData\Local"),
                    ProgramData = Path.Combine(basePath, "programdata"), Downloads = Path.Combine(basePath, @"user\Downloads"), RealRecycle = false,
                    BrowserRunning = delegate(string p) { return p == "msedge"; }
                };
                string temp = env.Temp;

                // Правило возраста
                DateTime now = DateTime.UtcNow;
                Check(!Scanner.OldEnough(now.AddHours(-23.9), now, 24) && Scanner.OldEnough(now.AddHours(-24.1), now, 24) && Scanner.OldEnough(now, now, 0), "24-hour rule on plain dates");

                // Какие папки считаются кэшем браузера
                List<string> dirs = Browsers.CacheDirs(Path.Combine(basePath, Chrome.TrimEnd('\\')), false);
                var names = new List<string>();
                foreach (string d in dirs) names.Add(d.Substring(basePath.Length + 1 + Chrome.Length));
                names.Sort(StringComparer.Ordinal);
                Check(string.Join("|", names.ToArray()) == @"Default\Cache|Default\Code Cache|Default\GPUCache|Profile 1\Cache", "browser cache matching picks only cache folders, got " + string.Join("|", names.ToArray()));
                Check(Browsers.CacheDirs(Path.Combine(basePath, L + @"Mozilla\Firefox\Profiles"), true).Count == 1, "Firefox: only cache2");
                Check(Browsers.CacheDirs(Path.Combine(basePath, "no-such-folder"), false).Count == 0, "a browser that is not installed gives nothing");

                // Проверка: что нашлось
                List<Category> cats = Scanner.Categories();
                Scanner.ScanAll(cats, env, null);
                Category cTemp = Scanner.Find(cats, "temp"), cWin = Scanner.Find(cats, "wintemp"), cDumps = Scanner.Find(cats, "dumps"), cThumbs = Scanner.Find(cats, "thumbs"),
                    cBrowsers = Scanner.Find(cats, "browsers"), cBin = Scanner.Find(cats, "recycle"), cDown = Scanner.Find(cats, "downloads");
                Check(cTemp.Count == 6 && cTemp.Bytes == 1000 + 300 + 500 + 600 + 700 + 60, "user temp: only files older than 24 hours, got " + cTemp.Count + " / " + cTemp.Bytes);
                Check(!cTemp.Items.Exists(delegate(Item i) { return i.Path.Contains("keep.txt") || i.Path.Contains("fresh") || i.Path.Contains("almost") || i.Path.EndsWith(".dmp"); }), "user temp: nothing fresh, nothing through the junction, no dumps");
                Check(cWin.Count == 1 && cWin.Bytes == 1100 && !cWin.NeedsAdmin, "Windows temp: the same rule, got " + cWin.Count);
                Check(cDumps.Count == 4 && cDumps.Bytes == 1300 + 1400 + 1500 + 800, "dumps and reports, got " + cDumps.Count + " / " + cDumps.Bytes);
                Check(cThumbs.Count == 1 && cThumbs.Bytes == 1600, "thumbnail cache: only thumbcache_*.db, got " + cThumbs.Count);
                Check(cBrowsers.Count == 5 && cBrowsers.Bytes == 2100 + 2200 + 2300 + 2400 + 2500, "browser cache: only cache folders, running browser skipped, got " + cBrowsers.Count + " / " + cBrowsers.Bytes);
                Check(cBrowsers.Note != null && cBrowsers.Note.Contains("Edge"), "the running browser is named in the note");
                Check(cBin.Count == 0 && cBin.Bytes == 0, "the recycle bin is not queried in tests");
                Check(cDown.Count == 1 && cDown.Bytes == 3000, "downloads: only older than 90 days, got " + cDown.Count);
                long sum = cTemp.Bytes + cWin.Bytes + cDumps.Bytes + cThumbs.Bytes + cBrowsers.Bytes;
                Check(Scanner.Total(cats) == sum, "the total counts every cleanable category and not downloads: " + Scanner.Total(cats) + " vs " + sum);
                long files;
                Check(Scanner.Chosen(cats, out files) == cTemp.Bytes + cWin.Bytes + cDumps.Bytes + cThumbs.Bytes && files == 12, "the default selection total, files " + files);
                Check(cTemp.Top.Count == 6 && cTemp.Top[0].Size == 1000 && cTemp.Top[5].Size == 60, "the top list is sorted by size");
                var nobody = Scanner.Categories();
                var locked = new Env { Temp = env.Temp, WinTemp = Path.Combine(basePath, "no-such"), LocalAppData = env.LocalAppData, ProgramData = env.ProgramData, Downloads = env.Downloads };
                Scanner.Scan(Scanner.Find(nobody, "wintemp"), locked);
                Check(!Scanner.Find(nobody, "wintemp").NeedsAdmin && Scanner.Find(nobody, "wintemp").Count == 0, "a missing folder is simply empty");

                // Сторож удаления
                Safe.Fence = basePath;
                Safe.Armed = false;
                Check(Safe.Delete(temp, Path.Combine(temp, "old.tmp")) == Del.Refused && Has(T + "old.tmp"), "nothing is deleted while deletion is switched off (--demo, --scan-only)");
                Safe.Armed = true;
                string outside = Path.Combine(basePath, @"outside\keep.txt");
                Check(Safe.Delete(temp, outside) == Del.Refused && File.Exists(outside), "SafeDelete refuses a path outside the root");
                Check(Safe.Delete(temp, temp + @"\sub\..\..\..\..\..\outside\keep.txt") == Del.Refused && File.Exists(outside), "SafeDelete refuses .. traversal out of the root");
                Check(Safe.Delete(temp, temp + @"\sub\..\old.tmp") == Del.Refused && Has(T + "old.tmp"), "SafeDelete refuses any path with .. in it");
                Check(Safe.Delete(temp, temp) == Del.Refused && Safe.Delete(temp, temp + "\\") == Del.Refused && Directory.Exists(temp), "SafeDelete refuses the root itself");
                Check(Safe.Delete(temp, Path.Combine(link, "keep.txt")) == Del.Refused && File.Exists(outside), "SafeDelete refuses a file behind a junction");
                Check(Safe.Delete(temp, link) == Del.Refused && Directory.Exists(link), "SafeDelete refuses the junction itself");
                Check(Safe.Delete(temp, Path.Combine(basePath, @"user\AppData\Local\Temp2\x.txt")) == Del.Refused && Has(@"user\AppData\Local\Temp2\x.txt"), "SafeDelete refuses a sibling folder with a similar name");
                Check(Safe.Delete(Path.GetPathRoot(basePath), outside) == Del.Refused && File.Exists(outside), "SafeDelete refuses a drive root as the allowed folder");
                Check(Safe.Delete(null, outside) == Del.Refused && Safe.Delete(temp, null) == Del.Refused && Safe.Delete("", "") == Del.Refused && Safe.Delete(temp, "old.tmp") == Del.Refused, "SafeDelete refuses empty and relative paths");
                Check(Safe.Delete(link, Path.Combine(link, "keep.txt")) == Del.Refused && File.Exists(outside), "SafeDelete refuses a root that is itself a junction");
                Check(Safe.Delete(tempRoot, Path.Combine(tempRoot, "metla-selftest-no-such-file.tmp")) == Del.Refused, "in the self-test nothing outside its own folder can be deleted");
                Check(Safe.Delete(temp, Path.Combine(temp, "sub")) == Del.Skipped && HasDir(T + "sub"), "a folder with files in it is never deleted");
                Check(Safe.Delete(temp, Path.Combine(temp, "locked.tmp")) == Del.Skipped && Has(T + "locked.tmp"), "a file somebody holds open is skipped");
                Check(Safe.Delete(temp, Path.Combine(temp, "readonly.tmp")) == Del.Skipped && Has(T + "readonly.tmp"), "a protected file is skipped, not forced");
                string why, full;
                Check(Safe.Inside(temp, Path.Combine(temp, @"sub\old2.log"), out full, out why) && full.EndsWith(@"sub\old2.log"), "a plain file inside the root is allowed");

                // Очистка: всё включено, даже то, что включать нельзя
                foreach (Category c in cats) c.On = true;
                Age(Path.Combine(basePath, T + "touched.tmp"), 0);   // файл изменили после проверки
                var before = new List<string>();
                ListFiles(basePath, before);
                CleanResult r = Cleaner.Clean(cats, env, null);
                var after = new List<string>();
                ListFiles(basePath, after);
                after.Sort(StringComparer.Ordinal);
                var expect = new List<string>
                {
                    T + "fresh.tmp", T + @"sub\fresh2.log", T + "locked.tmp", T + "readonly.tmp", T + "touched.tmp", T + "almost.tmp",
                    @"user\AppData\Local\Temp2\x.txt", @"outside\keep.txt", @"win\Temp\wfresh.tmp",
                    L + @"Microsoft\Windows\Explorer\iconcache_16.db", L + @"Microsoft\Windows\Explorer\other.txt",
                    Chrome + @"Default\Cookies", Chrome + @"Default\History", Chrome + @"Default\Login Data", Chrome + @"Default\Service Worker\CacheStorage\c",
                    Chrome + @"Default\Local Storage\leveldb\d", Chrome + @"Default\Cache Backup\z", Chrome + @"ShaderCache\x", Chrome + @"NotAProfile\Cache\y",
                    L + @"Mozilla\Firefox\Profiles\abc.default\places.sqlite",
                    L + @"Microsoft\Edge\User Data\Default\Cache\e", L + @"Microsoft\Edge\User Data\Default\Cookies",
                    @"user\Downloads\ancient.zip", @"user\Downloads\recent.zip"
                };
                expect.Sort(StringComparer.Ordinal);
                Check(string.Join("\n", after.ToArray()) == string.Join("\n", expect.ToArray()), "after the clean exactly the expected files survive; got:\n  " + string.Join("\n  ", after.ToArray()));
                Check(before.Count - after.Count == 14, "exactly 14 files were removed, removed " + (before.Count - after.Count));
                Check(r.Deleted == 14 && r.Skipped == 3 && r.Refused == 0, "the clean reports 14 deleted, 3 skipped: " + r.Deleted + " / " + r.Skipped + " / " + r.Refused);
                Check(r.Freed == 1000 + 300 + 60 + 1100 + 1300 + 1400 + 1500 + 800 + 1600 + 2100 + 2200 + 2300 + 2400 + 2500, "freed bytes are counted from what was really deleted: " + r.Freed);
                Check(!r.RecycleEmptied, "the recycle bin is not touched in tests");
                Check(File.Exists(outside) && Directory.Exists(link), "the junction and what is behind it are intact");
                Check(!HasDir(T + "oldempty") && HasDir(T + "sub"), "an old empty folder is removed, a folder with files stays");
                Check(!HasDir(@"programdata\Microsoft\Windows\WER\ReportArchive\r1") && HasDir(@"programdata\Microsoft\Windows\WER\ReportArchive"), "an emptied report folder is removed, its root stays");
                Check(HasDir(Chrome + @"Default\Cache") && HasDir(Chrome + "Default") && HasDir(temp.Substring(basePath.Length + 1)), "roots and profiles stay in place");

                // Вторая проверка после очистки: осталось только то, что пропущено
                Scanner.ScanAll(cats, env, null);
                Check(cTemp.Count == 2 && cDumps.Count == 0 && cThumbs.Count == 0 && cBrowsers.Count == 0 && cWin.Count == 0, "a scan after the clean finds only the skipped files, temp " + cTemp.Count);

                // Снимок выбора: чистится ровно то, что было выбрано, когда открылся вопрос.
                // Категория, включённая после снимка, не очищается; «только для сведения» не очищается, даже если попала в снимок.
                Make(T + "late.tmp", 111, 3);
                Make(L + @"CrashDumps\late.dmp", 222, 3);
                List<Category> shown = Scanner.ScanFresh(cats, env, null);
                Category sTemp = Scanner.Find(shown, "temp"), sDumps = Scanner.Find(shown, "dumps"), sDown = Scanner.Find(shown, "downloads");
                Check(shown != cats && sTemp != cTemp && sTemp.On == cTemp.On && cDumps.Count == 0, "a fresh scan makes new categories, keeps the selection and leaves the old list alone");
                Check(sTemp.Count == 3 && sDumps.Count == 1, "the fresh scan sees the new files, temp " + sTemp.Count + ", dumps " + sDumps.Count);
                foreach (Category c in shown) c.On = false;
                sDumps.On = true;
                HashSet<string> snapshot = Scanner.Snapshot(shown);
                Check(snapshot.Count == 1 && snapshot.Contains("dumps"), "the snapshot holds exactly the chosen categories");
                sTemp.On = true;              // включили уже после вопроса
                sDown.On = true;
                snapshot.Add("downloads");    // даже так загрузки не удаляются
                long seen = -1, of = -1;
                CleanResult late = Cleaner.Clean(shown, snapshot, env, delegate(long done, long total) { seen = done; of = total; });
                Check(!Has(L + @"CrashDumps\late.dmp"), "the category from the snapshot is cleaned");
                Check(Has(T + "late.tmp"), "a category switched on after the snapshot is not cleaned: late.tmp survives");
                Check(Has(@"user\Downloads\ancient.zip"), "downloads survive even when forced into the snapshot");
                Check(late.Deleted == 1 && late.Freed == 222 && late.Skipped == 0, "the snapshot clean deleted exactly one file: " + late.Deleted + " / " + late.Freed);
                Check(late.Parts.Count == 1 && late.Parts[0].Id == "dumps" && late.Parts[0].Deleted == 1 && late.Parts[0].Freed == 222, "the result is broken down by category");
                Check(seen == 1 && of == 1, "progress reports files done of total: " + seen + " / " + of);
                sDumps.On = false;            // выключили после вопроса — снимок всё равно прежний, но чистить уже нечего
                Check(Cleaner.Clean(shown, new HashSet<string>(), env, null).Deleted == 0 && Has(T + "late.tmp"), "an empty snapshot cleans nothing");
            }
            finally
            {
                // Уборка за собой: сначала точка соединения (только она сама), потом всё дерево
                Safe.Armed = false;
                if (held != null) held.Dispose();
                try { if (Directory.Exists(link)) Directory.Delete(link, false); } catch { }
                try { File.SetAttributes(Path.Combine(basePath, T + "readonly.tmp"), FileAttributes.Normal); } catch { }
                bool mine = basePath.StartsWith(tempRoot + "\\", StringComparison.OrdinalIgnoreCase) && basePath.Contains("metla-selftest-");
                if (mine && !Directory.Exists(link)) try { Directory.Delete(basePath, true); } catch { }
            }
            Check(File.Exists(Path.Combine(basePath, @"outside\keep.txt")) == false && !Directory.Exists(basePath), "the test folder is removed afterwards");
        }

        static void StartupApproved()
        {
            DateTime at = new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);
            byte[] on = Approved.Encode(true, at), off = Approved.Encode(false, at);
            Check(on.Length == 12 && on[0] == 2 && Array.TrueForAll(on, delegate(byte b) { return b == 2 || b == 0; }), "enabled is 02 and zeros");
            Check(off.Length == 12 && off[0] == 3 && off[1] == 0 && off[2] == 0 && off[3] == 0, "disabled starts with 03");
            Check(Approved.Decode(on) && !Approved.Decode(off), "StartupApproved round-trips");
            Check(Approved.Decode(Approved.Encode(Approved.Decode(on), at)) && !Approved.Decode(Approved.Encode(Approved.Decode(off), at)), "and again through decode");
            Check(Approved.DisabledAt(off) == at && Approved.DisabledAt(on) == null, "the time of disabling survives");
            Check(Approved.Decode(null) && Approved.Decode(new byte[0]), "no mark means enabled");
            Check(Approved.Decode(new byte[] { 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }) && !Approved.Decode(new byte[] { 7, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 0 }), "the marks Windows itself writes (06, 07) are understood");
            Check(Throws(delegate { Startup.Set(new StartupEntry { Name = "x", ApprovedName = "x", Editable = false }, false); }), "shared entries cannot be switched");
            Check(Startup.ExeOf(@"""C:\Program Files\App\app.exe"" --tray") == @"C:\Program Files\App\app.exe" && Startup.ExeOf(@"C:\Program Files\App\app.exe -silent") == @"C:\Program Files\App\app.exe", "the program is found in a start command");
            string gone = Path.Combine(Path.GetTempPath(), "metla-no-such-" + Process.GetCurrentProcess().Id, "app.exe");
            Check(Startup.Missing(gone) && !Startup.Missing(Process.GetCurrentProcess().MainModule.FileName), "an entry whose program file is gone is noticed");
            Check(!Startup.Missing("rundll32.exe") && !Startup.Missing(null) && !Startup.Missing(""), "a bare file name is found by Windows itself and is not «missing»");
            Check(!Startup.Missing(@"C:\Program") && !Startup.Missing(Path.GetTempPath()), "a path cut at a space and a folder are not «missing»");
        }

        static Dictionary<string, object> Entry(params object[] pairs)
        {
            var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i + 1 < pairs.Length; i += 2) d[(string)pairs[i]] = pairs[i + 1];
            return d;
        }

        static void UninstallEntries()
        {
            Prog p = Programs.FromValues(Entry("DisplayName", " Игра ", "Publisher", "Студия", "DisplayVersion", "1.2", "UninstallString", @"""C:\Games\Игра\unins000.exe"" /SILENT", "EstimatedSize", 2048, "InstallDate", "20260314"));
            Check(p != null && p.Name == "Игра" && p.Publisher == "Студия" && p.Bytes == 2048L * 1024 && p.Installed == new DateTime(2026, 3, 14), "a normal entry is read");
            Check(Programs.FromValues(Entry("DisplayName", "Часть системы", "UninstallString", "x.exe", "SystemComponent", 1)) == null, "system components are hidden");
            Check(Programs.FromValues(Entry("DisplayName", "Без удаления")) == null && Programs.FromValues(Entry("DisplayName", "Пустое", "UninstallString", "  ")) == null, "entries without an uninstaller are hidden");
            Check(Programs.FromValues(Entry("UninstallString", "x.exe")) == null, "entries without a name are hidden");
            Check(Programs.FromValues(Entry("DisplayName", "Обновление", "UninstallString", "x.exe", "ParentKeyName", "Office")) == null && Programs.FromValues(Entry("DisplayName", "KB1", "UninstallString", "x.exe", "ReleaseType", "Security Update")) == null, "updates are hidden");
            Prog bare = Programs.FromValues(Entry("DisplayName", "Простая", "UninstallString", "x.exe", "InstallDate", "вчера"));
            Check(bare != null && bare.Bytes == -1 && bare.Installed == null, "missing size and a bad date are simply unknown");
            Prog msi = Programs.FromValues(Entry("DisplayName", "MSI", "UninstallString", "MsiExec.exe /I{11111111-2222-3333-4444-555555555555}", "WindowsInstaller", 1));
            Check(msi != null && msi.Uninstall == "MsiExec.exe /X{11111111-2222-3333-4444-555555555555}", "Windows Installer entries are asked to uninstall, not to modify: " + (msi == null ? "" : msi.Uninstall));

            string exe, args;
            Programs.Split(@"""C:\Program Files\App\unins000.exe"" /SILENT /x", out exe, out args);
            Check(exe == @"C:\Program Files\App\unins000.exe" && args == "/SILENT /x", "a quoted uninstall string is split");
            Programs.Split(@"C:\Program Files\App\uninstall.exe /S", out exe, out args);
            Check(exe == @"C:\Program Files\App\uninstall.exe" && args == "/S", "an unquoted path with spaces is split");
            Programs.Split("MsiExec.exe /X{1}", out exe, out args);
            Check(exe == "MsiExec.exe" && args == "/X{1}", "msiexec is split");
            Programs.Split(@"C:\Tools\app.exe.config.exe", out exe, out args);
            Check(exe == @"C:\Tools\app.exe.config.exe" && args == "", "a single program without arguments");
            Programs.Split("  ", out exe, out args);
            Check(exe == null, "an empty string gives nothing");

            var all = new List<Prog>
            {
                new Prog { Name = "Бета", Publisher = "Один", Bytes = 10, Installed = new DateTime(2026, 1, 1) },
                new Prog { Name = "Альфа", Publisher = "Два", Bytes = 300, Installed = new DateTime(2025, 1, 1) },
                new Prog { Name = "Гамма", Publisher = "Один", Bytes = -1 }
            };
            List<Prog> bySize = Programs.View(all, "", false), byDate = Programs.View(all, null, true);
            Check(bySize[0].Name == "Альфа" && bySize[2].Name == "Гамма" && byDate[0].Name == "Бета" && byDate[2].Name == "Гамма", "sorting by size and by date; unknown goes last");
            Check(Programs.View(all, "аль", false).Count == 1 && Programs.View(all, "ОДИН", false).Count == 2 && Programs.View(all, "нет такого", false).Count == 0, "search by name and publisher");
        }

        static void HubAndPrefs()
        {
            // Описание настроек — в том виде, в каком его ждёт core.txt
            var item = FirstItem(AppHub.Settings());
            Check((string)item["key"] == "remind" && (string)item["type"] == "switch" && item["value"] is bool && !(bool)item["value"], "settings describe the reminder switch, off by default");
            Check(((string)item["label"]).Contains("5 ГБ"), "the switch has its label");
            string json = new JavaScriptSerializer().Serialize(HubLink.Msg("type", "state", "topic", AppHub.Topic, "data", AppHub.Settings()));
            var back = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            Check(back != null && (string)back["topic"] == Program.Id + ".settings" && FirstItem((Dictionary<string, object>)back["data"])["value"] is bool, "settings survive JSON");

            // metla.found: число и время в ISO
            var found = AppHub.FoundData(6871947674, new DateTime(2026, 11, 18, 7, 5, 9, DateTimeKind.Utc));
            Check(found.Count == 2 && (long)found["bytes"] == 6871947674 && (string)found["at"] == "2026-11-18T07:05:09Z", "metla.found is { bytes, at }");
            Check(new JavaScriptSerializer().Serialize(found).Contains("\"bytes\":6871947674"), "bytes travel as a number");
            Check(MainWindow.RemindBytes == 5L * 1024 * 1024 * 1024, "the reminder threshold is 5 GB");

            // settings.set: значение меняется, о нём сообщают один раз, чужой ключ и не то значение отклоняются
            int fired = 0;
            Action count = delegate { fired++; };
            Prefs.Changed += count;
            AppHub.Apply("remind", true);
            Check(Prefs.Remind && (bool)FirstItem(AppHub.Settings())["value"], "settings.set changes the value");
            AppHub.Apply("remind", true);
            Check(fired == 1, "an unchanged value is not announced again, announced " + fired);
            Check(Throws(delegate { AppHub.Apply("noSuchKey", true); }), "unknown key is refused");
            Check(Throws(delegate { AppHub.Apply("remind", "yes"); }), "a value of the wrong kind is refused");
            Check(Throws(delegate { AppHub.Apply(null, null); }), "empty arguments are refused");
            Check(Prefs.Remind, "refused commands change nothing");
            Prefs.Changed -= count;

            // hub.theme: «Меньше движения» читается только из настоящего «да»
            Check(AppHub.ReadReduced(HubLink.Msg("mode", "dark", "accent", null, "reducedMotion", true)), "reducedMotion: true is read");
            Check(!AppHub.ReadReduced(HubLink.Msg("mode", "dark", "reducedMotion", false)) && !AppHub.ReadReduced(HubLink.Msg("reducedMotion", "yes")) && !AppHub.ReadReduced(null) && !AppHub.ReadReduced("x"), "anything else is not reduced motion");

            // Настройки переживают запись и чтение
            string dir = Path.Combine(Path.GetTempPath(), Program.Id + "-test-" + Process.GetCurrentProcess().Id);
            Environment.SetEnvironmentVariable("METLA_DATA", dir);
            try
            {
                Prefs.Persist = true;
                Prefs.Remind = true;
                Prefs.LastAuto = new DateTime(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc);
                Prefs.Save();
                Prefs.Remind = false;
                Prefs.LastAuto = DateTime.MinValue;
                Prefs.Load();
                Check(Prefs.Remind && Prefs.LastAuto == new DateTime(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc), "settings survive saving and loading");
                File.WriteAllText(Path.Combine(dir, "settings.json"), "{ not json");
                Prefs.Remind = false;
                Prefs.Load();
                Check(!Prefs.Remind, "a broken settings file is ignored");
            }
            finally
            {
                Prefs.Persist = false;
                try { File.Delete(Path.Combine(dir, "settings.json")); Directory.Delete(dir, false); } catch { }
            }

            // Без хаба: клиент ничего не бросает и остаётся «не на связи». Порт 9 никто не слушает.
            var link = new HubLink(Program.Id, Program.Name, Program.VersionText, 9);
            Check(!Throws(delegate { link.Start(); link.State(AppHub.Topic, AppHub.Settings()); link.Command("core", "show", null); Thread.Sleep(300); link.Stop(); }), "no hub — nothing throws");
            Check(!link.Online, "no hub — the app is simply offline");
        }

        // ───────── Приложения dev.txt: в автозапуске и в программах ими ведает core.txt ─────────
        static void DevAppsChecks()
        {
            const string Home = @"C:\Users\вы\AppData\Local\Programs\dev.txt", Aura = @"C:\Users\вы\AppData\Local\Programs\aura-voice";
            Check(DevApps.OwnsIn(Home + @"\Core.exe", null, Home) && DevApps.OwnsIn(Home + @"\Kapsula\Kapsula.exe", "", Home) && DevApps.OwnsIn(Home + @"\Arkada\Tool.exe", "Кто-то ещё", Home), "anything in the dev.txt home is a dev.txt app");
            Check(DevApps.OwnsIn(@"C:\Users\вы\AppData\Local\Kapsula\Kapsula.exe", null, Home) && DevApps.OwnsIn(Aura + @"\Aura.exe", "Aura", Home) && DevApps.OwnsIn(@"D:\Apps\SMENA.EXE", "dev.txt", Home) && DevApps.OwnsIn(@"E:\dock\Dock.exe", null, Home) && DevApps.OwnsIn(@"C:\x\Metla.exe", null, Home), "a dev.txt file name anywhere is a dev.txt app (core.txt finds autostart entries the same way)");
            Check(!DevApps.OwnsIn(@"C:\Program Files (x86)\ASUS\AURA\Aura.exe", "ASUSTeK COMPUTER INC.", Home) && !DevApps.OwnsIn(@"C:\Games\Core.exe", "Другая студия", Home), "a program of another company with the same file name is not ours");
            Check(!DevApps.OwnsIn(@"C:\Program Files\Discord\Update.exe", "Discord Inc.", Home) && !DevApps.OwnsIn(@"C:\Tools\MyCore.exe", null, Home) && !DevApps.OwnsIn(@"C:\Tools\Core.exe.bak", null, Home) && !DevApps.OwnsIn(Home + @"2\Tool.exe", null, Home), "other programs, similar names and a sibling folder are not dev.txt apps");
            Check(!DevApps.OwnsIn(null, null, Home) && !DevApps.OwnsIn("", null, Home) && !DevApps.OwnsIn("Core.exe", null, Home) && !DevApps.OwnsIn(@"C:\a<b\Core.exe", null, Home), "empty, relative and broken paths are not dev.txt apps");
            Check(DevApps.OwnsIn("\"" + Home + "\\Core.exe\"", null, Home) && DevApps.OwnsIn(Home.ToUpperInvariant() + @"\TOOL.EXE", "x", Home), "quotes and letter case do not matter");
            Check(DevApps.Owns(@"%LOCALAPPDATA%\Programs\dev.txt\Tool\Tool.exe", "Кто-то ещё") && DevApps.Within(DevApps.Home, DevApps.Home), "environment variables are expanded; the real home is %LOCALAPPDATA%\\Programs\\dev.txt");
            Check(DevApps.Title(Home + @"\Kapsula\Kapsula.exe") == "kapsula.txt" && DevApps.Title(@"C:\x\AURA.exe") == "aura.txt" && DevApps.Title(@"C:\x\Discord.exe") == null && DevApps.Title(null) == null, "dev.txt apps are listed under their product names");

            // Автозапуск: пока core.txt установлен, у приложений dev.txt своя группа и нет переключателя
            var list = new List<StartupEntry>
            {
                new StartupEntry { Name = "core.txt", Editable = true, Family = true },
                new StartupEntry { Name = "Discord", Editable = true },
                new StartupEntry { Name = "Aura", Editable = false, Family = true },
                new StartupEntry { Name = "Realtek", Editable = false }
            };
            List<StartupEntry> mine = new List<StartupEntry>(), fam = new List<StartupEntry>(), shared = new List<StartupEntry>();
            StartupPage.Split(list, true, mine, fam, shared);
            Check(mine.Count == 1 && mine[0].Name == "Discord" && fam.Count == 2 && fam[0].Name == "core.txt" && shared.Count == 1 && shared[0].Name == "Realtek", "with core.txt installed dev.txt apps get their own group");
            mine.Clear(); fam.Clear(); shared.Clear();
            StartupPage.Split(list, false, mine, fam, shared);
            Check(mine.Count == 2 && fam.Count == 0 && shared.Count == 2, "without core.txt they are like any other program");
            Check(!MainWindow.CanToggle(list[0], true) && MainWindow.CanToggle(list[0], false) && MainWindow.CanToggle(list[1], true) && !MainWindow.CanToggle(list[3], false), "no switch for a dev.txt app while core.txt is installed; shared entries never switch");

            // Программы: приложения dev.txt удаляет core.txt
            Prog kapsula = Programs.FromValues(Entry("DisplayName", "kapsula.txt", "Publisher", "dev.txt", "DisplayVersion", "0.8.4", "UninstallString", "\"" + DevApps.Home + "\\Core.exe\" --uninstall kapsula", "DisplayIcon", DevApps.Home + @"\Kapsula\Kapsula.exe"), "dev.txt.kapsula");
            Check(kapsula != null && kapsula.Family && kapsula.Name == "kapsula.txt", "core.txt's uninstall entry (dev.txt.<id>) is a dev.txt app");
            Prog bare = Programs.FromValues(Entry("DisplayName", "Smena", "UninstallString", @"C:\x\u.exe"), "dev.txt.smena");
            Check(bare != null && bare.Family && bare.Name == "smena.txt", "the key name alone is enough, and it gives the product name");
            Prog aura = Programs.FromValues(Entry("DisplayName", "Aura 0.26.2", "Publisher", "Aura", "DisplayVersion", "0.26.2", "UninstallString", "\"" + DevApps.AuraHome + "\\Uninstall Aura.exe\" /currentuser", "DisplayIcon", DevApps.AuraHome + @"\uninstallerIcon.ico"), "5266ef77-3896-53f2-9a95-f401976d665b");
            Check(aura != null && aura.Family && aura.Name == "aura.txt" && aura.Version == "0.26.2", "aura.txt's own installer entry is found by its folder and listed under the product name");
            Prog other = Programs.FromValues(Entry("DisplayName", "Aura Sync", "Publisher", "ASUSTeK", "UninstallString", @"C:\Program Files (x86)\ASUS\AURA\unins000.exe", "DisplayIcon", @"C:\Program Files (x86)\ASUS\AURA\Aura.exe"), "{ABC}");
            Check(other != null && !other.Family && other.Name == "Aura Sync", "a program of another company is not a dev.txt app, even with an Aura.exe inside");
            Check(!Programs.FromValues(Entry("DisplayName", "Игра", "UninstallString", "x.exe"), "dev.txt").Family && !Programs.FromValues(Entry("DisplayName", "Игра", "UninstallString", "x.exe", "Publisher", "dev.txt games"), "devtxt.kapsula").Family && !Programs.FromValues(Entry("DisplayName", "Игра", "UninstallString", "x.exe")).Family, "similar keys and publishers are not dev.txt apps");
            Check(DevApps.OwnsProgramIn(null, null, new string[] { @"C:\D\dev.txt\Smena\Smena.exe" }, @"C:\D\dev.txt", @"C:\D\aura-voice") && !DevApps.OwnsProgramIn(null, null, new string[] { null, "", @"C:\D\aura-voice2\x.exe", @"C:\D\dev.txt2\y.exe" }, @"C:\D\dev.txt", @"C:\D\aura-voice"), "paths in the dev.txt home or the aura.txt folder count, neighbours do not");
            Check(!MainWindow.CanUninstall(kapsula, true) && MainWindow.CanUninstall(kapsula, false) && MainWindow.CanUninstall(other, true), "dev.txt apps are removed in core.txt while it is installed; other programs — by their own uninstaller");
        }

        // ───────── Один значок в трее, metla.tray и связь с семьёй (без сети) ─────────
        static void TrayAndFamily()
        {
            Check(!MainWindow.OwnTray(false, false, false, false, false), "no reminder — no own tray icon");
            Check(!MainWindow.OwnTray(true, true, true, false, false) && !MainWindow.OwnTray(true, true, true, true, false), "core.txt holds the family's one tray icon — ours is hidden");
            Check(MainWindow.OwnTray(true, false, false, false, false) && MainWindow.OwnTray(true, false, true, false, false), "no core.txt, or it keeps no icon — ours is shown");
            Check(!MainWindow.OwnTray(true, false, false, true, false) && MainWindow.OwnTray(true, false, true, true, false), "just started: wait for core.txt's word, but show at once when it says it keeps no icon");
            Check(MainWindow.OwnTray(true, true, true, false, true) && MainWindow.OwnTray(true, false, false, true, true), "an own Windows notification shows the icon for its time, even while core.txt holds the tray");
            Check(!MainWindow.OwnTray(false, false, false, false, true), "no reminder — no icon, notification or not");

            bool shown = false, scanned = false;
            List<TrayItem> items = MainWindow.TrayMenu(false, delegate { shown = true; }, delegate { scanned = true; });
            Check(items.Count >= 1 && items.Count <= Family.MaxTray && items[0].Key == "show" && items[0].Label == "Открыть" && items[0].Check == null, "metla.tray: «Открыть» first, at most four items");
            var forbidden = new HashSet<string> { "autostart", "startup", "update", "updates", "focus", "dnd", "core", "exit", "quit", "clean" };
            Check(items.TrueForAll(delegate(TrayItem t) { return !forbidden.Contains(t.Key) && !t.Label.Contains("metla") && !t.Label.Contains("Очист") && !t.Label.Contains("Удал"); }), "no common and no cleaning items in metla.tray, and no app name in the labels");
            Check(items.Exists(delegate(TrayItem t) { return t.Key == "scan" && t.Label == "Проверить диск" && t.Enabled; }) && !MainWindow.TrayMenu(true, null, null).Find(delegate(TrayItem t) { return t.Key == "scan"; }).Enabled, "«Проверить диск» is there and unavailable while work is going on");
            var data = MainWindow.TrayMenu(true, null, null)[1].ToData();
            Check(data.ContainsKey("enabled") && !(bool)data["enabled"] && !data.ContainsKey("check"), "an unavailable item says enabled: false and has no tick");

            // Связь, собранная как в AppHub.Start, но не запущенная: Family дописал своё, tray.run доходит до пункта
            AppHub.Tray = delegate { return MainWindow.TrayMenu(false, delegate { shown = true; }, delegate { scanned = true; }); };
            try
            {
                Family fam;
                HubLink l = AppHub.Wire(9, out fam);
                Check(Array.IndexOf(l.Commands, "settings.set") >= 0 && Array.IndexOf(l.Commands, "show") >= 0 && Array.IndexOf(l.Commands, "tray.run") >= 0, "commands: settings.set, show, tray.run");
                Check(Array.IndexOf(l.Topics, "hub.theme") >= 0 && Array.IndexOf(l.Topics, "core.family") >= 0 && Array.IndexOf(l.Topics, "hub.apps") >= 0, "topics: hub.theme, core.family, hub.apps");
                l.OnCommand("tray.run", HubLink.Msg("key", "scan"), "core");
                Check(scanned && !shown, "tray.run scan reaches «Проверить диск»");
                l.OnCommand("tray.run", HubLink.Msg("key", "show"), "core");
                Check(shown, "tray.run show opens the window");
                Check(Throws(delegate { l.OnCommand("tray.run", HubLink.Msg("key", "clean"), "core"); }), "an unknown tray item is refused");
                fam.SetTray(MainWindow.TrayMenu(true, null, delegate { scanned = false; }));
                scanned = true;
                Check(Throws(delegate { l.OnCommand("tray.run", HubLink.Msg("key", "scan"), "core"); }) && scanned, "an unavailable tray item is refused and does nothing");
                l.OnState("core.family", HubLink.Msg("tray", true, "focus", true, "game", true), "core");
                Check(!fam.HideOwnTray && !fam.Focus && !fam.Game && !fam.IslandOnline && !fam.Notify("metla.txt", "x", true), "without a live link nothing is hidden or held back, and there is no island");
            }
            finally { AppHub.Tray = null; }
        }

        // ───────── Корзина для дока: metla.bin, bin.open, show с page (bin — обычный вопрос), команды очистки нет — на подменённой корзине ─────────
        static void BinChecks()
        {
            // Формат: оба поля, целые; пустая — нули; отрицательное не уходит
            var d = BinWatch.Data(3, 4096);
            Check(d.Count == 2 && d["count"] is long && d["bytes"] is long && (long)d["count"] == 3 && (long)d["bytes"] == 4096, "metla.bin is { count, bytes } as integers");
            var zero = BinWatch.Data(0, 0);
            Check((long)zero["count"] == 0 && (long)zero["bytes"] == 0 && (long)BinWatch.Data(-1, -5)["bytes"] == 0, "an empty bin is { 0, 0 }; nothing negative");
            string json = new JavaScriptSerializer().Serialize(d);
            Check(json == "{\"count\":3,\"bytes\":4096}", "metla.bin travels as plain numbers, got " + json);

            // Подмена корзины: настоящая не трогается
            long count = 5, bytes = 1000;
            int opened = 0;
            var src = new BinSource();
            src.Query = delegate { return new long[] { Interlocked.Read(ref count), Interlocked.Read(ref bytes) }; };
            src.Open = delegate { opened++; };
            var sent = new List<Dictionary<string, object>>();
            var w = new BinWatch(src, delegate(Dictionary<string, object> x) { lock (sent) sent.Add(x); });
            w.Start(false);
            try
            {
                w.Recount(); w.Recount();
                Check(sent.Count == 1 && (long)sent[0]["count"] == 5 && (long)sent[0]["bytes"] == 1000, "metla.bin goes out once while nothing changes, sent " + sent.Count);
                Interlocked.Exchange(ref bytes, 1500);
                w.Recount();
                Check(sent.Count == 2 && (long)sent[1]["bytes"] == 1500, "a new size is sent again");

                // Дребезг: сотня уведомлений подряд — один пересчёт после тишины
                int before = w.Recounts;
                for (int i = 0; i < 200; i++) w.Poke();
                Thread.Sleep(BinWatch.QuietMs / 2);
                Check(w.Recounts == before, "no recount while notifications keep coming");
                Thread.Sleep(BinWatch.QuietMs + 600);
                Check(w.Recounts == before + 1, "a burst of notifications gives one recount, got " + (w.Recounts - before));

                // Корзину очистили (в окне metla.txt) — после пересчёта уходит { 0, 0 }
                Interlocked.Exchange(ref count, 0); Interlocked.Exchange(ref bytes, 0);
                w.Recount();
                var last = sent[sent.Count - 1];
                Check(sent.Count == 3 && (long)last["count"] == 0 && (long)last["bytes"] == 0, "an emptied bin is sent as { 0, 0 }");

                // Через команды хаба: связь не запущена, корзина — подменённая
                AppHub.BinWatcher = w;
                Family fam;
                HubLink l = AppHub.Wire(9, out fam);
                Check(Array.IndexOf(l.Commands, "bin.open") >= 0, "bin.open is declared");
                Check(Array.TrueForAll(l.Commands, delegate(string c) { return c != "bin.empty" && !c.Contains("clean") && !c.Contains("empty") && !c.Contains("delete"); }), "no hub command cleans (CLAUDE.md)");
                Check(Throws(delegate { l.OnCommand("bin.empty", new Dictionary<string, object>(), "dock"); }), "bin.empty is refused");
                Check(l.OnCommand("bin.open", new Dictionary<string, object>(), "dock") == null && opened == 1, "bin.open opens the bin and answers null");
                src.Open = delegate { throw new InvalidOperationException("x"); };
                Check(Throws(delegate { l.OnCommand("bin.open", new Dictionary<string, object>(), "dock"); }), "bin.open failure is refused");

                // show с page: scan и startup — на свою страницу, незнакомая или без page — окно как обычно
                Check(AppHub.PageArg(HubLink.Msg("page", "scan")) == "scan" && AppHub.PageArg(HubLink.Msg("page", "startup")) == "startup", "show understands page scan and startup");
                Check(AppHub.PageArg(HubLink.Msg("page", "nope")) == null && AppHub.PageArg(HubLink.Msg("page", 5)) == null && AppHub.PageArg(new Dictionary<string, object>()) == null && AppHub.PageArg(null) == null, "an unknown or missing page is ignored");
                int plain = 0; string at = null;
                AppHub.Show = delegate { plain++; };
                AppHub.ShowAt = delegate(string p) { at = p; };
                Check(!Throws(delegate { l.OnCommand("show", HubLink.Msg("page", "nope"), "dock"); }) && plain == 1 && at == null, "show with an unknown page just opens the window");
                l.OnCommand("show", HubLink.Msg("page", "startup"), "dock");
                Check(at == "startup" && plain == 1, "show with page startup goes to that page");
                l.OnCommand("show", HubLink.Msg("page", "bin"), "dock");
                Check(at == "bin" && plain == 1, "show with page bin goes to the recycle-bin question");

                // Вопрос про корзину: в снимке ровно корзина, что бы ни было выбрано в окне
                List<Category> cats = Scanner.Categories();
                foreach (Category c in cats) { c.Bytes = 100; c.Count = 1; c.On = true; }
                Scanner.Find(cats, "recycle").On = false;
                HashSet<string> ids = Scanner.BinSnapshot(cats);
                Check(ids.Count == 1 && ids.Contains("recycle"), "the bin question snapshots exactly the recycle bin, switched off or not");
                Check(Scanner.Find(cats, "temp").On && !Scanner.Find(cats, "recycle").On, "the choice in the window is not touched");
                Scanner.Find(cats, "recycle").Bytes = 0; Scanner.Find(cats, "recycle").Count = 0;
                Check(Scanner.BinSnapshot(cats).Count == 0, "an empty bin gives no question");
            }
            finally
            {
                AppHub.BinWatcher = null; AppHub.Show = null; AppHub.ShowAt = null;
                w.Stop();
            }
        }

        // ───────── Ожидание слова core.txt после запуска: кончается по своему таймеру, а не по часам ─────────
        static void TrayHoldChecks()
        {
            var hold = new TrayHold(TimeSpan.FromMilliseconds(40));   // audit:ok — короткое ожидание для проверки, не движение
            int over = 0;
            DispatcherFrame frame = null;
            hold.Over = delegate { over++; if (frame != null) frame.Continue = false; };
            Check(!hold.Waiting, "the startup hold is off until it starts");
            hold.Start();
            Check(hold.Waiting && !MainWindow.OwnTray(true, false, false, hold.Waiting, false), "while it holds, the own icon waits for core.txt");
            Pump(ref frame, 3000);
            Check(over == 1 && !hold.Waiting, "the hold ends by its own timer, once: fired " + over);
            Check(MainWindow.OwnTray(true, false, false, hold.Waiting, false), "core.txt never answered — after the hold the own icon is shown");
            hold.Start();
            hold.Stop();
            Pump(ref frame, 150);
            Check(over == 1 && !hold.Waiting, "a stopped hold (the link dropped) stays off and does not fire later");
        }

        // Крутит очередь потока, пока кадр не остановят или не выйдет время
        static void Pump(ref DispatcherFrame frame, int ms)
        {
            var f = new DispatcherFrame();
            frame = f;
            var guard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            guard.Tick += delegate { guard.Stop(); f.Continue = false; };
            guard.Start();
            Dispatcher.PushFrame(f);
            guard.Stop();
            frame = null;
        }

        // ───────── Напоминание: остров, своё уведомление, «Не беспокоить» и игра ─────────
        static void Reminders()
        {
            int asked = 0;
            Func<bool> yes = delegate { asked++; return true; }, no = delegate { asked++; return false; };
            Check(!Reminder.Quiet(true, false, false, yes) && asked == 0, "with core.txt online its word decides; the own full-screen check is not run");
            Check(Reminder.Quiet(true, true, false, no) && Reminder.Quiet(true, false, true, no) && asked == 0, "do-not-disturb or a game from core.txt hold the reminder back");
            Check(Reminder.Quiet(false, false, false, yes) && !Reminder.Quiet(false, false, false, no) && asked == 2, "without core.txt a full-screen window in front holds it back");
            Check(!Reminder.Quiet(false, false, false, null), "nothing to check — not quiet");
            Check(Reminder.Way(false, true, false, true, true) == RemindWay.Island && Reminder.Way(false, true, false, true, false) == RemindWay.Island, "the island goes first");
            Check(Reminder.Way(false, true, false, false, true) == RemindWay.Balloon, "without the island — the own Windows notification, also while core.txt holds the tray (the icon shows just for it)");
            Check(Reminder.Way(false, true, true, true, true) == RemindWay.Wait && Reminder.Way(false, true, true, false, true) == RemindWay.Wait, "do-not-disturb or a game: the reminder waits, not dropped");
            Check(Reminder.Way(false, true, false, false, false) == RemindWay.Wait, "no own icon at all and no island: it waits");
            Check(Reminder.Way(true, true, false, true, true) == RemindWay.Drop && Reminder.Way(false, false, false, true, true) == RemindWay.Drop, "an open window or nothing to free: no reminder");
        }
    }
}

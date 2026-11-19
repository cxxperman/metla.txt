// metla.txt. SelfTest.cs — самопроверка (--selftest). Правила удаления проверяются на собственном выдуманном
// дереве во временной папке; настоящие файлы, корзина и реестр не трогаются. Всё, что удаляет проверка, лежит
// внутри «ограды» (Safe.Fence) — её собственной папки.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

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
    }
}

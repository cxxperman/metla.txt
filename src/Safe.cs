// metla.txt. Safe.cs — единственное место в программе, где что-то удаляется с диска.
// Правила жёсткие и проверяются в --selftest:
//   • удалить можно только то, что лежит строго внутри разрешённой папки (root); сама папка не удаляется никогда;
//   • путь с «..» отклоняется, даже если после раскрытия он остался бы внутри;
//   • ссылки и точки соединения (reparse points) не удаляются и по ним никто не ходит — ни сам путь, ни папки на пути к нему;
//   • файл, который кем-то открыт, защищён от записи или недоступен, пропускается: никакой настойчивости;
//   • папка удаляется, только если она пуста (никакого рекурсивного удаления в программе нет вообще);
//   • пока Armed == false (--demo, --shot, --scan-only), не удаляется ничего.
using System;
using System.IO;

namespace DevTxt.Metla
{
    enum Del { Deleted, Skipped, Refused }   // Skipped — занят или нет доступа; Refused — запретили правила

    static class Safe
    {
        public static bool Armed;      // включается только в обычном запуске и в самопроверке
        public static string Fence;    // самопроверка: любые удаления — только внутри её временной папки
        public static int Refusals;    // сколько раз правила сказали «нет» (для проверок)

        static readonly char[] Seps = { '\\', '/' };

        static bool IsReparse(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        // Полный путь без хвостового разделителя; null — путь не годится
        static string Norm(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Trim().Length == 0) return null;
            if (path.IndexOf('\0') >= 0) return null;
            foreach (string part in path.Split(Seps)) if (part == ".." || part == ".") return null;
            if (!Path.IsPathRooted(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return null;   // только обычные локальные пути вида C:\…
            string full;
            try { full = Path.GetFullPath(path); } catch { return null; }
            full = full.TrimEnd(Seps);
            return full.Length < 3 && full.Length != 2 ? null : full;
        }

        // Лежит ли path строго внутри root. Ничего не трогает на диске, кроме чтения атрибутов.
        public static bool Inside(string root, string path, out string full, out string why)
        {
            full = null; why = null;
            string r = Norm(root), p = Norm(path);
            if (r == null) { why = "папка не годится"; return false; }
            if (p == null) { why = "путь не годится"; return false; }
            if (r.Length <= 3) { why = "корень диска не может быть разрешённой папкой"; return false; }
            if (Fence != null)
            {
                string f = Norm(Fence);
                if (f == null || !(r + "\\").StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase)) { why = "папка вне ограды самопроверки"; return false; }
            }
            if (string.Equals(r, p, StringComparison.OrdinalIgnoreCase)) { why = "саму папку удалять нельзя"; return false; }
            if (!p.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase)) { why = "путь вне разрешённой папки"; return false; }
            try
            {
                // Ни сама папка, ни что-либо на пути от неё к файлу, ни сам файл не должны быть ссылкой
                if (IsReparse(r)) { why = "разрешённая папка — ссылка"; return false; }
                string at = r;
                foreach (string part in p.Substring(r.Length + 1).Split('\\'))
                {
                    at = at + "\\" + part;
                    if (IsReparse(at)) { why = "на пути ссылка или точка соединения"; return false; }
                }
            }
            catch { why = "не удалось прочитать атрибуты"; return false; }
            full = p;
            return true;
        }

        // Удаляет один файл или одну пустую папку внутри root
        public static Del Delete(string root, string path)
        {
            string full, why;
            if (!Armed) { Refusals++; return Del.Refused; }
            if (!Inside(root, path, out full, out why)) { Refusals++; return Del.Refused; }
            try
            {
                FileAttributes a = File.GetAttributes(full);
                if ((a & FileAttributes.ReparsePoint) != 0) { Refusals++; return Del.Refused; }
                if ((a & FileAttributes.Directory) != 0)
                {
                    Directory.Delete(full, false);   // только пустую: с содержимым система сама откажет
                    return Del.Deleted;
                }
                // Файл кем-то открыт — монопольно его не открыть, и мы его не трогаем
                using (new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                File.Delete(full);
                return Del.Deleted;
            }
            catch { return Del.Skipped; }
        }
    }
}

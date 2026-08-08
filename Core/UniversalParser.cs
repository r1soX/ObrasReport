using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ObrasReport.Models;

namespace ObrasReport.Core
{
    /// <summary>Разбор произвольной книги Excel в таблицу: строка заголовков + строки данных.</summary>
    public static class UniversalParser
    {
        private static readonly Regex ObrRe = new Regex(@"^Обращение\s+(\d+)\s+от", RegexOptions.Compiled);
        private static readonly HashSet<string> TitleRows = new HashSet<string>
        {
            "Реестр обращений", "Параметры:", "Итого", "Обращение"
        };

        public static UniversalTable Parse(string path)
        {
            var rows = XlsxReader.ReadFirstSheet(path);
            var table = new UniversalTable { FilePath = path, FileName = Path.GetFileName(path) };
            if (rows.Count == 0) return table;

            if (IsRegistry(rows)) return ParseRegistry(rows, path);
            return ParseGeneric(rows, path);
        }

        // ---------- обычная плоская таблица ----------
        private static UniversalTable ParseGeneric(SortedDictionary<int, Dictionary<int, string>> rows, string path)
        {
            var table = new UniversalTable { FilePath = path, FileName = Path.GetFileName(path) };
            int headerRow = DetectHeaderRow(rows);
            int maxCol = rows.Values.SelectMany(r => r.Keys).DefaultIfEmpty(0).Max();

            var header = rows.TryGetValue(headerRow, out var h) ? h : new Dictionary<int, string>();
            for (int c = 1; c <= maxCol; c++)
            {
                string name = header.TryGetValue(c, out var v) && !string.IsNullOrWhiteSpace(v)
                    ? v.Trim() : $"Столбец {c}";
                table.Columns.Add(new ColumnInfo { Pos = c, Name = name });
            }

            foreach (var kv in rows)
            {
                if (kv.Key <= headerRow) continue;
                if (kv.Value.Values.All(string.IsNullOrWhiteSpace)) continue;
                table.DataRows.Add(new Dictionary<int, string>(kv.Value));
            }
            return table;
        }

        // ---------- 1С «Реестр обращений» (сгруппирован по ответственному) ----------
        private static bool IsRegistry(SortedDictionary<int, Dictionary<int, string>> rows)
        {
            bool hasResp = rows.Values.Any(r => r.TryGetValue(1, out var v) && (v ?? "").Trim() == "Ответственный");
            bool hasObr = rows.Values.Any(r => r.TryGetValue(1, out var v) && ObrRe.IsMatch((v ?? "").Trim()));
            return hasResp && hasObr;
        }

        private static UniversalTable ParseRegistry(SortedDictionary<int, Dictionary<int, string>> rows, string path)
        {
            var table = new UniversalTable { FilePath = path, FileName = Path.GetFileName(path) };
            int maxCol = rows.Values.SelectMany(r => r.Keys).DefaultIfEmpty(0).Max();

            // строка-заголовок «Ответственный» и следующая за ней «Обращение»
            int capRow = rows.First(r => r.Value.TryGetValue(1, out var v) && (v ?? "").Trim() == "Ответственный").Key;
            int obrRow = rows.Keys.Where(k => k > capRow &&
                rows[k].TryGetValue(1, out var v) && (v ?? "").Trim() == "Обращение").DefaultIfEmpty(capRow).First();
            int firstData = System.Math.Max(capRow, obrRow) + 1;

            // синтетические колонки
            table.Columns.Add(new ColumnInfo { Pos = 1, Name = "Ответственный" });
            table.Columns.Add(new ColumnInfo { Pos = 2, Name = "№ обращения" });

            // метрики/атрибуты — подписи из строк заголовка (кроме колонки 1)
            var metricSrc = new List<int>();
            int newPos = 3;
            for (int c = 2; c <= maxCol; c++)
            {
                string cap = Cap(rows, capRow, c);
                if (string.IsNullOrEmpty(cap)) cap = Cap(rows, obrRow, c);
                if (string.IsNullOrWhiteSpace(cap)) continue;
                table.Columns.Add(new ColumnInfo { Pos = newPos++, Name = cap.Trim() });
                metricSrc.Add(c);
            }

            string currentResp = "";
            foreach (var kv in rows)
            {
                if (kv.Key < firstData) continue;
                var cells = kv.Value;
                string c1 = ((cells.TryGetValue(1, out var v1) ? v1 : "") ?? "").Trim();
                if (c1.Length == 0) continue;
                if (TitleRows.Contains(c1)) continue;

                var m = ObrRe.Match(c1);
                if (m.Success)
                {
                    var row = new Dictionary<int, string>
                    {
                        [1] = currentResp,
                        [2] = m.Groups[1].Value
                    };
                    for (int i = 0; i < metricSrc.Count; i++)
                        row[3 + i] = cells.TryGetValue(metricSrc[i], out var mv) ? (mv ?? "").Trim() : "";
                    table.DataRows.Add(row);
                }
                else
                {
                    currentResp = c1; // строка-группа ответственного (итоги пропускаем)
                }
            }
            return table;
        }

        private static string Cap(SortedDictionary<int, Dictionary<int, string>> rows, int row, int col)
        {
            if (rows.TryGetValue(row, out var r) && r.TryGetValue(col, out var v)) return (v ?? "").Trim();
            return "";
        }

        /// <summary>Строка заголовков — с наибольшим числом непустых ячеек среди первых строк.</summary>
        private static int DetectHeaderRow(SortedDictionary<int, Dictionary<int, string>> rows)
        {
            int bestRow = rows.Keys.First();
            int bestCount = -1;
            foreach (var kv in rows.Where(r => r.Key <= 25))
            {
                int cnt = kv.Value.Count(c => !string.IsNullOrWhiteSpace(c.Value));
                if (cnt > bestCount) { bestCount = cnt; bestRow = kv.Key; }
            }
            return bestRow;
        }
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ObrasReport.Models;

namespace ObrasReport.Core
{
    /// <summary>
    /// Разбор 1С-выгрузки «Закрытые обращения» (счётчики по ответственному):
    /// Количество обращений / (ЗАКРЫТО) / Количество нарядов / (ЗАКРЫТО).
    /// Понимает и агрегированный вид (строка на ответственного),
    /// и детальный (берёт итоги из строк-групп, пропуская строки обращений).
    /// </summary>
    public static class ClosedTrendParser
    {
        private static readonly Regex ObrRe = new Regex(@"^Обращение\s+\d+\s+от", RegexOptions.Compiled);
        private static readonly HashSet<string> Skip = new HashSet<string>
        {
            "Реестр обращений", "Параметры:", "Итого", "Обращение", "Ответственный"
        };

        /// <summary>Возвращает срез или null, если формат не подходит.</summary>
        public static TrendSnapshot TryParse(string path)
        {
            SortedDictionary<int, Dictionary<int, string>> rows;
            try { rows = XlsxReader.ReadFirstSheet(path); }
            catch { return null; }
            if (rows.Count == 0) return null;

            // строка заголовков «Ответственный»
            var capKv = rows.FirstOrDefault(r => r.Value.TryGetValue(1, out var v) && (v ?? "").Trim() == "Ответственный");
            if (capKv.Value == null) return null;
            int capRow = capKv.Key;
            int maxCol = rows.Values.SelectMany(r => r.Keys).DefaultIfEmpty(0).Max();

            int obrTotal = 0, obrClosed = 0, narTotal = 0, narClosed = 0;
            for (int c = 2; c <= maxCol; c++)
            {
                string cap = Cap(rows, capRow, c);
                if (string.IsNullOrWhiteSpace(cap)) continue;
                string n = cap.ToLowerInvariant();
                bool closed = n.Contains("закр");
                if (n.Contains("наряд")) { if (closed) narClosed = c; else narTotal = c; }
                else if (n.Contains("обращени")) { if (closed) obrClosed = c; else obrTotal = c; }
            }

            // формат считаем «динамикой», только если есть колонки нарядов (отличает от статусных выгрузок)
            if (narTotal == 0 && narClosed == 0) return null;

            var snap = new TrendSnapshot { FilePath = path, FileName = Path.GetFileName(path) };
            if (RegistryParser.TryParseDate(snap.FileName, out var d, out var lbl)) { snap.Date = d; snap.Label = lbl; }
            else { snap.Date = File.GetLastWriteTime(path); snap.Label = ""; }

            int obrCaptionRow = rows.Keys.Where(k => k > capRow &&
                rows[k].TryGetValue(1, out var v) && (v ?? "").Trim() == "Обращение").DefaultIfEmpty(capRow).First();
            int firstData = System.Math.Max(capRow, obrCaptionRow) + 1;

            foreach (var kv in rows)
            {
                if (kv.Key < firstData) continue;
                var cells = kv.Value;
                string c1 = ((cells.TryGetValue(1, out var v1) ? v1 : "") ?? "").Trim();
                if (c1.Length == 0 || Skip.Contains(c1) || ObrRe.IsMatch(c1)) continue;

                // строка ответственного с итогами
                var m = new TrendMetrics
                {
                    ObrTotal = Num(cells, obrTotal),
                    ObrClosed = Num(cells, obrClosed),
                    NarTotal = Num(cells, narTotal),
                    NarClosed = Num(cells, narClosed),
                };
                if (snap.ByResp.TryGetValue(c1, out var ex))
                {
                    ex.ObrTotal += m.ObrTotal; ex.ObrClosed += m.ObrClosed;
                    ex.NarTotal += m.NarTotal; ex.NarClosed += m.NarClosed;
                }
                else snap.ByResp[c1] = m;
            }

            return snap.ByResp.Count > 0 ? snap : null;
        }

        private static string Cap(SortedDictionary<int, Dictionary<int, string>> rows, int row, int col)
        {
            if (rows.TryGetValue(row, out var r) && r.TryGetValue(col, out var v)) return (v ?? "").Trim();
            return "";
        }

        private static int Num(Dictionary<int, string> cells, int col)
        {
            if (col <= 0) return 0;
            if (cells.TryGetValue(col, out var v) && int.TryParse((v ?? "").Trim(), out var n)) return n;
            return 0;
        }
    }
}

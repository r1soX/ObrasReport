using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ObrasReport.Models;

namespace ObrasReport.Core
{
    /// <summary>Разбор одной выгрузки в <see cref="Snapshot"/> с автоопределением формата.</summary>
    public static class RegistryParser
    {
        // строки-заголовки в колонке №1, которые не являются ни обращением, ни ответственным
        private static readonly HashSet<string> SkipCol1 = new HashSet<string>
        {
            "Реестр обращений", "Параметры:", "Ответственный", "Обращение", "Ссылка", "Итого"
        };

        private static readonly Regex ObrRe = new Regex(@"^Обращение\s+(\d+)\s+от", RegexOptions.Compiled);

        public static Snapshot Parse(string path)
        {
            var rows = XlsxReader.ReadFirstSheet(path);

            var map = BuildColumnMap(rows);
            var layout = DetectLayout(map);

            var snap = new Snapshot
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                Layout = layout
            };
            ExtractDate(snap);

            string currentResp = null;
            foreach (var kv in rows)
            {
                var cells = kv.Value;
                string c1 = Get(cells, 1);
                if (string.IsNullOrWhiteSpace(c1)) continue;
                c1 = c1.Trim();
                if (SkipCol1.Contains(c1)) continue;

                var m = ObrRe.Match(c1);
                if (m.Success)
                {
                    string num = m.Groups[1].Value;
                    if (snap.Records.ContainsKey(num)) { snap.RawRowCount++; continue; }

                    var rec = new ObrRecord
                    {
                        Number = num,
                        Responsible = currentResp ?? "(не указан)",
                        Status = Get(cells, map.StatusCol).Trim(),
                    };

                    if (layout == LayoutType.Repairs)
                    {
                        rec.ObjectName = Get(cells, map.FilialCol).Trim();
                        rec.Classifier = Get(cells, map.ClassifierCol).Trim();
                        rec.ConfigUnit = Get(cells, map.KeCol).Trim();
                        rec.Days = Get(cells, map.DaysCol).Trim();
                        rec.Severity = DetectSeverity(cells, map);
                    }
                    else
                    {
                        rec.ObjectName = Get(cells, map.ClientCol).Trim();
                        rec.ConfigUnit = Get(cells, map.KeCol).Trim();
                        rec.Service = Get(cells, map.ServiceCol).Trim();
                    }

                    snap.Records[num] = rec;
                    snap.RawRowCount++;
                }
                else
                {
                    // строка-заголовок группы ответственного
                    currentResp = c1;
                }
            }

            return snap;
        }

        private class ColumnMap
        {
            public int StatusCol, ClientCol, FilialCol, ClassifierCol, KeCol, DaysCol, DateCol, ServiceCol;
            public int SevBlackCol, SevRedCol, SevYellowCol;
        }

        private static ColumnMap BuildColumnMap(SortedDictionary<int, Dictionary<int, string>> rows)
        {
            var map = new ColumnMap();
            foreach (var kv in rows)
            {
                foreach (var cell in kv.Value)
                {
                    string v = (cell.Value ?? "").Trim();
                    if (v.Length == 0) continue;
                    int col = cell.Key;
                    switch (v)
                    {
                        case "Клиент": if (map.ClientCol == 0) map.ClientCol = col; break;
                        case "Филиал": if (map.FilialCol == 0) map.FilialCol = col; break;
                        case "Классификатор": if (map.ClassifierCol == 0) map.ClassifierCol = col; break;
                        case "КЕ":
                        case "Конфигурационная единица": if (map.KeCol == 0) map.KeCol = col; break;
                        case "Услуга": if (map.ServiceCol == 0) map.ServiceCol = col; break;
                        case "Состояние": if (map.StatusCol == 0) map.StatusCol = col; break;
                        case "Состояние дней": if (map.DaysCol == 0) map.DaysCol = col; break;
                        case "Обращение.Дата последнего изменения": if (map.DateCol == 0) map.DateCol = col; break;
                        case "Желтая":
                        case "Жёлтая": if (map.SevYellowCol == 0) map.SevYellowCol = col; break;
                        case "Красная": if (map.SevRedCol == 0) map.SevRedCol = col; break;
                        case "Черная":
                        case "Чёрная": if (map.SevBlackCol == 0) map.SevBlackCol = col; break;
                    }
                }
            }
            return map;
        }

        private static LayoutType DetectLayout(ColumnMap map)
        {
            if (map.FilialCol != 0 || map.ClassifierCol != 0) return LayoutType.Repairs;
            if (map.ClientCol != 0) return LayoutType.Colored;
            // запасной вариант: если нашли состояние — считаем цветным
            return map.StatusCol != 0 ? LayoutType.Colored : LayoutType.Unknown;
        }

        private static string DetectSeverity(Dictionary<int, string> cells, ColumnMap map)
        {
            if (map.SevYellowCol != 0 && !string.IsNullOrWhiteSpace(Get(cells, map.SevYellowCol))) return "Жёлтая";
            if (map.SevRedCol != 0 && !string.IsNullOrWhiteSpace(Get(cells, map.SevRedCol))) return "Красная";
            if (map.SevBlackCol != 0 && !string.IsNullOrWhiteSpace(Get(cells, map.SevBlackCol))) return "Чёрная";
            return "";
        }

        private static string Get(Dictionary<int, string> cells, int col)
        {
            if (col <= 0) return "";
            return cells.TryGetValue(col, out var v) ? (v ?? "") : "";
        }

        // ---- дата из имени файла ----
        private static readonly Regex DateRe = new Regex(@"(\d{1,2})[.\-_ ](\d{1,2})(?:[.\-_ ](\d{2,4}))?", RegexOptions.Compiled);

        private static void ExtractDate(Snapshot snap)
        {
            if (TryParseDate(snap.FileName, out DateTime date, out string label))
            {
                snap.SortDate = date;
                snap.Label = label;
                snap.DateDetected = true;
            }
            else
            {
                snap.SortDate = File.GetLastWriteTime(snap.FilePath);
                snap.Label = "";              // пусто → пользователь укажет вручную
                snap.DateDetected = false;
            }
        }

        /// <summary>Пытается извлечь дату (последнюю по тексту) из произвольной строки.</summary>
        private static readonly Regex QuarterRe = new Regex(@"(\d)\s*кв", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex YearRe = new Regex(@"(20\d{2})", RegexOptions.Compiled);

        public static bool TryParseDate(string text, out DateTime date, out string label)
        {
            date = default; label = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            // квартал: «1 квартал», «2 кв»
            var q = QuarterRe.Match(text);
            if (q.Success)
            {
                int qn = int.Parse(q.Groups[1].Value);
                if (qn >= 1 && qn <= 4)
                {
                    int yr = YearRe.Match(text).Success ? int.Parse(YearRe.Match(text).Groups[1].Value) : DateTime.Now.Year;
                    date = new DateTime(yr, (qn - 1) * 3 + 1, 1);
                    label = $"{qn} кв {yr}";
                    return true;
                }
            }

            var matches = DateRe.Matches(text);
            if (matches.Count == 0) return false;

            var m = matches[matches.Count - 1]; // дата обычно в конце имени
            int day = int.Parse(m.Groups[1].Value);
            int month = int.Parse(m.Groups[2].Value);
            int year = DateTime.Now.Year;
            if (m.Groups[3].Success)
            {
                year = int.Parse(m.Groups[3].Value);
                if (year < 100) year += 2000;
            }
            if (day < 1 || day > 31 || month < 1 || month > 12) return false;
            try
            {
                date = new DateTime(year, month, day);
                label = m.Groups[3].Success ? $"{day:00}.{month:00}.{year}" : $"{day:00}.{month:00}";
                return true;
            }
            catch { return false; }
        }
    }
}

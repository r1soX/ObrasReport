using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ExcelDataReader;

namespace ObrasReport.Core
{
    /// <summary>
    /// Читатель первого листа Excel: .xlsx / .xlsm (Open XML) и .xls / .xlsb (BIFF/binary).
    /// Для .xlsx предпочтителен собственный ZIP+XML-парсер (устойчив к регистру SharedStrings из 1С);
    /// при ошибке и для .xls/.xlsb используется ExcelDataReader.
    /// </summary>
    public static class XlsxReader
    {
        private static readonly XNamespace NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        private static readonly HashSet<string> SupportedExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".xlsx", ".xlsm", ".xls", ".xlsb"
        };

        public static bool IsSupportedExcel(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return SupportedExt.Contains(Path.GetExtension(path));
        }

        /// <summary>Строки листа: индекс строки (1-based) -> (индекс колонки 1-based -> значение).</summary>
        public static SortedDictionary<int, Dictionary<int, string>> ReadFirstSheet(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Файл не найден.", path);

            string ext = Path.GetExtension(path) ?? "";
            if (!IsSupportedExcel(path))
                throw new InvalidDataException(
                    "Неподдерживаемый формат. Допустимы: .xlsx, .xlsm, .xls, .xlsb.");

            bool openXml = ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                           || ext.Equals(".xlsm", StringComparison.OrdinalIgnoreCase);

            if (openXml)
            {
                try { return ReadOpenXml(path); }
                catch (Exception primary)
                {
                    try { return ReadWithExcelDataReader(path); }
                    catch (Exception fallback)
                    {
                        throw new InvalidDataException(
                            "Не удалось прочитать книгу Excel (.xlsx/.xlsm): " + primary.Message +
                            " / запасной читатель: " + fallback.Message, primary);
                    }
                }
            }

            return ReadWithExcelDataReader(path);
        }

        // ---------- Excel 97-2003 / binary (.xls, .xlsb) и запасной путь ----------
        private static SortedDictionary<int, Dictionary<int, string>> ReadWithExcelDataReader(string path)
        {
            var rows = new SortedDictionary<int, Dictionary<int, string>>();
            var conf = new ExcelReaderConfiguration
            {
                // старые .xls из 1С часто в Windows-1251
                FallbackEncoding = Encoding.GetEncoding(1251)
            };

            using (var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(fs, conf))
            {
                int rowIdx = 0;
                while (reader.Read())
                {
                    rowIdx++;
                    var dict = new Dictionary<int, string>();
                    for (int c = 0; c < reader.FieldCount; c++)
                    {
                        if (reader.IsDBNull(c)) continue;
                        string val = CellToString(reader.GetValue(c));
                        if (string.IsNullOrWhiteSpace(val)) continue;
                        dict[c + 1] = val;
                    }
                    if (dict.Count > 0)
                        rows[rowIdx] = dict;
                }
                // только первый лист (без NextResult)
            }

            if (rows.Count == 0)
                throw new InvalidDataException("Лист пуст или не удалось разобрать ячейки.");
            return rows;
        }

        private static string CellToString(object val)
        {
            if (val == null) return null;
            if (val is string s) return s;
            if (val is DateTime dt) return dt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            if (val is double d)
            {
                if (Math.Abs(d - Math.Round(d)) < 1e-9)
                    return ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
                return d.ToString(CultureInfo.InvariantCulture);
            }
            if (val is float f)
            {
                if (Math.Abs(f - Math.Round(f)) < 1e-5)
                    return ((long)Math.Round(f)).ToString(CultureInfo.InvariantCulture);
                return f.ToString(CultureInfo.InvariantCulture);
            }
            if (val is decimal m)
            {
                if (m == decimal.Truncate(m))
                    return decimal.Truncate(m).ToString(CultureInfo.InvariantCulture);
                return m.ToString(CultureInfo.InvariantCulture);
            }
            if (val is bool b) return b ? "TRUE" : "FALSE";
            return Convert.ToString(val, CultureInfo.InvariantCulture);
        }

        // ---------- Open XML (.xlsx / .xlsm) — устойчивый к SharedStrings ----------
        private static SortedDictionary<int, Dictionary<int, string>> ReadOpenXml(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var shared = ReadSharedStrings(zip);
                var sheetEntry = FindSheetEntry(zip);
                if (sheetEntry == null)
                    throw new InvalidDataException("В файле не найден лист (xl/worksheets/sheetN.xml).");

                var rows = new SortedDictionary<int, Dictionary<int, string>>();
                using (var st = sheetEntry.Open())
                {
                    var doc = XDocument.Load(st);
                    foreach (var row in doc.Descendants(NS + "row"))
                    {
                        foreach (var c in row.Elements(NS + "c"))
                        {
                            var reference = (string)c.Attribute("r");
                            if (string.IsNullOrEmpty(reference)) continue;
                            ParseRef(reference, out int col, out int rowIdx);

                            string val = null;
                            var t = (string)c.Attribute("t");
                            var v = c.Element(NS + "v");
                            var isEl = c.Element(NS + "is");

                            if (t == "s" && v != null)
                            {
                                if (int.TryParse(v.Value, out int idx) && idx >= 0 && idx < shared.Count)
                                    val = shared[idx];
                            }
                            else if (t == "inlineStr" && isEl != null)
                            {
                                val = string.Concat(isEl.Descendants(NS + "t").Select(x => x.Value));
                            }
                            else if (v != null)
                            {
                                val = v.Value;
                            }

                            if (val == null) continue;
                            if (!rows.TryGetValue(rowIdx, out var dict))
                            {
                                dict = new Dictionary<int, string>();
                                rows[rowIdx] = dict;
                            }
                            dict[col] = val;
                        }
                    }
                }
                return rows;
            }
        }

        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            var list = new List<string>();
            var entry = zip.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, "xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase));
            if (entry == null) return list;

            using (var st = entry.Open())
            {
                var doc = XDocument.Load(st);
                foreach (var si in doc.Descendants(NS + "si"))
                    list.Add(string.Concat(si.Descendants(NS + "t").Select(x => x.Value)));
            }
            return list;
        }

        private static ZipArchiveEntry FindSheetEntry(ZipArchive zip)
        {
            var exact = zip.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            return zip.Entries
                .Where(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                            && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static readonly Regex RefRe = new Regex(@"^([A-Za-z]+)(\d+)$", RegexOptions.Compiled);

        private static void ParseRef(string reference, out int col, out int row)
        {
            var m = RefRe.Match(reference);
            col = 0; row = 0;
            if (!m.Success) return;
            foreach (char ch in m.Groups[1].Value.ToUpperInvariant())
                col = col * 26 + (ch - 'A' + 1);
            int.TryParse(m.Groups[2].Value, out row);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ObrasReport.Core
{
    /// <summary>
    /// Минимальный устойчивый читатель .xlsx: читает первый лист как таблицу ячеек.
    /// Не зависит от регистра имени xl/SharedStrings.xml (частая проблема выгрузок 1С),
    /// поддерживает shared/inline строки и числовые значения.
    /// </summary>
    public static class XlsxReader
    {
        private static readonly XNamespace NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        /// <summary>Строки листа: индекс строки (1-based) -> (индекс колонки 1-based -> значение).</summary>
        public static SortedDictionary<int, Dictionary<int, string>> ReadFirstSheet(string path)
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
            // предпочтительно sheet1.xml, иначе первый по имени лист
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

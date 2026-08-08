using System;
using System.Collections.Generic;
using System.Linq;
using ObrasReport.Models;

namespace ObrasReport.Core
{
    /// <summary>Нейтральное сравнение произвольных таблиц по ключевому столбцу.</summary>
    public static class UniversalEngine
    {
        public class Input
        {
            public UniversalTable Table;
            public DateTime Date;
            public string Label;
        }

        public static UniversalReportModel Build(
            List<Input> inputs, int keyPos, int trackedPos, List<int> displayPositions,
            Func<int, string> colName)
        {
            if (inputs.Count < 2)
                throw new InvalidOperationException("Для сравнения требуется не менее двух файлов.");
            if (keyPos <= 0)
                throw new InvalidOperationException("Не выбран ключевой столбец (ID).");

            var snaps = inputs.OrderBy(i => i.Date).ThenBy(i => i.Table.FileName, StringComparer.OrdinalIgnoreCase).ToList();
            displayPositions = displayPositions ?? new List<int>();

            // key -> row по каждому срезу (первое вхождение ключа)
            var maps = new List<Dictionary<string, Dictionary<int, string>>>();
            foreach (var s in snaps)
            {
                var m = new Dictionary<string, Dictionary<int, string>>();
                foreach (var row in s.Table.DataRows)
                {
                    string key = Val(row, keyPos);
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    if (!m.ContainsKey(key)) m[key] = row;
                }
                maps.Add(m);
            }

            var model = new UniversalReportModel
            {
                KeyHeader = colName(keyPos),
                TrackedHeader = trackedPos > 0 ? colName(trackedPos) : null,
                DisplayHeaders = displayPositions.Select(colName).ToList(),
                DateLabels = snaps.Select(s => s.Label).ToList(),
            };

            var allKeys = new SortedSet<string>();
            foreach (var m in maps) foreach (var k in m.Keys) allKeys.Add(k);

            int idx = 0;
            foreach (var key in allKeys)
            {
                idx++;
                var present = Enumerable.Range(0, snaps.Count).Where(i => maps[i].ContainsKey(key)).ToList();
                bool inFirst = maps[0].ContainsKey(key);
                bool inLast = maps[maps.Count - 1].ContainsKey(key);
                var lastRow = maps[present.Last()][key];
                var firstRow = maps[present.First()][key];

                var row = new UniversalRow { Index = idx, Key = key };
                row.DisplayValues = displayPositions.Select(p => Val(lastRow, p)).ToList();
                row.TrackedByDate = snaps.Select(s =>
                    maps[snaps.IndexOf(s)].TryGetValue(key, out var r)
                        ? (trackedPos > 0 ? Val(r, trackedPos) : "") : "—нет—").ToList();

                bool changed;
                if (trackedPos > 0)
                {
                    var traj = present.Select(i => Val(maps[i][key], trackedPos)).Distinct().Count();
                    changed = traj > 1;
                }
                else
                {
                    changed = displayPositions.Any(p => Val(firstRow, p) != Val(lastRow, p));
                }

                if (!inLast) { row.Itog = "Удалено"; row.Kind = "removed"; model.Removed++; }
                else if (!inFirst) { row.Itog = "Добавлено"; row.Kind = "added"; model.Added++; }
                else if (changed) { row.Itog = "Изменено"; row.Kind = "changed"; model.Changed++; }
                else { row.Itog = "Без изменений"; row.Kind = "same"; model.Same++; }

                model.Rows.Add(row);
            }

            return model;
        }

        private static string Val(Dictionary<int, string> row, int pos)
        {
            if (pos <= 0 || row == null) return "";
            return row.TryGetValue(pos, out var v) ? (v ?? "").Trim() : "";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using ObrasReport.Models;

namespace ObrasReport.Core
{
    /// <summary>Строит отчёт динамики закрытых обращений/нарядов по периодам.</summary>
    public static class ClosedTrendEngine
    {
        public static TrendReportModel Build(List<TrendSnapshot> snapshots)
        {
            if (snapshots.Count < 2)
                throw new InvalidOperationException("Для динамики требуется не менее двух периодов.");

            var snaps = snapshots
                .OrderBy(s => s.Date)
                .ThenBy(s => s.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var model = new TrendReportModel { DateLabels = snaps.Select(s => s.Label).ToList() };

            var resps = new SortedSet<string>();
            foreach (var s in snaps) foreach (var r in s.ByResp.Keys) resps.Add(r);

            model.TotalObrClosed = Enumerable.Repeat(0, snaps.Count).ToList();
            model.TotalNarClosed = Enumerable.Repeat(0, snaps.Count).ToList();

            int idx = 0;
            foreach (var resp in resps)
            {
                idx++;
                var row = new TrendRow { Index = idx, Responsible = resp };
                for (int i = 0; i < snaps.Count; i++)
                {
                    var m = snaps[i].ByResp.TryGetValue(resp, out var v) ? v : new TrendMetrics();
                    row.ObrClosed.Add(m.ObrClosed);
                    row.NarClosed.Add(m.NarClosed);
                    model.TotalObrClosed[i] += m.ObrClosed;
                    model.TotalNarClosed[i] += m.NarClosed;
                }
                row.ObrDelta = row.ObrClosed.Last() - row.ObrClosed.First();
                row.NarDelta = row.NarClosed.Last() - row.NarClosed.First();
                model.Rows.Add(row);
            }

            model.TotalObrDelta = model.TotalObrClosed.Last() - model.TotalObrClosed.First();
            model.TotalNarDelta = model.TotalNarClosed.Last() - model.TotalNarClosed.First();
            return model;
        }
    }
}

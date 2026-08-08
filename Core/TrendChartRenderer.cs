using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ObrasReport.Models;
using ScottPlot;

namespace ObrasReport.Core
{
    /// <summary>Картинка графика для предпросмотра и вставки в Excel.</summary>
    public class ChartImage
    {
        public string Title;
        public byte[] Png;
    }

    /// <summary>Рендер диаграмм динамики закрытых обращений/нарядов (ScottPlot → PNG).</summary>
    public static class TrendChartRenderer
    {
        private static readonly Color BrandBlue = ColorTranslator.FromHtml("#1F4E78");
        private static readonly Color Green = ColorTranslator.FromHtml("#2E7D32");
        private static readonly Color Accent = ColorTranslator.FromHtml("#5B8DB8");

        private const int Width = 900;
        private const int Height = 420;

        /// <summary>
        /// Строит набор графиков. При ошибке отдельного графика пропускает его;
        /// при полной неудаче возвращает пустой список (не бросает наружу).
        /// </summary>
        public static List<ChartImage> RenderAll(TrendReportModel model)
        {
            var list = new List<ChartImage>();
            if (model == null || model.DateLabels == null || model.DateLabels.Count == 0)
                return list;

            TryAdd(list, () => LineTotals(model, obr: true));
            TryAdd(list, () => LineTotals(model, obr: false));
            TryAdd(list, () => GroupedTotals(model));
            TryAdd(list, () => TopResponsibles(model));
            return list;
        }

        private static void TryAdd(List<ChartImage> list, Func<ChartImage> factory)
        {
            try
            {
                var img = factory();
                if (img?.Png != null && img.Png.Length > 0)
                    list.Add(img);
            }
            catch
            {
                /* best-effort: один сломанный график не роняет весь набор */
            }
        }

        private static ChartImage LineTotals(TrendReportModel model, bool obr)
        {
            var values = obr ? model.TotalObrClosed : model.TotalNarClosed;
            string title = obr
                ? "Решено обращений по периодам"
                : "Решено нарядов по периодам";
            var color = obr ? BrandBlue : Green;

            double[] xs = Enumerable.Range(0, values.Count).Select(i => (double)i).ToArray();
            double[] ys = values.Select(v => (double)v).ToArray();

            var plt = NewPlot(title);
            var scatter = plt.AddScatter(xs, ys, color: color, lineWidth: 2.5f, markerSize: 8);
            scatter.Smooth = false;

            for (int i = 0; i < ys.Length; i++)
                plt.AddText(ys[i].ToString("0"), xs[i], ys[i], size: 11, color: color);

            plt.XTicks(xs, model.DateLabels.ToArray());
            plt.YLabel(obr ? "Закрыто обращений" : "Закрыто нарядов");
            plt.SetAxisLimits(yMin: 0);

            return ToImage(title, plt);
        }

        private static ChartImage GroupedTotals(TrendReportModel model)
        {
            const string title = "Обращения и наряды по периодам";
            double[] obr = model.TotalObrClosed.Select(v => (double)v).ToArray();
            double[] nar = model.TotalNarClosed.Select(v => (double)v).ToArray();

            var plt = NewPlot(title);
            string[] seriesLabels = { "Обращения", "Наряды" };
            double[][] series = { obr, nar };
            var bars = plt.AddBarGroups(model.DateLabels.ToArray(), seriesLabels, series, null);
            if (bars != null && bars.Length >= 2)
            {
                bars[0].FillColor = BrandBlue;
                bars[1].FillColor = Green;
                foreach (var b in bars)
                {
                    b.BorderColor = Color.Transparent;
                    b.ShowValuesAboveBars = true;
                }
            }
            plt.Legend(location: Alignment.UpperRight);
            plt.YLabel("Закрыто");
            plt.SetAxisLimits(yMin: 0);

            return ToImage(title, plt);
        }

        private static ChartImage TopResponsibles(TrendReportModel model)
        {
            const string title = "Топ‑10 ответственных (закрытые обращения, последний период)";
            int last = model.DateLabels.Count - 1;
            string lastLabel = model.DateLabels[last];

            var top = model.Rows
                .Select(r => new
                {
                    Name = Truncate(r.Responsible, 28),
                    Closed = r.ObrClosed.Count > last ? r.ObrClosed[last] : 0,
                    Delta = r.ObrDelta
                })
                .OrderByDescending(x => x.Closed)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .Reverse() // снизу вверх для горизонтальных баров
                .ToList();

            if (top.Count == 0)
            {
                var empty = NewPlot(title + " — нет данных");
                return ToImage(title, empty);
            }

            double[] values = top.Select(t => (double)t.Closed).ToArray();
            double[] positions = Enumerable.Range(0, top.Count).Select(i => (double)i).ToArray();
            string[] labels = top.Select(t =>
            {
                string d = t.Delta > 0 ? $"+{t.Delta}" : t.Delta.ToString();
                return $"{t.Name} ({d})";
            }).ToArray();

            var plt = NewPlot($"{title}\nПериод: {lastLabel}");
            var bar = plt.AddBar(values, positions);
            bar.Orientation = Orientation.Horizontal;
            bar.FillColor = Accent;
            bar.BorderColor = BrandBlue;
            bar.ShowValuesAboveBars = true;

            plt.YTicks(positions, labels);
            plt.XLabel("Закрыто обращений");
            plt.SetAxisLimits(xMin: 0);
            plt.YAxis.Layout(padding: 10, minimumSize: 180);

            return ToImage(title, plt);
        }

        private static Plot NewPlot(string title)
        {
            var plt = new Plot(Width, Height);
            plt.Title(title, size: 14, color: BrandBlue, bold: true);
            plt.Style(figureBackground: Color.White, dataBackground: Color.FromArgb(247, 250, 252));
            plt.Grid(color: Color.FromArgb(220, 227, 234));
            return plt;
        }

        private static ChartImage ToImage(string title, Plot plt)
        {
            // GetImageBytes возвращает PNG
            byte[] png = plt.GetImageBytes(lowQuality: false, scale: 1);
            return new ChartImage { Title = title, Png = png };
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "—";
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }
    }
}

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

    /// <summary>Рендер графиков и диаграмм динамики закрытых обращений/нарядов (ScottPlot → PNG).</summary>
    public static class TrendChartRenderer
    {
        private const int Width = 980;
        private const int Height = 520;

        public static List<ChartImage> RenderAll(TrendReportModel model, ReportTheme theme = null)
        {
            var t = theme ?? ReportTheme.Get("Синяя");
            var list = new List<ChartImage>();
            if (model == null || model.DateLabels == null || model.DateLabels.Count == 0)
                return list;

            TryAdd(list, () => LineTotals(model, obr: true, t));
            TryAdd(list, () => LineTotals(model, obr: false, t));
            TryAdd(list, () => GroupedTotals(model, t));
            TryAdd(list, () => TopResponsibles(model, t));
            TryAdd(list, () => PieShareResponsibles(model, t));
            TryAdd(list, () => PieObrVsNar(model, t));
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
            catch { /* best-effort */ }
        }

        private static ChartImage LineTotals(TrendReportModel model, bool obr, ReportTheme t)
        {
            var values = obr ? model.TotalObrClosed : model.TotalNarClosed;
            string title = obr
                ? "Решено обращений по периодам"
                : "Решено нарядов по периодам";
            var color = obr ? ColorTranslator.FromHtml(t.Brand) : ColorTranslator.FromHtml(t.GreenBright);

            double[] xs = Enumerable.Range(0, values.Count).Select(i => (double)i).ToArray();
            double[] ys = values.Select(v => (double)v).ToArray();
            double yMax = ys.Length == 0 ? 1 : Math.Max(ys.Max(), 1);
            double topPad = Math.Max(yMax * 0.22, 2);

            var plt = NewPlot(title, t);
            plt.AddScatter(xs, ys, color: color, lineWidth: 2.5f, markerSize: 9);

            for (int i = 0; i < ys.Length; i++)
            {
                var txt = plt.AddText(ys[i].ToString("0"), xs[i], ys[i] + topPad * 0.35, size: 11, color: color);
                txt.Alignment = Alignment.LowerCenter;
            }

            plt.XTicks(xs, model.DateLabels.ToArray());
            plt.YLabel(obr ? "Закрыто обращений" : "Закрыто нарядов");
            plt.SetAxisLimits(xMin: -0.35, xMax: xs.Length - 0.65, yMin: 0, yMax: yMax + topPad);
            ApplyCartesianLayout(plt);

            return ToImage(title, plt);
        }

        private static ChartImage GroupedTotals(TrendReportModel model, ReportTheme t)
        {
            const string title = "Обращения и наряды по периодам";
            double[] obr = model.TotalObrClosed.Select(v => (double)v).ToArray();
            double[] nar = model.TotalNarClosed.Select(v => (double)v).ToArray();
            double yMax = Math.Max(obr.DefaultIfEmpty(0).Max(), nar.DefaultIfEmpty(0).Max());
            yMax = Math.Max(yMax, 1);

            var plt = NewPlot(title, t);
            string[] seriesLabels = { "Обращения", "Наряды" };
            double[][] series = { obr, nar };
            var bars = plt.AddBarGroups(model.DateLabels.ToArray(), seriesLabels, series, null);
            if (bars != null)
            {
                if (bars.Length >= 1) bars[0].FillColor = ColorTranslator.FromHtml(t.Brand);
                if (bars.Length >= 2) bars[1].FillColor = ColorTranslator.FromHtml(t.GreenBright);
                foreach (var b in bars)
                {
                    b.BorderColor = Color.Transparent;
                    b.ShowValuesAboveBars = true;
                    b.Font.Size = 10;
                }
            }

            plt.Legend(location: Alignment.UpperLeft);
            plt.YLabel("Закрыто");
            plt.SetAxisLimits(yMin: 0, yMax: yMax * 1.35);
            ApplyCartesianLayout(plt, right: 30, top: 70);

            return ToImage(title, plt);
        }

        private static ChartImage TopResponsibles(TrendReportModel model, ReportTheme t)
        {
            const string title = "Топ‑10 ответственных (закрытые обращения)";
            int last = model.DateLabels.Count - 1;
            string lastLabel = model.DateLabels[last];

            var top = model.Rows
                .Select(r => new
                {
                    Name = Truncate(r.Responsible, 24),
                    Closed = r.ObrClosed.Count > last ? r.ObrClosed[last] : 0,
                    Delta = r.ObrDelta
                })
                .OrderByDescending(x => x.Closed)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .Reverse()
                .ToList();

            if (top.Count == 0)
                return ToImage(title, NewPlot(title + " — нет данных", t));

            double[] values = top.Select(t2 => (double)t2.Closed).ToArray();
            double[] positions = Enumerable.Range(0, top.Count).Select(i => (double)i).ToArray();
            string[] labels = top.Select(t2 => t2.Name).ToArray();
            double xMax = Math.Max(values.DefaultIfEmpty(0).Max(), 1);

            var brand = ColorTranslator.FromHtml(t.Brand);
            var accent = ColorTranslator.FromHtml(t.Accent);
            var plt = NewPlot(title, t);
            plt.XLabel("Период: " + lastLabel + "   ·   Закрыто обращений");

            var bar = plt.AddBar(values, positions);
            bar.Orientation = Orientation.Horizontal;
            bar.FillColor = accent;
            bar.BorderColor = brand;
            bar.ShowValuesAboveBars = false;

            for (int i = 0; i < values.Length; i++)
            {
                string d = top[i].Delta > 0 ? $"+{top[i].Delta}" : top[i].Delta.ToString();
                string caption = $"{values[i]:0} ({d})";
                var txt = plt.AddText(caption, values[i] + xMax * 0.02, positions[i], size: 10, color: brand);
                txt.Alignment = Alignment.MiddleLeft;
            }

            plt.YTicks(positions, labels);
            plt.SetAxisLimits(xMin: 0, xMax: xMax * 1.28, yMin: -0.7, yMax: top.Count - 0.3);
            ApplyCartesianLayout(plt, left: 220, right: 40, bottom: 55, top: 55);
            plt.YAxis.TickLabelStyle(rotation: 0);

            return ToImage(title, plt);
        }

        private static ChartImage PieShareResponsibles(TrendReportModel model, ReportTheme t)
        {
            int last = model.DateLabels.Count - 1;
            string lastLabel = model.DateLabels[last];
            const string title = "Диаграмма: доля закрытых обращений";

            var ranked = model.Rows
                .Select(r => new { Name = Truncate(r.Responsible, 26), Closed = r.ObrClosed.Count > last ? r.ObrClosed[last] : 0 })
                .Where(x => x.Closed > 0)
                .OrderByDescending(x => x.Closed)
                .ToList();

            if (ranked.Count == 0)
                return ToImage(title, NewPlot(title + " — нет данных", t));

            const int topN = 8;
            var top = ranked.Take(topN).ToList();
            int other = ranked.Skip(topN).Sum(x => x.Closed);
            var valuesList = top.Select(t2 => (double)t2.Closed).ToList();
            var namesList = top.Select(t2 => t2.Name).ToList();
            if (other > 0)
            {
                valuesList.Add(other);
                namesList.Add("Прочие");
            }

            double total = valuesList.Sum();
            double[] values = valuesList.ToArray();
            string[] legend = namesList
                .Select((n, i) => $"{n} — {values[i]:0} ({Pct(values[i], total)})")
                .ToArray();

            var plt = NewPiePlot(title, "Период: " + lastLabel, t);
            var pie = plt.AddPie(values);
            pie.ShowLabels = false;
            pie.ShowPercentages = false;
            pie.ShowValues = false;
            pie.Size = 0.55;
            pie.LegendLabels = legend;
            plt.Legend(true, Alignment.MiddleRight);
            ApplyPieLayout(plt);

            return ToImage(title, plt);
        }

        private static ChartImage PieObrVsNar(TrendReportModel model, ReportTheme t)
        {
            int last = model.DateLabels.Count - 1;
            string lastLabel = model.DateLabels[last];
            const string title = "Диаграмма: обращения и наряды";

            double obr = model.TotalObrClosed[last];
            double nar = model.TotalNarClosed[last];
            if (obr <= 0 && nar <= 0)
                return ToImage(title, NewPlot(title + " — нет данных", t));

            double[] values = { Math.Max(obr, 0), Math.Max(nar, 0) };
            double total = values.Sum();
            string[] legend =
            {
                $"Обращения — {values[0]:0} ({Pct(values[0], total)})",
                $"Наряды — {values[1]:0} ({Pct(values[1], total)})"
            };

            var brand = ColorTranslator.FromHtml(t.Brand);
            var green = ColorTranslator.FromHtml(t.GreenBright);
            var plt = NewPiePlot(title, "Период: " + lastLabel + "   ·   Итого: " + total.ToString("0"), t);
            var pie = plt.AddPie(values);
            pie.ShowLabels = false;
            pie.ShowPercentages = false;
            pie.ShowValues = false;
            pie.DonutSize = 0.4;
            pie.Size = 0.55;
            pie.DonutLabel = total.ToString("0");
            pie.CenterFont.Size = 16;
            pie.CenterFont.Bold = true;
            pie.CenterFont.Color = brand;
            pie.SliceFillColors = new[] { brand, green };
            pie.LegendLabels = legend;
            plt.Legend(true, Alignment.MiddleRight);
            ApplyPieLayout(plt);

            return ToImage(title, plt);
        }

        private static string Pct(double part, double total) =>
            total <= 0 ? "0%" : (part / total * 100).ToString("0") + "%";

        private static Plot NewPlot(string title, ReportTheme t)
        {
            var brand = ColorTranslator.FromHtml(t.Brand);
            var plt = new Plot(Width, Height);
            plt.Title(title, size: 15, color: brand, bold: true);
            plt.Style(figureBackground: Color.White, dataBackground: Color.FromArgb(247, 250, 252));
            plt.Grid(color: Color.FromArgb(220, 227, 234));
            return plt;
        }

        private static Plot NewPiePlot(string title, string subtitle, ReportTheme t)
        {
            var brand = ColorTranslator.FromHtml(t.Brand);
            var plt = new Plot(Width, Height);
            plt.Title(title, size: 15, color: brand, bold: true);
            plt.XLabel(subtitle);
            plt.Style(figureBackground: Color.White, dataBackground: Color.White);
            plt.Grid(enable: false);
            plt.XAxis.Ticks(false);
            plt.YAxis.Ticks(false);
            plt.XAxis.Line(false);
            plt.YAxis.Line(false);
            return plt;
        }

        private static void ApplyCartesianLayout(Plot plt, float left = 70, float right = 25, float bottom = 55, float top = 60)
        {
            plt.Layout(left: left, right: right, bottom: bottom, top: top);
        }

        private static void ApplyPieLayout(Plot plt)
        {
            plt.Layout(left: 40, right: 320, bottom: 50, top: 55);
        }

        private static ChartImage ToImage(string title, Plot plt)
        {
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
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ObrasReport.Models;
using ScottPlot;

namespace ObrasReport.Core
{
    public enum TrendRankingMetric
    {
        ClosedAppeals,
        ClosedWorkOrders,
        TotalClosed,
    }

    /// <summary>Картинка графика для предпросмотра и вставки в Excel.</summary>
    public class ChartImage
    {
        public string Title;
        public string Description;
        public byte[] Png;
    }

    /// <summary>Рендер графиков и диаграмм динамики закрытых обращений/нарядов (ScottPlot → PNG).</summary>
    public static class TrendChartRenderer
    {
        private const int Width = 980;
        private const int Height = 520;

        public static List<ChartImage> RenderAll(TrendReportModel model, ReportTheme theme = null,
            int? rankingLimit = 10, IEnumerable<TrendRankingMetric> rankingMetrics = null)
        {
            var t = theme ?? ReportTheme.Get("Синяя");
            var list = new List<ChartImage>();
            if (model == null || model.DateLabels == null || model.DateLabels.Count == 0)
                return list;

            TryAdd(list, () => LineTotals(model, obr: true, t));
            TryAdd(list, () => LineTotals(model, obr: false, t));
            TryAdd(list, () => GroupedTotals(model, t));
            TryAdd(list, () => PeriodComparison(model, t));
            var metrics = (rankingMetrics ?? new[]
                {
                    TrendRankingMetric.ClosedAppeals,
                    TrendRankingMetric.ClosedWorkOrders,
                })
                .Distinct()
                .ToList();
            foreach (var metric in metrics)
            {
                var selectedMetric = metric;
                TryAdd(list, () => ResponsibleRanking(model, t, selectedMetric, rankingLimit));
            }
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

        private static ChartImage PeriodComparison(TrendReportModel model, ReportTheme t)
        {
            const string title = "Последний период и предыдущий";
            int last = model.DateLabels.Count - 1;
            if (last < 1)
                return ToImage(title, NewPlot(title + " — нет данных", t));

            int previous = last - 1;
            string[] metricLabels = { "Закрытые обращения", "Закрытые наряды" };
            string[] seriesLabels = { model.DateLabels[previous], model.DateLabels[last] };
            double[][] series =
            {
                new[] { (double)model.TotalObrClosed[previous], model.TotalNarClosed[previous] },
                new[] { (double)model.TotalObrClosed[last], model.TotalNarClosed[last] },
            };
            double yMax = Math.Max(series.SelectMany(x => x).DefaultIfEmpty(0).Max(), 1);

            var plt = NewPlot(title, t);
            var bars = plt.AddBarGroups(metricLabels, seriesLabels, series, null);
            if (bars != null)
            {
                var colors = new[] { ColorTranslator.FromHtml(t.Accent), ColorTranslator.FromHtml(t.Brand) };
                for (int i = 0; i < bars.Length; i++)
                {
                    bars[i].FillColor = colors[Math.Min(i, colors.Length - 1)];
                    bars[i].BorderColor = Color.Transparent;
                    bars[i].ShowValuesAboveBars = true;
                    bars[i].Font.Size = 10;
                }
            }
            plt.Legend(location: Alignment.UpperLeft);
            plt.YLabel("Закрыто");
            plt.SetAxisLimits(yMin: 0, yMax: yMax * 1.38);
            ApplyCartesianLayout(plt, bottom: 70, top: 75);
            return ToImage(title, plt, BuildPeriodComparisonText(model));
        }

        public static string BuildPeriodComparisonText(TrendReportModel model)
        {
            if (model?.DateLabels == null || model.DateLabels.Count < 2)
                return "Для сравнения нужны минимум два периода.";
            int last = model.DateLabels.Count - 1;
            int previous = last - 1;
            int obrCurrent = model.TotalObrClosed[last];
            int obrPrevious = model.TotalObrClosed[previous];
            int narCurrent = model.TotalNarClosed[last];
            int narPrevious = model.TotalNarClosed[previous];
            return $"Период {model.DateLabels[last]} относительно {model.DateLabels[previous]}: " +
                $"закрытые обращения {obrCurrent} ({DeltaWithPercent(obrCurrent, obrPrevious)}); " +
                $"закрытые наряды {narCurrent} ({DeltaWithPercent(narCurrent, narPrevious)}).";
        }

        private static ChartImage ResponsibleRanking(TrendReportModel model, ReportTheme t,
            TrendRankingMetric metric, int? limit)
        {
            int last = model.DateLabels.Count - 1;
            string lastLabel = model.DateLabels[last];
            Func<TrendRow, int> current;
            Func<TrendRow, int> delta;
            string subject;
            string axis;
            Color fill;

            switch (metric)
            {
                case TrendRankingMetric.ClosedWorkOrders:
                    current = r => r.NarClosed.Count > last ? r.NarClosed[last] : 0;
                    delta = r => r.NarDelta;
                    subject = "закрытые наряды";
                    axis = "Закрыто нарядов";
                    fill = ColorTranslator.FromHtml(t.GreenBright);
                    break;
                case TrendRankingMetric.TotalClosed:
                    current = r => (r.ObrClosed.Count > last ? r.ObrClosed[last] : 0) +
                                   (r.NarClosed.Count > last ? r.NarClosed[last] : 0);
                    delta = r => r.ObrDelta + r.NarDelta;
                    subject = "всего закрыто";
                    axis = "Закрыто обращений и нарядов";
                    fill = ColorTranslator.FromHtml(t.Brand);
                    break;
                default:
                    current = r => r.ObrClosed.Count > last ? r.ObrClosed[last] : 0;
                    delta = r => r.ObrDelta;
                    subject = "закрытые обращения";
                    axis = "Закрыто обращений";
                    fill = ColorTranslator.FromHtml(t.Accent);
                    break;
            }

            string prefix = limit.HasValue ? $"Топ‑{limit.Value}" : "Рейтинг всех ответственных";
            string title = prefix + " — " + subject;

            var top = model.Rows
                .Select(r => new
                {
                    Name = Truncate(r.Responsible, 24),
                    Closed = current(r),
                    Delta = delta(r)
                })
                .Where(x => x.Closed > 0)
                .OrderByDescending(x => x.Closed)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (limit.HasValue) top = top.Take(limit.Value).ToList();
            top.Reverse();

            if (top.Count == 0)
                return ToImage(title, NewPlot(title + " — нет данных", t));

            double[] values = top.Select(t2 => (double)t2.Closed).ToArray();
            double[] positions = Enumerable.Range(0, top.Count).Select(i => (double)i).ToArray();
            string[] labels = top.Select(t2 => t2.Name).ToArray();
            double xMax = Math.Max(values.DefaultIfEmpty(0).Max(), 1);

            var brand = ColorTranslator.FromHtml(t.Brand);
            int height = Math.Max(Height, 175 + top.Count * 34);
            var plt = new Plot(Width, height);
            plt.Title(title, size: 15, color: brand, bold: true);
            plt.Style(figureBackground: Color.White, dataBackground: Color.FromArgb(247, 250, 252));
            plt.Grid(color: Color.FromArgb(220, 227, 234));
            plt.XLabel("Период: " + lastLabel + "   ·   " + axis);

            var bar = plt.AddBar(values, positions);
            bar.Orientation = Orientation.Horizontal;
            bar.FillColor = fill;
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

            return ToImage(title, plt,
                "Значение у столбца — результат последнего периода; в скобках — изменение между первым и последним загруженными периодами.");
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
            pie.SliceLabels = legend;
            pie.LegendLabels = legend;
            plt.Legend(true, Alignment.MiddleRight);
            ApplyPieLayout(plt);

            string description = string.Join(" · ", legend) +
                ". Процент показывает долю закрытых обращений ответственного среди всех закрытых обращений за период.";
            return ToImage(title, plt, description);
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
            pie.SliceLabels = legend;
            pie.LegendLabels = legend;
            plt.Legend(true, Alignment.MiddleRight);
            ApplyPieLayout(plt);

            string description =
                $"Закрытые обращения — {obr:0} ({Pct(obr, total)}) · " +
                $"закрытые наряды — {nar:0} ({Pct(nar, total)}). " +
                "Проценты показывают соотношение закрытых обращений и нарядов за последний период.";
            return ToImage(title, plt, description);
        }

        private static string Pct(double part, double total) =>
            total <= 0 ? "0%" : (part / total * 100).ToString("0") + "%";

        private static string DeltaWithPercent(int current, int previous)
        {
            int delta = current - previous;
            string signed = delta > 0 ? "+" + delta : delta.ToString();
            if (previous == 0)
                return signed + ", процент не рассчитывается: в предыдущем периоде 0";
            double percent = delta * 100.0 / previous;
            string signedPercent = percent > 0 ? "+" + percent.ToString("0.0") : percent.ToString("0.0");
            return signed + ", " + signedPercent + "%";
        }

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

        private static ChartImage ToImage(string title, Plot plt, string description = null)
        {
            byte[] png = plt.GetImageBytes(lowQuality: false, scale: 1);
            return new ChartImage { Title = title, Description = description, Png = png };
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "—";
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }
    }
}

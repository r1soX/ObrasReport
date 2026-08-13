using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ObrasReport.Models;
using ScottPlot;

namespace ObrasReport.Core
{
    /// <summary>
    /// Графики и диаграммы для отчёта по обращениям (по ремонту / видеонаблюдение / не по ремонту).
    /// Каждый набор строится отдельно для своей категории (ReportModel.CategoryLabel).
    /// </summary>
    public static class ReportChartRenderer
    {
        private const int Width = 980;
        private const int Height = 520;

        public static List<ChartImage> RenderAll(ReportModel model, ReportTheme theme = null)
        {
            var t = theme ?? ReportTheme.Get("Синяя");
            var list = new List<ChartImage>();
            if (model == null || model.DateStats == null || model.DateStats.Count == 0)
                return list;

            TryAdd(list, () => LineTotals(model, t));
            if (model.Layout == LayoutType.Repairs)
                TryAdd(list, () => SeverityByDate(model, t));
            TryAdd(list, () => DynamicsBars(model, t));
            TryAdd(list, () => PieItog(model, t));
            if (model.Layout == LayoutType.Repairs)
                TryAdd(list, () => PieSeverityLast(model, t));
            TryAdd(list, () => TopOnControl(model, t));
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

        private static string Cat(ReportModel m) =>
            string.IsNullOrWhiteSpace(m.CategoryLabel)
                ? (m.Layout == LayoutType.Repairs ? "По ремонту" : "Не по ремонту")
                : m.CategoryLabel.Trim();

        private static string T(ReportModel m, string name) => $"[{Cat(m)}] {name}";

        private static ChartImage LineTotals(ReportModel model, ReportTheme t)
        {
            string title = T(model, "Количество обращений по датам");
            var labels = model.DateStats.Select(d => d.Label).ToArray();
            double[] xs = Enumerable.Range(0, labels.Length).Select(i => (double)i).ToArray();
            double[] ys = model.DateStats.Select(d => (double)d.TotalUnique).ToArray();
            double yMax = Math.Max(ys.DefaultIfEmpty(0).Max(), 1);
            double topPad = Math.Max(yMax * 0.22, 2);

            var brand = ColorTranslator.FromHtml(t.Brand);
            var plt = NewPlot(title, t);
            plt.AddScatter(xs, ys, color: brand, lineWidth: 2.5f, markerSize: 9);
            for (int i = 0; i < ys.Length; i++)
            {
                var txt = plt.AddText(ys[i].ToString("0"), xs[i], ys[i] + topPad * 0.35, size: 11, color: brand);
                txt.Alignment = Alignment.LowerCenter;
            }
            plt.XTicks(xs, labels);
            plt.YLabel("Обращений");
            plt.SetAxisLimits(xMin: -0.35, xMax: xs.Length - 0.65, yMin: 0, yMax: yMax + topPad);
            ApplyCartesian(plt);
            return ToImage(title, plt);
        }

        private static ChartImage SeverityByDate(ReportModel model, ReportTheme t)
        {
            string title = T(model, "Критичность по датам");
            var labels = model.DateStats.Select(d => d.Label).ToArray();
            double[] black = model.DateStats.Select(d => (double)d.Black).ToArray();
            double[] red = model.DateStats.Select(d => (double)d.Red).ToArray();
            double[] yellow = model.DateStats.Select(d => (double)d.Yellow).ToArray();
            double yMax = Math.Max(Math.Max(black.DefaultIfEmpty(0).Max(), red.DefaultIfEmpty(0).Max()),
                yellow.DefaultIfEmpty(0).Max());
            yMax = Math.Max(yMax, 1);

            var plt = NewPlot(title, t);
            string[] seriesLabels = { "Чёрная", "Красная", "Жёлтая" };
            double[][] series = { black, red, yellow };
            var bars = plt.AddBarGroups(labels, seriesLabels, series, null);
            if (bars != null)
            {
                if (bars.Length > 0) { bars[0].FillColor = ColorTranslator.FromHtml(t.Blackish); bars[0].ShowValuesAboveBars = true; bars[0].Font.Size = 9; }
                if (bars.Length > 1) { bars[1].FillColor = ColorTranslator.FromHtml(t.RedText); bars[1].ShowValuesAboveBars = true; bars[1].Font.Size = 9; }
                if (bars.Length > 2) { bars[2].FillColor = ColorTranslator.FromHtml(t.AmberBright); bars[2].ShowValuesAboveBars = true; bars[2].Font.Size = 9; }
                foreach (var b in bars) b.BorderColor = Color.Transparent;
            }
            plt.Legend(location: Alignment.UpperLeft);
            plt.YLabel("Обращений");
            plt.SetAxisLimits(yMin: 0, yMax: yMax * 1.35);
            ApplyCartesian(plt, top: 70);
            return ToImage(title, plt);
        }

        private static ChartImage DynamicsBars(ReportModel model, ReportTheme t)
        {
            string title = T(model, "Динамика между выгрузками");
            if (model.LeftCounts == null || model.LeftCounts.Count == 0)
                return ToImage(title, NewPlot(title + " — нет данных", t));

            var periodLabels = new string[model.LeftCounts.Count];
            for (int i = 0; i < model.LeftCounts.Count; i++)
                periodLabels[i] = model.Snapshots[i].Label + "→" + model.Snapshots[i + 1].Label;

            double[] left = model.LeftCounts.Select(v => (double)v).ToArray();
            double[] neu = model.NewCounts.Select(v => (double)v).ToArray();
            bool repairs = model.Layout == LayoutType.Repairs;
            double[] changed = repairs
                ? model.ChangedCounts.Select(v => (double)v).ToArray()
                : null;

            string[] seriesLabels = repairs
                ? new[] { "Снято с контроля", "Новые", "Изменили состояние" }
                : new[] { "Снято с контроля", "Новые" };
            double[][] series = repairs
                ? new[] { left, neu, changed }
                : new[] { left, neu };

            double yMax = series.SelectMany(s => s).DefaultIfEmpty(0).Max();
            yMax = Math.Max(yMax, 1);

            var plt = NewPlot(title, t);
            var bars = plt.AddBarGroups(periodLabels, seriesLabels, series, null);
            if (bars != null)
            {
                Color[] colors = repairs
                    ? new[] { ColorTranslator.FromHtml(t.GreenBright), ColorTranslator.FromHtml(t.Brand), ColorTranslator.FromHtml(t.AmberBright) }
                    : new[] { ColorTranslator.FromHtml(t.GreenBright), ColorTranslator.FromHtml(t.Brand) };
                for (int i = 0; i < bars.Length && i < colors.Length; i++)
                {
                    bars[i].FillColor = colors[i];
                    bars[i].BorderColor = Color.Transparent;
                    bars[i].ShowValuesAboveBars = true;
                    bars[i].Font.Size = 9;
                }
            }
            plt.Legend(location: Alignment.UpperLeft);
            plt.YLabel("Обращений");
            plt.SetAxisLimits(yMin: 0, yMax: yMax * 1.35);
            ApplyCartesian(plt, bottom: 70, top: 70);
            return ToImage(title, plt);
        }

        private static ChartImage PieItog(ReportModel model, ReportTheme t)
        {
            string title = T(model, "Итог: обработано и на контроле");
            double done = model.ProcessedTotal;
            double ctrl = model.OnControlTotal;
            if (done <= 0 && ctrl <= 0)
                return ToImage(title, NewPlot(title + " — нет данных", t));

            var valuesList = new List<double>();
            var legendList = new List<string>();
            double total = done + ctrl;

            if (done > 0)
            {
                valuesList.Add(done);
                legendList.Add($"Обработано — {done:0} ({Pct(done, total)})");
            }
            if (ctrl > 0)
            {
                if (model.Layout == LayoutType.Repairs && model.OnControlExternal > 0 && model.OnControlExternal < ctrl)
                {
                    double own = ctrl - model.OnControlExternal;
                    if (own > 0)
                    {
                        valuesList.Add(own);
                        legendList.Add($"На контроле — {own:0} ({Pct(own, total)})");
                    }
                    valuesList.Add(model.OnControlExternal);
                    legendList.Add($"На контроле (внешние) — {model.OnControlExternal:0} ({Pct(model.OnControlExternal, total)})");
                }
                else
                {
                    valuesList.Add(ctrl);
                    legendList.Add($"На контроле — {ctrl:0} ({Pct(ctrl, total)})");
                }
            }

            var green = ColorTranslator.FromHtml(t.GreenBright);
            var amber = ColorTranslator.FromHtml(t.AmberBright);
            var accent = ColorTranslator.FromHtml(t.Accent);

            var plt = NewPiePlot(title, "Категория: " + Cat(model), t);
            var pie = plt.AddPie(valuesList.ToArray());
            pie.ShowLabels = false;
            pie.ShowPercentages = false;
            pie.ShowValues = false;
            pie.Size = 0.55;
            pie.LegendLabels = legendList.ToArray();
            if (valuesList.Count >= 2)
                pie.SliceFillColors = valuesList.Count == 2
                    ? new[] { green, amber }
                    : new[] { green, amber, accent };
            else if (done > 0)
                pie.SliceFillColors = new[] { green };
            else
                pie.SliceFillColors = new[] { amber };
            plt.Legend(true, Alignment.MiddleRight);
            ApplyPie(plt);
            return ToImage(title, plt);
        }

        private static ChartImage PieSeverityLast(ReportModel model, ReportTheme t)
        {
            string title = T(model, "Критичность на последнюю дату");
            var last = model.DateStats.Last();
            double[] values = { last.Black, last.Red, last.Yellow };
            if (values.Sum() <= 0)
                return ToImage(title, NewPlot(title + " — нет данных", t));

            var pairs = new[]
            {
                ("Чёрная", values[0], ColorTranslator.FromHtml(t.Blackish)),
                ("Красная", values[1], ColorTranslator.FromHtml(t.RedText)),
                ("Жёлтая", values[2], ColorTranslator.FromHtml(t.AmberBright))
            }.Where(p => p.Item2 > 0).ToList();

            double total = pairs.Sum(p => p.Item2);
            double[] vals = pairs.Select(p => p.Item2).ToArray();
            string[] legend = pairs.Select(p => $"{p.Item1} — {p.Item2:0} ({Pct(p.Item2, total)})").ToArray();
            Color[] colors = pairs.Select(p => p.Item3).ToArray();

            var plt = NewPiePlot(title, "Период: " + last.Label + "   ·   Категория: " + Cat(model), t);
            var pie = plt.AddPie(vals);
            pie.ShowLabels = false;
            pie.ShowPercentages = false;
            pie.ShowValues = false;
            pie.Size = 0.55;
            pie.SliceFillColors = colors;
            pie.LegendLabels = legend;
            plt.Legend(true, Alignment.MiddleRight);
            ApplyPie(plt);
            return ToImage(title, plt);
        }

        private static ChartImage TopOnControl(ReportModel model, ReportTheme t)
        {
            string title = T(model, "Топ‑10 ответственных (на контроле)");
            var top = model.Rows
                .Where(r => !r.Processed)
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Responsible) ? "—" : r.Responsible.Trim())
                .Select(g => new { Name = Truncate(g.Key, 24), Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .Reverse()
                .ToList();

            if (top.Count == 0)
                return ToImage(title, NewPlot(title + " — нет обращений на контроле", t));

            double[] values = top.Select(t2 => (double)t2.Count).ToArray();
            double[] positions = Enumerable.Range(0, top.Count).Select(i => (double)i).ToArray();
            string[] labels = top.Select(t2 => t2.Name).ToArray();
            double xMax = Math.Max(values.DefaultIfEmpty(0).Max(), 1);

            var brand = ColorTranslator.FromHtml(t.Brand);
            var accent = ColorTranslator.FromHtml(t.Accent);
            var plt = NewPlot(title, t);
            plt.XLabel("Категория: " + Cat(model) + "   ·   Обращений на контроле");
            var bar = plt.AddBar(values, positions);
            bar.Orientation = Orientation.Horizontal;
            bar.FillColor = accent;
            bar.BorderColor = brand;
            bar.ShowValuesAboveBars = false;
            for (int i = 0; i < values.Length; i++)
            {
                var txt = plt.AddText(values[i].ToString("0"), values[i] + xMax * 0.02, positions[i], size: 10, color: brand);
                txt.Alignment = Alignment.MiddleLeft;
            }
            plt.YTicks(positions, labels);
            plt.SetAxisLimits(xMin: 0, xMax: xMax * 1.28, yMin: -0.7, yMax: top.Count - 0.3);
            ApplyCartesian(plt, left: 220, right: 40, bottom: 55, top: 55);
            return ToImage(title, plt);
        }

        private static string Pct(double part, double total) =>
            total <= 0 ? "0%" : (part / total * 100).ToString("0") + "%";

        private static Plot NewPlot(string title, ReportTheme t)
        {
            var brand = ColorTranslator.FromHtml(t.Brand);
            var plt = new Plot(Width, Height);
            plt.Title(title, size: 14, color: brand, bold: true);
            plt.Style(figureBackground: Color.White, dataBackground: Color.FromArgb(247, 250, 252));
            plt.Grid(color: Color.FromArgb(220, 227, 234));
            return plt;
        }

        private static Plot NewPiePlot(string title, string subtitle, ReportTheme t)
        {
            var brand = ColorTranslator.FromHtml(t.Brand);
            var plt = new Plot(Width, Height);
            plt.Title(title, size: 14, color: brand, bold: true);
            plt.XLabel(subtitle);
            plt.Style(figureBackground: Color.White, dataBackground: Color.White);
            plt.Grid(enable: false);
            plt.XAxis.Ticks(false);
            plt.YAxis.Ticks(false);
            plt.XAxis.Line(false);
            plt.YAxis.Line(false);
            return plt;
        }

        private static void ApplyCartesian(Plot plt, float left = 70, float right = 25, float bottom = 55, float top = 60) =>
            plt.Layout(left: left, right: right, bottom: bottom, top: top);

        private static void ApplyPie(Plot plt) =>
            plt.Layout(left: 40, right: 340, bottom: 50, top: 55);

        private static ChartImage ToImage(string title, Plot plt) =>
            new ChartImage { Title = title, Png = plt.GetImageBytes(lowQuality: false, scale: 1) };

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "—";
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }
    }
}
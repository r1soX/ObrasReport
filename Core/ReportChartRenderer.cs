using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ObrasReport.Models;
using ScottPlot;

namespace ObrasReport.Core
{
    public enum ResponsibleRankingMetric
    {
        OnControl,
        Closed,
        New,
        NoMovement,
        AverageStateDays,
    }

    /// <summary>
    /// Графики и диаграммы для отчёта по обращениям (по ремонту / видеонаблюдение / не по ремонту).
    /// Каждый набор строится отдельно для своей категории (ReportModel.CategoryLabel).
    /// </summary>
    public static class ReportChartRenderer
    {
        private const int Width = 980;
        private const int Height = 520;

        public static List<ChartImage> RenderAll(ReportModel model, ReportTheme theme = null,
            int? rankingLimit = 10, IEnumerable<ResponsibleRankingMetric> rankingMetrics = null)
        {
            var t = theme ?? ReportTheme.Get("Синяя");
            var list = new List<ChartImage>();
            if (model == null || model.DateStats == null || model.DateStats.Count == 0)
                return list;

            TryAdd(list, () => LineTotals(model, t));
            if (model.Layout == LayoutType.Repairs)
                TryAdd(list, () => SeverityByDate(model, t));
            TryAdd(list, () => DynamicsBars(model, t));
            TryAdd(list, () => PeriodComparison(model, t));
            TryAdd(list, () => PieItog(model, t));
            if (model.Layout == LayoutType.Repairs)
                TryAdd(list, () => PieSeverityLast(model, t));

            var metrics = (rankingMetrics ?? new[]
                {
                    ResponsibleRankingMetric.OnControl,
                    ResponsibleRankingMetric.Closed,
                })
                .Distinct()
                .ToList();
            foreach (var metric in metrics)
            {
                var selectedMetric = metric;
                TryAdd(list, () => ResponsibleRanking(model, t, selectedMetric, rankingLimit));
            }
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
                ? new[] { "Закрытые", "Новые", "Изменили состояние" }
                : new[] { "Закрытые", "Новые" };
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

        private static ChartImage PeriodComparison(ReportModel model, ReportTheme t)
        {
            string title = T(model, "Последний период и предыдущий");
            if (model.Transitions == null || model.Transitions.Count == 0)
                return ToImage(title, NewPlot(title + " — нет данных", t));

            var current = model.Transitions.Last();
            var previous = model.Transitions.Count > 1
                ? model.Transitions[model.Transitions.Count - 2]
                : null;

            var metricLabels = model.Layout == LayoutType.Repairs
                ? new[] { "Закрыто", "Обработано", "На контроле", "Новые" }
                : new[] { "Закрыто", "На контроле", "Новые" };

            Func<TransitionStat, double[]> values = s => model.Layout == LayoutType.Repairs
                ? new[] { (double)s.Closed, s.Processed, s.OnControl, s.New }
                : new[] { (double)s.Closed, s.OnControl, s.New };

            string currentLabel = current.FromLabel + "→" + current.ToLabel;
            string[] seriesLabels;
            double[][] series;
            if (previous != null)
            {
                seriesLabels = new[] { previous.FromLabel + "→" + previous.ToLabel, currentLabel };
                series = new[] { values(previous), values(current) };
            }
            else
            {
                seriesLabels = new[] { currentLabel };
                series = new[] { values(current) };
            }

            double yMax = Math.Max(series.SelectMany(x => x).DefaultIfEmpty(0).Max(), 1);
            var plt = NewPlot(title, t);
            var bars = plt.AddBarGroups(metricLabels, seriesLabels, series, null);
            if (bars != null)
            {
                var colors = previous != null
                    ? new[] { ColorTranslator.FromHtml(t.Accent), ColorTranslator.FromHtml(t.Brand) }
                    : new[] { ColorTranslator.FromHtml(t.Brand) };
                for (int i = 0; i < bars.Length; i++)
                {
                    bars[i].FillColor = colors[Math.Min(i, colors.Length - 1)];
                    bars[i].BorderColor = Color.Transparent;
                    bars[i].ShowValuesAboveBars = true;
                    bars[i].Font.Size = 9;
                }
            }
            plt.Legend(location: Alignment.UpperLeft);
            plt.YLabel("Обращений");
            plt.SetAxisLimits(yMin: 0, yMax: yMax * 1.38);
            ApplyCartesian(plt, bottom: 70, top: 75);
            return ToImage(title, plt, BuildPeriodComparisonText(model));
        }

        public static string BuildPeriodComparisonText(ReportModel model)
        {
            if (model?.Transitions == null || model.Transitions.Count == 0)
                return "Нет переходов между выгрузками для сравнения.";

            var current = model.Transitions.Last();
            var previous = model.Transitions.Count > 1
                ? model.Transitions[model.Transitions.Count - 2]
                : null;
            string prefix = $"Последний период {current.FromLabel}→{current.ToLabel}: ";
            if (previous == null)
            {
                string only = $"закрыто {current.Closed}; на контроле {current.OnControl}; новых {current.New}";
                if (model.Layout == LayoutType.Repairs) only += $"; обработано {current.Processed}";
                return prefix + only + $"; доля закрытия {current.ClosureRate:0.0}%. " +
                    "Для сравнения с предыдущим периодом нужны минимум три выгрузки.";
            }

            string text = prefix +
                $"закрыто {current.Closed} ({Signed(current.Closed - previous.Closed)}); " +
                $"на контроле {current.OnControl} ({Signed(current.OnControl - previous.OnControl)}); " +
                $"новых {current.New} ({Signed(current.New - previous.New)})";
            if (model.Layout == LayoutType.Repairs)
                text += $"; обработано {current.Processed} ({Signed(current.Processed - previous.Processed)})";
            double rateDelta = current.ClosureRate - previous.ClosureRate;
            text += $"; доля закрытия {current.ClosureRate:0.0}% ({Signed(rateDelta, "0.0")} п.п.). " +
                "Доля закрытия — закрытые обращения относительно количества обращений в начале периода.";
            return text;
        }

        private static ChartImage PieItog(ReportModel model, ReportTheme t)
        {
            string title = T(model, "Итог обращений");
            double done = model.ProcessedTotal;
            double closed = model.ClosedTotal;
            double ctrl = model.OnControlTotal;
            if (done <= 0 && closed <= 0 && ctrl <= 0)
                return ToImage(title, NewPlot(title + " — нет данных", t));

            var valuesList = new List<double>();
            var legendList = new List<string>();
            var colorList = new List<Color>();
            double total = done + closed + ctrl;
            var green = ColorTranslator.FromHtml(t.GreenBright);
            var amber = ColorTranslator.FromHtml(t.AmberBright);
            var accent = ColorTranslator.FromHtml(t.Accent);
            var brand = ColorTranslator.FromHtml(t.Brand);

            if (done > 0)
            {
                valuesList.Add(done);
                legendList.Add($"Обработано — {done:0} ({Pct(done, total)})");
                colorList.Add(brand);
            }
            if (closed > 0)
            {
                valuesList.Add(closed);
                legendList.Add($"Закрыто — {closed:0} ({Pct(closed, total)})");
                colorList.Add(green);
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
                        colorList.Add(amber);
                    }
                    valuesList.Add(model.OnControlExternal);
                    legendList.Add($"Внешний контроль — {model.OnControlExternal:0} ({Pct(model.OnControlExternal, total)})");
                    colorList.Add(accent);
                }
                else
                {
                    valuesList.Add(ctrl);
                    legendList.Add($"На контроле — {ctrl:0} ({Pct(ctrl, total)})");
                    colorList.Add(amber);
                }
            }

            var plt = NewPiePlot(title, "Категория: " + Cat(model), t);
            var pie = plt.AddPie(valuesList.ToArray());
            pie.ShowLabels = false;
            pie.ShowPercentages = false;
            pie.ShowValues = false;
            pie.Size = 0.55;
            // ScottPlot 4 формирует элементы легенды круговой диаграммы из
            // SliceLabels, даже когда подписи на самих секторах отключены.
            pie.SliceLabels = legendList.ToArray();
            pie.LegendLabels = legendList.ToArray();
            pie.SliceFillColors = colorList.ToArray();
            plt.Legend(true, Alignment.MiddleRight);
            ApplyPie(plt);
            return ToImage(title, plt, ItogExplanation(model, total));
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

            var plt = NewPiePlot(title,
                "Период: " + last.Label + "   ·   Категория: " + Cat(model), t);
            var pie = plt.AddPie(vals);
            pie.ShowLabels = false;
            pie.ShowPercentages = false;
            pie.ShowValues = false;
            pie.Size = 0.55;
            pie.SliceFillColors = colors;
            pie.SliceLabels = legend;
            pie.LegendLabels = legend;
            plt.Legend(true, Alignment.MiddleRight);
            ApplyPie(plt);
            string description = string.Join(" · ", pairs.Select(p =>
                $"{p.Item1} — {p.Item2:0} ({Pct(p.Item2, total)})")) +
                ". Процент показывает долю уровня среди обращений с указанной критичностью.";
            return ToImage(title, plt, description);
        }

        private sealed class RankingValue
        {
            public string Name;
            public double Value;
        }

        private static ChartImage ResponsibleRanking(ReportModel model, ReportTheme t,
            ResponsibleRankingMetric metric, int? limit)
        {
            Func<ReportRow, bool> filter;
            Func<IEnumerable<ReportRow>, double> aggregate;
            string subject;
            string axis;
            string explanation;
            string valueFormat = "0";
            Color fill;

            switch (metric)
            {
                case ResponsibleRankingMetric.Closed:
                    filter = r => r.Closed;
                    aggregate = rows => rows.Count();
                    subject = "закрытые обращения";
                    axis = "Закрытых обращений";
                    explanation = "Обращения, которых нет в последней выгрузке.";
                    fill = ColorTranslator.FromHtml(t.GreenBright);
                    break;
                case ResponsibleRankingMetric.New:
                    filter = r => r.NewInLast;
                    aggregate = rows => rows.Count();
                    subject = "новые обращения";
                    axis = "Новых обращений";
                    explanation = "Обращения, появившиеся между двумя последними выгрузками.";
                    fill = ColorTranslator.FromHtml(t.Brand);
                    break;
                case ResponsibleRankingMetric.NoMovement:
                    filter = r => r.NoMovement;
                    aggregate = rows => rows.Count();
                    subject = "без движения";
                    axis = "Обращений без изменения состояния";
                    explanation = "Обращения, состояние которых не изменилось между двумя последними выгрузками.";
                    fill = ColorTranslator.FromHtml(t.AmberBright);
                    break;
                case ResponsibleRankingMetric.AverageStateDays:
                    filter = r => !r.Closed && ParseDays(r.Days).HasValue;
                    aggregate = rows => rows.Select(r => ParseDays(r.Days).Value).Average();
                    subject = "среднее время в состоянии";
                    axis = "Среднее число дней в состоянии";
                    explanation = "Среднее значение поля «Дней в состоянии» по обращениям последней выгрузки.";
                    valueFormat = "0.0";
                    fill = ColorTranslator.FromHtml(t.RedText);
                    break;
                default:
                    filter = r => r.OnControlInLast;
                    aggregate = rows => rows.Count();
                    subject = "на контроле исполнения";
                    axis = "Обращений на контроле";
                    explanation = "Обращения, оставшиеся на контроле исполнения по итогам периода.";
                    fill = ColorTranslator.FromHtml(t.Accent);
                    break;
            }

            string prefix = limit.HasValue ? $"Топ‑{limit.Value}" : "Рейтинг всех ответственных";
            string title = T(model, prefix + " — " + subject);
            var ranked = model.Rows
                .Where(filter)
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Responsible) ? "—" : r.Responsible.Trim())
                .Select(g => new RankingValue { Name = Truncate(g.Key, 32), Value = aggregate(g) })
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (limit.HasValue)
                ranked = ranked.Take(limit.Value).ToList();
            ranked.Reverse();

            if (ranked.Count == 0)
                return ToImage(title, NewPlot(title + " — нет данных", t), explanation);

            double[] values = ranked.Select(x => x.Value).ToArray();
            double[] positions = Enumerable.Range(0, ranked.Count).Select(i => (double)i).ToArray();
            string[] labels = ranked.Select(x => x.Name).ToArray();
            double xMax = Math.Max(values.DefaultIfEmpty(0).Max(), 1);

            var brand = ColorTranslator.FromHtml(t.Brand);
            int height = Math.Max(Height, 175 + ranked.Count * 34);
            var plt = NewPlot(title, t, height);
            plt.XLabel("Категория: " + Cat(model) + "   ·   " + axis);
            var bar = plt.AddBar(values, positions);
            bar.Orientation = Orientation.Horizontal;
            bar.FillColor = fill;
            bar.BorderColor = brand;
            bar.ShowValuesAboveBars = false;
            for (int i = 0; i < values.Length; i++)
            {
                var txt = plt.AddText(values[i].ToString(valueFormat), values[i] + xMax * 0.02,
                    positions[i], size: 10, color: brand);
                txt.Alignment = Alignment.MiddleLeft;
            }
            plt.YTicks(positions, labels);
            plt.SetAxisLimits(xMin: 0, xMax: xMax * 1.28, yMin: -0.7, yMax: ranked.Count - 0.3);
            ApplyCartesian(plt, left: 270, right: 40, bottom: 55, top: 55);
            string period = model.Snapshots.Count >= 2
                ? $" Последний переход: {model.Snapshots[model.Snapshots.Count - 2].Label}→{model.Snapshots.Last().Label}."
                : "";
            return ToImage(title, plt, explanation + period);
        }

        private static double? ParseDays(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var match = Regex.Match(value, @"\d+(?:[\.,]\d+)?");
            if (!match.Success) return null;
            string normalized = match.Value.Replace(',', '.');
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var days)
                ? (double?)days
                : null;
        }

        private static string Pct(double part, double total) =>
            total <= 0 ? "0%" : (part / total * 100).ToString("0") + "%";

        private static string Signed(int value) => value > 0 ? "+" + value : value.ToString();

        private static string Signed(double value, string format) =>
            value > 0 ? "+" + value.ToString(format) : value.ToString(format);

        private static string ItogExplanation(ReportModel model, double total)
        {
            if (model.Layout == LayoutType.Repairs)
            {
                string text =
                    $"Обработано — {model.ProcessedTotal} ({Pct(model.ProcessedTotal, total)}): состояние обращения изменилось. " +
                    $"Закрыто — {model.ClosedTotal} ({Pct(model.ClosedTotal, total)}): обращения нет в последней выгрузке. " +
                    $"На контроле исполнения — {model.OnControlTotal} ({Pct(model.OnControlTotal, total)}): обращение остаётся в последней выгрузке без изменения состояния.";
                if (model.OnControlExternal > 0)
                    text += $" Из них ожидают действий внешних сторон — {model.OnControlExternal} ({Pct(model.OnControlExternal, total)} от всех обращений).";
                return text;
            }

            return
                $"Закрыто — {model.ClosedTotal} ({Pct(model.ClosedTotal, total)}): обращения нет в последней выгрузке. " +
                $"На контроле исполнения — {model.OnControlTotal} ({Pct(model.OnControlTotal, total)}): обращение присутствует в последней выгрузке.";
        }

        private static Plot NewPlot(string title, ReportTheme t, int height = Height)
        {
            var brand = ColorTranslator.FromHtml(t.Brand);
            var plt = new Plot(Width, height);
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

        private static ChartImage ToImage(string title, Plot plt, string description = null) =>
            new ChartImage
            {
                Title = title,
                Description = description,
                Png = plt.GetImageBytes(lowQuality: false, scale: 1)
            };

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "—";
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }
    }
}

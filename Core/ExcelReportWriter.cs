using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using ObrasReport.Models;

namespace ObrasReport.Core
{
    /// <summary>Запись готовой модели отчёта в оформленный .xlsx.</summary>
    public static class ExcelReportWriter
    {
        public static void Write(ReportModel model, string outputPath, IList<ChartImage> charts = null,
            ReportTheme theme = null)
        {
            var t = theme ?? ReportTheme.Get("Синяя");
            using (var wb = new XLWorkbook())
            {
                WriteTable(wb, model, t);
                WriteStats(wb, model, t);
                string cat = string.IsNullOrWhiteSpace(model.CategoryLabel)
                    ? (model.Layout == LayoutType.Repairs ? "По ремонту" : "Не по ремонту")
                    : model.CategoryLabel;
                string periods = "Категория: " + cat + ". Периоды: " +
                                 string.Join(", ", model.Snapshots.Select(s => s.Label));
                if (charts != null && charts.Count > 0)
                    ChartSheetHelper.Write(wb,
                        "Графики и диаграммы — " + cat,
                        periods,
                        charts,
                        "ObrChart_",
                        t);
                wb.SaveAs(outputPath);
            }
        }

        private static void WriteTable(XLWorkbook wb, ReportModel model, ReportTheme t)
        {
            var ws = wb.AddWorksheet("Сводная таблица");
            bool repairs = model.Layout == LayoutType.Repairs;

            string catPart = string.IsNullOrWhiteSpace(model.CategoryLabel)
                ? (repairs ? "по ремонтам" : "не по ремонту")
                : model.CategoryLabel.ToLowerInvariant();
            string title = "Сводная таблица обработки обращений — " + catPart;
            ws.Cell(1, 1).Value = title;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 13;
            ws.Cell(1, 1).Style.Font.FontColor = t.TitleColorXL;

            string period = "Период: выгрузки " + string.Join(", ", model.Snapshots.Select(s => s.Label)) + ". " +
                            (repairs ? "Признак обработки — изменение состояния обращения." : "Признак обработки — снятие обращения с выгрузки.");
            ws.Cell(2, 1).Value = period;
            ws.Cell(2, 1).Style.Font.Italic = true;
            ws.Cell(2, 1).Style.Font.FontSize = 9;
            ws.Cell(2, 1).Style.Font.FontColor = t.SubtitleXL;

            if (!string.IsNullOrWhiteSpace(model.Description))
            {
                ws.Cell(3, 1).Value = "Описание: " + model.Description;
                ws.Cell(3, 1).Style.Font.FontSize = 10;
                ws.Cell(3, 1).Style.Font.FontColor = XLColor.FromHtml("#333333");
                ws.Cell(3, 1).Style.Alignment.WrapText = false;
            }

            // --- заголовки ---
            var headers = new List<string> { "№ п/п", "№ обращения", "Ответственный" };
            if (repairs)
            {
                headers.Add("Филиал");
                headers.Add("Классификатор");
                headers.Add("Критичность");
                headers.Add("Дней в сост.");
            }
            else
            {
                headers.Add("Объект (клиент)");
                if (model.HasService) headers.Add("Услуга");
            }
            foreach (var s in model.Snapshots)
                headers.Add((repairs ? "Состояние " : "Статус ") + s.Label);
            headers.Add("Итог");
            headers.Add("Комментарий");

            int hr = 4;
            for (int c = 0; c < headers.Count; c++)
            {
                var cell = ws.Cell(hr, c + 1);
                cell.Value = headers[c];
                cell.Style.Fill.BackgroundColor = t.HeaderFillXL;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Font.FontSize = 10;
                cell.Style.Alignment.WrapText = true;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            int r = hr;
            foreach (var row in model.Rows)
            {
                r++;
                int c = 1;
                ws.Cell(r, c++).Value = row.Index;
                ws.Cell(r, c++).Value = row.Number;
                ws.Cell(r, c++).Value = row.Responsible;
                if (repairs)
                {
                    ws.Cell(r, c++).Value = row.ObjectName;
                    ws.Cell(r, c++).Value = row.Classifier;
                    ws.Cell(r, c++).Value = row.Severity;
                    ws.Cell(r, c++).Value = row.Days;
                }
                else
                {
                    ws.Cell(r, c++).Value = row.ObjectName;
                    if (model.HasService) ws.Cell(r, c++).Value = row.Service;
                }
                foreach (var st in row.StatusByDate)
                    ws.Cell(r, c++).Value = st;

                var itog = ws.Cell(r, c++);
                itog.Value = row.Itog;
                itog.Style.Font.Bold = true;
                itog.Style.Font.FontSize = 9;
                if (row.Processed)
                {
                    itog.Style.Fill.BackgroundColor = t.GreenFillXL;
                    itog.Style.Font.FontColor = t.GreenTextXL;
                }
                else
                {
                    itog.Style.Fill.BackgroundColor = t.AmberFillXL;
                    itog.Style.Font.FontColor = t.AmberTextXL;
                }

                ws.Cell(r, c++).Value = row.Comment;
            }

            var used = ws.Range(hr, 1, r, headers.Count);
            used.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            used.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            used.Style.Border.OutsideBorderColor = t.BorderXL;
            used.Style.Border.InsideBorderColor = t.BorderXL;

            var body = ws.Range(hr + 1, 1, r, headers.Count);
            body.Style.Font.FontSize = 9;
            body.Style.Alignment.WrapText = true;
            body.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

            // ширины
            ws.Column(1).Width = 6;
            ws.Column(2).Width = 15;
            ws.Column(3).Width = 24;
            int col0 = 4;
            if (repairs)
            {
                ws.Column(col0++).Width = 14;
                ws.Column(col0++).Width = 18;
                ws.Column(col0++).Width = 11;
                ws.Column(col0++).Width = 10;
            }
            else
            {
                ws.Column(col0++).Width = 18;
                if (model.HasService) ws.Column(col0++).Width = 24;
            }
            for (int i = 0; i < model.Snapshots.Count; i++) ws.Column(col0++).Width = 19;
            ws.Column(col0++).Width = 20;
            ws.Column(col0).Width = 60;

            ws.SheetView.FreezeRows(hr);
            ws.Range(hr, 1, r, headers.Count).SetAutoFilter();
        }

        private static void WriteStats(XLWorkbook wb, ReportModel model, ReportTheme t)
        {
            var ws = wb.AddWorksheet("Статистика");
            bool repairs = model.Layout == LayoutType.Repairs;

            ws.Cell(1, 1).Value = "Общая статистика и динамика";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 13;
            ws.Cell(1, 1).Style.Font.FontColor = t.TitleColorXL;

            int r0 = 3;
            ws.Cell(r0, 1).Value = "Показатель";
            for (int i = 0; i < model.DateStats.Count; i++)
                ws.Cell(r0, 2 + i).Value = model.DateStats[i].Label;

            var statRows = new List<Tuple<string, Func<DateStat, object>>>
            {
                Tuple.Create<string, Func<DateStat, object>>("Всего обращений", d => d.TotalUnique),
            };
            if (repairs)
            {
                statRows.Add(Tuple.Create<string, Func<DateStat, object>>("— Чёрная критичность", d => d.Black));
                statRows.Add(Tuple.Create<string, Func<DateStat, object>>("— Красная критичность", d => d.Red));
                statRows.Add(Tuple.Create<string, Func<DateStat, object>>("— Жёлтая критичность", d => d.Yellow));
            }

            for (int i = 0; i < statRows.Count; i++)
            {
                int rr = r0 + 1 + i;
                ws.Cell(rr, 1).Value = statRows[i].Item1;
                for (int j = 0; j < model.DateStats.Count; j++)
                {
                    var val = statRows[i].Item2(model.DateStats[j]);
                    ws.Cell(rr, 2 + j).Value = Convert.ToInt32(val);
                    ws.Cell(rr, 2 + j).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
            }

            var tableRange = ws.Range(r0, 1, r0 + statRows.Count, 1 + model.DateStats.Count);
            tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            tableRange.Style.Border.OutsideBorderColor = t.BorderXL;
            tableRange.Style.Border.InsideBorderColor = t.BorderXL;
            var head = ws.Range(r0, 1, r0, 1 + model.DateStats.Count);
            head.Style.Fill.BackgroundColor = t.HeaderFillXL;
            head.Style.Font.Bold = true;
            head.Style.Font.FontColor = XLColor.White;
            head.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var lines = new List<Tuple<string, bool>>();
            void Head(string txt) => lines.Add(Tuple.Create(txt, true));
            void Line(string txt) => lines.Add(Tuple.Create(txt, false));

            Line("");
            Head("Итоги за период:");
            if (repairs)
                Line($"• Обработано — состояние изменилось: {model.ProcessedTotal}");
            Line($"• Закрыто — отсутствует в последней выгрузке: {model.ClosedTotal}");
            Line($"• На контроле исполнения — присутствует в последней выгрузке: {model.OnControlTotal}");
            if (repairs)
                Line($"   – из них в состояниях, движение которых обеспечивают внешние стороны: {model.OnControlExternal}");
            Line("");
            Head("Динамика между выгрузками:");
            for (int i = 0; i < model.Snapshots.Count - 1; i++)
            {
                string a = model.Snapshots[i].Label, b = model.Snapshots[i + 1].Label;
                Line($"• {a} → {b}: закрытые {model.LeftCounts[i]}, поступило новых {model.NewCounts[i]}" +
                     (repairs ? $", изменили состояние {model.ChangedCounts[i]}" : ""));
            }
            Line($"• Всего закрытых: {model.LeftCounts.Sum()}; всего новых: {model.NewCounts.Sum()}" +
                 (repairs ? $"; всего изменений состояния: {model.ChangedCounts.Sum()}" : ""));
            if (repairs && model.DateStats.Count > 0)
            {
                var crit = model.DateStats.Select(d => (d.Black + d.Red).ToString());
                Line($"• Критичные (Чёрная+Красная): {string.Join(" → ", crit)}");
            }

            Line("");
            Head("Сравнение с предыдущим периодом:");
            Line(ReportChartRenderer.BuildPeriodComparisonText(model));

            if (!string.IsNullOrWhiteSpace(model.Description))
            {
                Line("");
                Head("Описание отчёта:");
                Line(model.Description);
            }

            Line("");
            Line("Дата формирования: " + DateTime.Now.ToString("dd.MM.yyyy"));

            int rr2 = r0 + statRows.Count + 3;
            foreach (var ln in lines)
            {
                var cell = ws.Cell(rr2++, 1);
                cell.Value = ln.Item1;
                if (ln.Item2)
                {
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontSize = 11;
                    cell.Style.Font.FontColor = t.TitleColorXL;
                }
                else cell.Style.Font.FontSize = 10;
            }

            ws.Column(1).Width = 70;
            for (int i = 0; i < model.DateStats.Count; i++) ws.Column(2 + i).Width = 12;
        }
    }
}

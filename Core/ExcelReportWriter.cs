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
        private static readonly XLColor HeaderFill = XLColor.FromHtml("#1F4E78");
        private static readonly XLColor TitleColor = XLColor.FromHtml("#1F4E78");
        private static readonly XLColor GreenFill = XLColor.FromHtml("#E2EFDA");
        private static readonly XLColor AmberFill = XLColor.FromHtml("#FFF2CC");
        private static readonly XLColor GreenText = XLColor.FromHtml("#375623");
        private static readonly XLColor AmberText = XLColor.FromHtml("#7F6000");

        public static void Write(ReportModel model, string outputPath, IList<ChartImage> charts = null)
        {
            using (var wb = new XLWorkbook())
            {
                WriteTable(wb, model);
                WriteStats(wb, model);
                string cat = string.IsNullOrWhiteSpace(model.CategoryLabel)
                    ? (model.Layout == LayoutType.Repairs ? "По ремонту" : "Не по ремонту")
                    : model.CategoryLabel;
                string periods = "Категория: " + cat + ". Периоды: " +
                                 string.Join(", ", model.Snapshots.Select(s => s.Label));
                ChartSheetHelper.Write(wb,
                    "Графики и диаграммы — " + cat,
                    periods,
                    charts,
                    "ObrChart_");
                wb.SaveAs(outputPath);
            }
        }

        private static void WriteTable(XLWorkbook wb, ReportModel model)
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
            ws.Cell(1, 1).Style.Font.FontColor = TitleColor;

            string period = "Период: выгрузки " + string.Join(", ", model.Snapshots.Select(s => s.Label)) + ". " +
                            (repairs ? "Признак обработки — изменение состояния обращения." : "Признак обработки — снятие обращения с выгрузки.");
            ws.Cell(2, 1).Value = period;
            ws.Cell(2, 1).Style.Font.Italic = true;
            ws.Cell(2, 1).Style.Font.FontSize = 9;
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#595959");

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
                cell.Style.Fill.BackgroundColor = HeaderFill;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Font.FontSize = 10;
                cell.Style.Alignment.WrapText = true;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            int itogCol = headers.Count - 1;   // 1-based позже
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
                    itog.Style.Fill.BackgroundColor = GreenFill;
                    itog.Style.Font.FontColor = GreenText;
                }
                else
                {
                    itog.Style.Fill.BackgroundColor = AmberFill;
                    itog.Style.Font.FontColor = AmberText;
                }

                ws.Cell(r, c++).Value = row.Comment;
            }

            var used = ws.Range(hr, 1, r, headers.Count);
            used.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            used.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            used.Style.Border.OutsideBorderColor = XLColor.FromHtml("#B0B0B0");
            used.Style.Border.InsideBorderColor = XLColor.FromHtml("#B0B0B0");

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
                ws.Column(col0++).Width = 14; // филиал
                ws.Column(col0++).Width = 18; // классификатор
                ws.Column(col0++).Width = 11; // критичность
                ws.Column(col0++).Width = 10; // дней
            }
            else
            {
                ws.Column(col0++).Width = 18; // объект
                if (model.HasService) ws.Column(col0++).Width = 24; // услуга
            }
            for (int i = 0; i < model.Snapshots.Count; i++) ws.Column(col0++).Width = 19;
            ws.Column(col0++).Width = 20; // итог
            ws.Column(col0).Width = 60;   // комментарий

            ws.SheetView.FreezeRows(hr);
            ws.Range(hr, 1, r, headers.Count).SetAutoFilter();
        }

        private static void WriteStats(XLWorkbook wb, ReportModel model)
        {
            var ws = wb.AddWorksheet("Статистика");
            bool repairs = model.Layout == LayoutType.Repairs;

            ws.Cell(1, 1).Value = "Общая статистика и динамика";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 13;
            ws.Cell(1, 1).Style.Font.FontColor = TitleColor;

            int r0 = 3;
            // шапка таблицы по датам
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
            tableRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#B0B0B0");
            tableRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#B0B0B0");
            var head = ws.Range(r0, 1, r0, 1 + model.DateStats.Count);
            head.Style.Fill.BackgroundColor = HeaderFill;
            head.Style.Font.Bold = true;
            head.Style.Font.FontColor = XLColor.White;
            head.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // блок динамики
            var lines = new List<Tuple<string, bool>>();
            void Head(string t) => lines.Add(Tuple.Create(t, true));
            void Line(string t) => lines.Add(Tuple.Create(t, false));

            Line("");
            Head("Итоги за период:");
            Line($"• Обработано (итог): {model.ProcessedTotal}");
            Line($"• На контроле исполнения (итог): {model.OnControlTotal}");
            if (repairs)
                Line($"   – из них в состояниях, движение которых обеспечивают внешние стороны: {model.OnControlExternal}");
            Line("");
            Head("Динамика между выгрузками:");
            for (int i = 0; i < model.Snapshots.Count - 1; i++)
            {
                string a = model.Snapshots[i].Label, b = model.Snapshots[i + 1].Label;
                Line($"• {a} → {b}: снято с контроля {model.LeftCounts[i]}, поступило новых {model.NewCounts[i]}" +
                     (repairs ? $", изменили состояние {model.ChangedCounts[i]}" : ""));
            }
            Line($"• Всего снято с контроля: {model.LeftCounts.Sum()}; всего новых: {model.NewCounts.Sum()}" +
                 (repairs ? $"; всего изменений состояния: {model.ChangedCounts.Sum()}" : ""));
            if (repairs && model.DateStats.Count > 0)
            {
                var crit = model.DateStats.Select(d => (d.Black + d.Red).ToString());
                Line($"• Критичные (Чёрная+Красная): {string.Join(" → ", crit)}");
            }

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
                    cell.Style.Font.FontColor = TitleColor;
                }
                else cell.Style.Font.FontSize = 10;
            }

            ws.Column(1).Width = 70;
            for (int i = 0; i < model.DateStats.Count; i++) ws.Column(2 + i).Width = 12;
        }
    }
}

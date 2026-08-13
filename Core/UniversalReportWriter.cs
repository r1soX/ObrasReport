using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using ObrasReport.Models;

namespace ObrasReport.Core
{
    /// <summary>Запись универсального (нейтрального) отчёта в оформленный .xlsx.</summary>
    public static class UniversalReportWriter
    {
        public static void Write(UniversalReportModel model, string outputPath, ReportTheme theme = null)
        {
            var t = theme ?? ReportTheme.Get("Синяя");
            using (var wb = new XLWorkbook())
            {
                WriteTable(wb, model, t);
                WriteStats(wb, model, t);
                wb.SaveAs(outputPath);
            }
        }

        private static void WriteTable(XLWorkbook wb, UniversalReportModel model, ReportTheme t)
        {
            var ws = wb.AddWorksheet("Сравнение");
            bool tracked = !string.IsNullOrEmpty(model.TrackedHeader);

            ws.Cell(1, 1).Value = string.IsNullOrWhiteSpace(model.Title)
                ? "Универсальное сравнение таблиц" : model.Title;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 13;
            ws.Cell(1, 1).Style.Font.FontColor = t.TitleColorXL;

            ws.Cell(2, 1).Value = "Сравнение по ключу «" + model.KeyHeader + "». Даты: " +
                                  string.Join(", ", model.DateLabels) + ".";
            ws.Cell(2, 1).Style.Font.Italic = true;
            ws.Cell(2, 1).Style.Font.FontSize = 9;
            ws.Cell(2, 1).Style.Font.FontColor = t.SubtitleXL;

            if (!string.IsNullOrWhiteSpace(model.Description))
            {
                ws.Cell(3, 1).Value = "Описание: " + model.Description;
                ws.Cell(3, 1).Style.Font.FontSize = 10;
                ws.Cell(3, 1).Style.Font.FontColor = XLColor.FromHtml("#333333");
            }

            var headers = new List<string> { "№ п/п", model.KeyHeader };
            headers.AddRange(model.DisplayHeaders);
            if (tracked)
                foreach (var d in model.DateLabels) headers.Add(model.TrackedHeader + " " + d);
            headers.Add("Итог");

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
                ws.Cell(r, c++).Value = row.Key;
                foreach (var dv in row.DisplayValues) ws.Cell(r, c++).Value = dv;
                if (tracked) foreach (var tv in row.TrackedByDate) ws.Cell(r, c++).Value = tv;

                var itog = ws.Cell(r, c++);
                itog.Value = row.Itog;
                itog.Style.Font.Bold = true;
                itog.Style.Font.FontSize = 9;
                switch (row.Kind)
                {
                    case "added": itog.Style.Fill.BackgroundColor = t.BlueFillXL; itog.Style.Font.FontColor = t.TitleColorXL; break;
                    case "removed": itog.Style.Fill.BackgroundColor = t.GreyFillXL; itog.Style.Font.FontColor = t.SubtitleXL; break;
                    case "changed": itog.Style.Fill.BackgroundColor = t.AmberFillXL; itog.Style.Font.FontColor = t.AmberTextXL; break;
                    default: itog.Style.Fill.BackgroundColor = t.GreenFillXL; itog.Style.Font.FontColor = t.GreenTextXL; break;
                }
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

            ws.Column(1).Width = 6;
            ws.Column(2).Width = 18;
            for (int i = 0; i < model.DisplayHeaders.Count; i++) ws.Column(3 + i).Width = 20;
            int after = 3 + model.DisplayHeaders.Count;
            if (tracked) for (int i = 0; i < model.DateLabels.Count; i++) ws.Column(after++).Width = 18;
            ws.Column(after).Width = 16;

            ws.SheetView.FreezeRows(hr);
            ws.Range(hr, 1, r, headers.Count).SetAutoFilter();
        }

        private static void WriteStats(XLWorkbook wb, UniversalReportModel model, ReportTheme t)
        {
            var ws = wb.AddWorksheet("Статистика");
            ws.Cell(1, 1).Value = "Итоги сравнения";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 13;
            ws.Cell(1, 1).Style.Font.FontColor = t.TitleColorXL;

            var lines = new List<Tuple<string, bool>>
            {
                Tuple.Create("", false),
                Tuple.Create($"Всего уникальных значений ключа: {model.Rows.Count}", false),
                Tuple.Create($"• Добавлено: {model.Added}", false),
                Tuple.Create($"• Удалено: {model.Removed}", false),
                Tuple.Create($"• Изменено: {model.Changed}", false),
                Tuple.Create($"• Без изменений: {model.Same}", false),
                Tuple.Create("", false),
                Tuple.Create($"Ключевой столбец: {model.KeyHeader}", false),
            };
            if (!string.IsNullOrEmpty(model.TrackedHeader))
                lines.Add(Tuple.Create($"Отслеживаемый столбец: {model.TrackedHeader}", false));
            lines.Add(Tuple.Create($"Сравниваемые даты: {string.Join(", ", model.DateLabels)}", false));
            if (!string.IsNullOrWhiteSpace(model.Description))
            {
                lines.Add(Tuple.Create("", false));
                lines.Add(Tuple.Create("Описание отчёта:", true));
                lines.Add(Tuple.Create(model.Description, false));
            }
            lines.Add(Tuple.Create("", false));
            lines.Add(Tuple.Create("Дата формирования: " + DateTime.Now.ToString("dd.MM.yyyy"), false));

            int rr = 3;
            foreach (var ln in lines)
            {
                var cell = ws.Cell(rr++, 1);
                cell.Value = ln.Item1;
                cell.Style.Font.FontSize = ln.Item2 ? 11 : 10;
                cell.Style.Font.Bold = ln.Item2;
                if (ln.Item2) cell.Style.Font.FontColor = t.TitleColorXL;
            }
            ws.Column(1).Width = 70;
        }
    }
}
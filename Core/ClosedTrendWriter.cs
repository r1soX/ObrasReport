using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using ObrasReport.Models;

namespace ObrasReport.Core
{
    /// <summary>Запись отчёта динамики закрытых обращений/нарядов.</summary>
    public static class ClosedTrendWriter
    {
        private static readonly XLColor HeaderFill = XLColor.FromHtml("#1F4E78");
        private static readonly XLColor TitleColor = XLColor.FromHtml("#1F4E78");
        private static readonly XLColor GreenText = XLColor.FromHtml("#2E7D32");
        private static readonly XLColor RedText = XLColor.FromHtml("#C62828");
        private static readonly XLColor TotalFill = XLColor.FromHtml("#EAF1F8");
        private static readonly XLColor Border = XLColor.FromHtml("#B0B0B0");

        public static void Write(TrendReportModel model, string outputPath, IList<ChartImage> charts = null)
        {
            using (var wb = new XLWorkbook())
            {
                WriteTable(wb, model);
                WriteStats(wb, model);
                WriteCharts(wb, model, charts);
                wb.SaveAs(outputPath);
            }
        }

        private static void WriteCharts(XLWorkbook wb, TrendReportModel model, IList<ChartImage> charts)
        {
            var ws = wb.AddWorksheet("Графики");
            ws.Cell(1, 1).Value = string.IsNullOrWhiteSpace(model.Title)
                ? "Графики прогресса закрытия"
                : "Графики: " + model.Title;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 13;
            ws.Cell(1, 1).Style.Font.FontColor = TitleColor;

            ws.Cell(2, 1).Value = "Периоды: " + string.Join(", ", model.DateLabels);
            ws.Cell(2, 1).Style.Font.Italic = true;
            ws.Cell(2, 1).Style.Font.FontSize = 9;
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#595959");

            if (charts == null || charts.Count == 0)
            {
                ws.Cell(4, 1).Value = "Графики не сформированы.";
                ws.Cell(4, 1).Style.Font.FontColor = XLColor.FromHtml("#C62828");
                ws.Column(1).Width = 70;
                return;
            }

            // Вставляем PNG друг под другом. ClosedXML привязывает картинку к ячейке.
            int anchorRow = 4;
            const int chartHeightRows = 22; // ~ высота картинки в строках листа
            int picIndex = 0;
            foreach (var chart in charts)
            {
                if (chart?.Png == null || chart.Png.Length == 0) continue;
                ws.Cell(anchorRow, 1).Value = chart.Title ?? ("График " + (picIndex + 1));
                ws.Cell(anchorRow, 1).Style.Font.Bold = true;
                ws.Cell(anchorRow, 1).Style.Font.FontSize = 11;
                ws.Cell(anchorRow, 1).Style.Font.FontColor = TitleColor;

                using (var ms = new MemoryStream(chart.Png))
                {
                    var pic = ws.AddPicture(ms)
                        .MoveTo(ws.Cell(anchorRow + 1, 1))
                        .WithSize(820, 380);
                    pic.Name = "TrendChart_" + picIndex;
                }

                anchorRow += chartHeightRows;
                picIndex++;
            }

            ws.Column(1).Width = 90;
        }

        private static void WriteTable(XLWorkbook wb, TrendReportModel model)
        {
            var ws = wb.AddWorksheet("Динамика");
            int n = model.DateLabels.Count;

            ws.Cell(1, 1).Value = string.IsNullOrWhiteSpace(model.Title)
                ? "Динамика решённых обращений и нарядов" : model.Title;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 13;
            ws.Cell(1, 1).Style.Font.FontColor = TitleColor;

            ws.Cell(2, 1).Value = "Периоды: " + string.Join(", ", model.DateLabels) + ". Показаны закрытые (решённые) обращения и наряды по ответственным.";
            ws.Cell(2, 1).Style.Font.Italic = true;
            ws.Cell(2, 1).Style.Font.FontSize = 9;
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#595959");

            if (!string.IsNullOrWhiteSpace(model.Description))
            {
                ws.Cell(3, 1).Value = "Описание: " + model.Description;
                ws.Cell(3, 1).Style.Font.FontSize = 10;
            }

            int hr = 4;                 // строка группового заголовка
            int sub = hr + 1;           // строка периодов
            int data = sub + 1;

            // раскладка колонок
            int cNo = 1, cResp = 2;
            int obrStart = 3, obrEnd = 2 + n, dObr = 3 + n;
            int narStart = 4 + n, narEnd = 3 + 2 * n, dNar = 4 + 2 * n;
            int lastCol = dNar;

            ws.Cell(hr, cNo).Value = "№";
            ws.Cell(hr, cResp).Value = "Ответственный";
            ws.Range(hr, cNo, sub, cNo).Merge();
            ws.Range(hr, cResp, sub, cResp).Merge();

            ws.Cell(hr, obrStart).Value = "Решено обращений";
            ws.Range(hr, obrStart, hr, obrEnd).Merge();
            ws.Cell(hr, dObr).Value = "Тренд, обр.";
            ws.Range(hr, dObr, sub, dObr).Merge();

            ws.Cell(hr, narStart).Value = "Решено нарядов";
            ws.Range(hr, narStart, hr, narEnd).Merge();
            ws.Cell(hr, dNar).Value = "Тренд, наряды";
            ws.Range(hr, dNar, sub, dNar).Merge();

            for (int i = 0; i < n; i++)
            {
                ws.Cell(sub, obrStart + i).Value = model.DateLabels[i];
                ws.Cell(sub, narStart + i).Value = model.DateLabels[i];
            }

            var head = ws.Range(hr, 1, sub, lastCol);
            head.Style.Fill.BackgroundColor = HeaderFill;
            head.Style.Font.Bold = true;
            head.Style.Font.FontColor = XLColor.White;
            head.Style.Font.FontSize = 10;
            head.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            head.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            head.Style.Alignment.WrapText = true;

            int r = data - 1;
            foreach (var row in model.Rows)
            {
                r++;
                ws.Cell(r, cNo).Value = row.Index;
                ws.Cell(r, cResp).Value = row.Responsible;
                for (int i = 0; i < n; i++)
                {
                    ws.Cell(r, obrStart + i).Value = row.ObrClosed[i];
                    ws.Cell(r, narStart + i).Value = row.NarClosed[i];
                }
                WriteDelta(ws.Cell(r, dObr), row.ObrDelta);
                WriteDelta(ws.Cell(r, dNar), row.NarDelta);
            }

            // строка ИТОГО
            r++;
            ws.Cell(r, cResp).Value = "ИТОГО";
            for (int i = 0; i < n; i++)
            {
                ws.Cell(r, obrStart + i).Value = model.TotalObrClosed[i];
                ws.Cell(r, narStart + i).Value = model.TotalNarClosed[i];
            }
            WriteDelta(ws.Cell(r, dObr), model.TotalObrDelta);
            WriteDelta(ws.Cell(r, dNar), model.TotalNarDelta);
            var totalRange = ws.Range(r, 1, r, lastCol);
            totalRange.Style.Fill.BackgroundColor = TotalFill;
            totalRange.Style.Font.Bold = true;

            var used = ws.Range(hr, 1, r, lastCol);
            used.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            used.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            used.Style.Border.OutsideBorderColor = Border;
            used.Style.Border.InsideBorderColor = Border;
            ws.Range(data, cNo, r, cNo).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(data, obrStart, r, lastCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Column(cNo).Width = 6;
            ws.Column(cResp).Width = 30;
            for (int c = obrStart; c <= lastCol; c++) ws.Column(c).Width = c == dObr || c == dNar ? 12 : 11;

            ws.SheetView.FreezeRows(sub);
        }

        private static void WriteDelta(IXLCell cell, int delta)
        {
            cell.Value = delta > 0 ? "+" + delta : delta.ToString();
            cell.Style.Font.Bold = true;
            if (delta > 0) cell.Style.Font.FontColor = GreenText;
            else if (delta < 0) cell.Style.Font.FontColor = RedText;
            else cell.Style.Font.FontColor = XLColor.FromHtml("#777777");
        }

        private static void WriteStats(XLWorkbook wb, TrendReportModel model)
        {
            var ws = wb.AddWorksheet("Статистика");
            ws.Cell(1, 1).Value = "Итоги динамики";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 13;
            ws.Cell(1, 1).Style.Font.FontColor = TitleColor;

            var lines = new List<string> { "" };
            for (int i = 0; i < model.DateLabels.Count; i++)
                lines.Add($"{model.DateLabels[i]}: решено обращений {model.TotalObrClosed[i]}, решено нарядов {model.TotalNarClosed[i]}");
            lines.Add("");
            lines.Add($"Тренд обращений (последний − первый): {(model.TotalObrDelta > 0 ? "+" : "")}{model.TotalObrDelta}");
            lines.Add($"Тренд нарядов (последний − первый): {(model.TotalNarDelta > 0 ? "+" : "")}{model.TotalNarDelta}");
            lines.Add($"Ответственных в отчёте: {model.Rows.Count}");
            if (!string.IsNullOrWhiteSpace(model.Description))
            {
                lines.Add("");
                lines.Add("Описание: " + model.Description);
            }
            lines.Add("");
            lines.Add("Дата формирования: " + DateTime.Now.ToString("dd.MM.yyyy"));

            int r = 3;
            foreach (var ln in lines) { ws.Cell(r++, 1).Value = ln; ws.Cell(r - 1, 1).Style.Font.FontSize = 10; }
            ws.Column(1).Width = 70;
        }
    }
}

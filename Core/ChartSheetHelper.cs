using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;

namespace ObrasReport.Core
{
    /// <summary>Общая запись листа «Графики» с PNG.</summary>
    public static class ChartSheetHelper
    {
        public static void Write(XLWorkbook wb, string heading, string subtitle, IList<ChartImage> charts,
            string picPrefix = "Chart_", ReportTheme theme = null)
        {
            var t = theme ?? ReportTheme.Get("Синяя");
            var ws = wb.AddWorksheet("Графики");
            ws.Cell(1, 1).Value = heading ?? "Графики и диаграммы";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 13;
            ws.Cell(1, 1).Style.Font.FontColor = t.TitleColorXL;

            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                ws.Cell(2, 1).Value = subtitle;
                ws.Cell(2, 1).Style.Font.Italic = true;
                ws.Cell(2, 1).Style.Font.FontSize = 9;
                ws.Cell(2, 1).Style.Font.FontColor = t.SubtitleXL;
            }

            if (charts == null || charts.Count == 0)
            {
                ws.Cell(4, 1).Value = "Графики и диаграммы не сформированы.";
                ws.Cell(4, 1).Style.Font.FontColor = XLColor.FromHtml(t.RedText);
                ws.Column(1).Width = 70;
                return;
            }

            int anchorRow = 4;
            const int chartHeightRows = 28;
            int picIndex = 0;
            foreach (var chart in charts)
            {
                if (chart?.Png == null || chart.Png.Length == 0) continue;
                ws.Cell(anchorRow, 1).Value = chart.Title ?? ("График " + (picIndex + 1));
                ws.Cell(anchorRow, 1).Style.Font.Bold = true;
                ws.Cell(anchorRow, 1).Style.Font.FontSize = 11;
                ws.Cell(anchorRow, 1).Style.Font.FontColor = t.TitleColorXL;

                using (var ms = new MemoryStream(chart.Png))
                {
                    var pic = ws.AddPicture(ms)
                        .MoveTo(ws.Cell(anchorRow + 1, 1))
                        .WithSize(900, 480);
                    pic.Name = picPrefix + picIndex;
                }

                anchorRow += chartHeightRows;
                picIndex++;
            }

            ws.Column(1).Width = 90;
        }
    }
}
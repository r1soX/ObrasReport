using System;
using System.Collections.Generic;
using System.Drawing;
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
            int picIndex = 0;
            foreach (var chart in charts)
            {
                if (chart?.Png == null || chart.Png.Length == 0) continue;
                ws.Cell(anchorRow, 1).Value = chart.Title ?? ("График " + (picIndex + 1));
                ws.Cell(anchorRow, 1).Style.Font.Bold = true;
                ws.Cell(anchorRow, 1).Style.Font.FontSize = 11;
                ws.Cell(anchorRow, 1).Style.Font.FontColor = t.TitleColorXL;

                int pictureRow = anchorRow + 1;
                if (!string.IsNullOrWhiteSpace(chart.Description))
                {
                    var descriptionCell = ws.Cell(anchorRow + 1, 1);
                    descriptionCell.Value = chart.Description;
                    descriptionCell.Style.Font.FontSize = 9;
                    descriptionCell.Style.Font.FontColor = t.SubtitleXL;
                    descriptionCell.Style.Alignment.WrapText = true;
                    ws.Row(anchorRow + 1).Height = 42;
                    pictureRow = anchorRow + 2;
                }

                using (var ms = new MemoryStream(chart.Png))
                {
                    int pictureWidth = 900;
                    int pictureHeight;
                    using (var source = Image.FromStream(ms, false, false))
                    {
                        pictureHeight = Math.Max(480,
                            (int)Math.Round(pictureWidth * source.Height / (double)source.Width));
                    }
                    ms.Position = 0;
                    var pic = ws.AddPicture(ms)
                        .MoveTo(ws.Cell(pictureRow, 1))
                        .WithSize(pictureWidth, pictureHeight);
                    pic.Name = picPrefix + picIndex;

                    // Около 17 пикселей на стандартную строку Excel. Высота зависит
                    // от числа ответственных в полных рейтингах.
                    anchorRow += Math.Max(28,
                        pictureRow - anchorRow + (int)Math.Ceiling(pictureHeight / 17.0) + 2);
                }
                picIndex++;
            }

            ws.Column(1).Width = 90;
        }
    }
}

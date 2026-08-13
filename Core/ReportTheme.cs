using System.Collections.Generic;
using ClosedXML.Excel;

namespace ObrasReport.Core
{
    /// <summary>Цветовая тема отчёта (Excel + графики).</summary>
    public class ReportTheme
    {
        public string Name { get; set; }

        // основные цвета (меняются по теме)
        public string HeaderFill { get; set; }   // фон шапки таблицы
        public string TitleColor { get; set; }   // заголовки
        public string Brand { get; set; }        // брендовый цвет (графики, акценты)
        public string Accent { get; set; }       // вторичный акцент (графики)
        public string TotalFill { get; set; }    // фон строки ИТОГО

        // неизменные цвета (статусы, ошибки)
        public string GreenFill { get; set; } = "#E2EFDA";
        public string AmberFill { get; set; } = "#FFF2CC";
        public string GreenText { get; set; } = "#375623";
        public string AmberText { get; set; } = "#7F6000";
        public string GreenBright { get; set; } = "#2E7D32";
        public string RedText { get; set; } = "#C62828";
        public string AmberBright { get; set; } = "#F9A825";
        public string Blackish { get; set; } = "#333333";
        public string Border { get; set; } = "#B0B0B0";
        public string BlueFill { get; set; } = "#DDEBF7";
        public string GreyFill { get; set; } = "#EDEDED";
        public string Subtitle { get; set; } = "#595959";

        // convenience accessors for ClosedXML
        public XLColor HeaderFillXL => XLColor.FromHtml(HeaderFill);
        public XLColor TitleColorXL => XLColor.FromHtml(TitleColor);
        public XLColor GreenFillXL => XLColor.FromHtml(GreenFill);
        public XLColor AmberFillXL => XLColor.FromHtml(AmberFill);
        public XLColor GreenTextXL => XLColor.FromHtml(GreenText);
        public XLColor AmberTextXL => XLColor.FromHtml(AmberText);
        public XLColor TotalFillXL => XLColor.FromHtml(TotalFill);
        public XLColor BorderXL => XLColor.FromHtml(Border);
        public XLColor BlueFillXL => XLColor.FromHtml(BlueFill);
        public XLColor GreyFillXL => XLColor.FromHtml(GreyFill);
        public XLColor SubtitleXL => XLColor.FromHtml(Subtitle);

        private static readonly Dictionary<string, ReportTheme> Themes = new Dictionary<string, ReportTheme>
        {
            ["Синяя"] = new ReportTheme
            {
                Name = "Синяя",
                HeaderFill = "#1F4E78", TitleColor = "#1F4E78",
                Brand = "#1F4E78", Accent = "#5B8DB8",
                TotalFill = "#EAF1F8",
            },
            ["Зелёная"] = new ReportTheme
            {
                Name = "Зелёная",
                HeaderFill = "#2E7D32", TitleColor = "#2E7D32",
                Brand = "#2E7D32", Accent = "#66BB6A",
                TotalFill = "#E8F5E9",
            },
            ["Графит"] = new ReportTheme
            {
                Name = "Графит",
                HeaderFill = "#333333", TitleColor = "#333333",
                Brand = "#333333", Accent = "#78909C",
                TotalFill = "#F0F0F0",
            },
            ["Светлая"] = new ReportTheme
            {
                Name = "Светлая",
                HeaderFill = "#557A95", TitleColor = "#557A95",
                Brand = "#557A95", Accent = "#8DAA9D",
                TotalFill = "#EEF3F7",
            },
        };

        public static ReportTheme Get(string name)
        {
            if (!string.IsNullOrWhiteSpace(name) && Themes.TryGetValue(name.Trim(), out var t))
                return t;
            return Themes["Синяя"];
        }

        public static IReadOnlyList<string> Names => new List<string> { "Синяя", "Зелёная", "Графит", "Светлая" };
    }
}

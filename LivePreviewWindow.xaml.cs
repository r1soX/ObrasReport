using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ObrasReport.Core;
using ObrasReport.Models;

namespace ObrasReport
{
    /// <summary>
    /// Живой предпросмотр отчёта: таблица (DataGrid), графики и статистика.
    /// Пользователь выбирает тему и состав отчёта; результат читается
    /// из публичных свойств после закрытия окна (DialogResult == true).
    /// </summary>
    public partial class LivePreviewWindow : Window
    {
        private readonly ReportModel _model;
        private readonly List<ChartImage> _charts;
        private readonly string _subtitle;
        private readonly string _heading;

        private sealed class ChartToggleItem
        {
            public string Title;
            public ChartImage Source;
        }

        private sealed class ChartViewItem
        {
            public string Title;
            public ImageSource Image;
        }

        // ---- результаты выбора пользователя ----
        public bool ChartsEnabled => ShowChartsCb.IsChecked == true;
        public bool StatsEnabled => ShowStatsCb.IsChecked == true;
        public bool CommentsEnabled => ShowCommentsCb.IsChecked == true;
        public string SelectedTheme => (ThemeCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "Синяя";

        public LivePreviewWindow(ReportModel model, List<ChartImage> charts, string subtitle,
            string heading, string theme = "Синяя")
        {
            InitializeComponent();
            _model = model;
            _charts = charts ?? new List<ChartImage>();
            _subtitle = subtitle;
            _heading = heading;

            ThemeCombo.SelectedIndex = Math.Max(0, Array.IndexOf(new[] { "Синяя", "Зелёная", "Графит", "Светлая" }, theme));
            BuildChartToggles();
            Render();
        }

        private void BuildChartToggles()
        {
            ChartTogglePanel.Children.Clear();
            foreach (var c in _charts)
            {
                var cb = new CheckBox
                {
                    Content = c.Title,
                    IsChecked = true,
                    Margin = new Thickness(0, 0, 0, 6),
                    Tag = new ChartToggleItem { Title = c.Title, Source = c }
                };
                cb.Checked += (s, e) => Render();
                cb.Unchecked += (s, e) => Render();
                ChartTogglePanel.Children.Add(cb);
            }
            if (_charts.Count == 0)
            {
                ChartTogglePanel.Children.Add(new TextBlock
                {
                    Text = "Графики не сформированы",
                    Foreground = Brushes.Gray,
                    FontSize = 12
                });
            }
        }

        // ---------- рендер ----------
        private void Render()
        {
            RenderTable();
            RenderCharts();
            RenderStats();
            UpdateInfo();
        }

        private void RenderTable()
        {
            var headers = BuildHeaders();
            TablePreview.Columns.Clear();
            foreach (var h in headers)
            {
                TablePreview.Columns.Add(new DataGridTextColumn
                {
                    Header = h,
                    Binding = new System.Windows.Data.Binding(SafeKey(h)),
                    Width = new DataGridLength(h.Length > 25 ? 200 : 140)
                });
            }

            var items = new System.Collections.ObjectModel.ObservableCollection<Dictionary<string, object>>();
            foreach (var row in _model.Rows)
            {
                var dict = new Dictionary<string, object>();
                int idx = 0;
                AddCell(dict, headers[idx++], row.Index);
                AddCell(dict, headers[idx++], row.Number);
                AddCell(dict, headers[idx++], row.Responsible);

                if (_model.Layout == LayoutType.Repairs)
                {
                    AddCell(dict, headers[idx++], row.ObjectName);
                    AddCell(dict, headers[idx++], row.Classifier);
                    AddCell(dict, headers[idx++], row.Severity);
                    AddCell(dict, headers[idx++], row.Days);
                }
                else
                {
                    AddCell(dict, headers[idx++], row.ObjectName);
                    if (_model.HasService) AddCell(dict, headers[idx++], row.Service);
                }
                foreach (var st in row.StatusByDate) AddCell(dict, headers[idx++], st);
                AddCell(dict, headers[idx++], row.Itog);
                AddCell(dict, headers[idx++], row.Comment);
                items.Add(dict);
            }
            TablePreview.ItemsSource = items;
            ApplyTheme(SelectedTheme);
        }

        private static string SafeKey(string header) =>
            header.Replace(" ", "_").Replace("/", "_").Replace("№", "N").Replace("(", "").Replace(")", "");

        private static void AddCell(Dictionary<string, object> dict, string header, object value)
        {
            dict[SafeKey(header)] = value ?? "";
        }

        private List<string> BuildHeaders()
        {
            var headers = new List<string> { "№ п/п", "№ обращения", "Ответственный" };
            if (_model.Layout == LayoutType.Repairs)
            {
                headers.Add("Филиал");
                headers.Add("Классификатор");
                headers.Add("Критичность");
                headers.Add("Дней в сост.");
            }
            else
            {
                headers.Add("Объект (клиент)");
                if (_model.HasService) headers.Add("Услуга");
            }
            foreach (var s in _model.Snapshots)
                headers.Add((_model.Layout == LayoutType.Repairs ? "Состояние " : "Статус ") + s.Label);
            headers.Add("Итог");
            headers.Add("Комментарий");
            return headers;
        }

        private void RenderCharts()
        {
            var visible = _charts
                .Where(c => ChartTogglePanel.Children
                    .Cast<CheckBox>()
                    .Where(cb => cb.Tag is ChartToggleItem t && t.Source == c)
                    .Any(cb => cb.IsChecked == true))
                .ToList();

            ChartsPreview.ItemsSource = visible.Select(c => new ChartViewItem
            {
                Title = c.Title,
                Image = ToBitmap(c.Png)
            }).ToList();
        }

        private void RenderStats()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ОБЩАЯ СТАТИСТИКА И ДИНАМИКА");
            sb.AppendLine(new string('=', 46));
            sb.AppendLine();
            sb.AppendLine($"Всего обращений: {_model.Rows.Count}");
            sb.AppendLine($"Обработано (итог): {_model.ProcessedTotal}");
            sb.AppendLine($"На контроле исполнения (итог): {_model.OnControlTotal}");
            if (_model.Layout == LayoutType.Repairs)
                sb.AppendLine($"  из них во внешних состояниях: {_model.OnControlExternal}");
            sb.AppendLine();
            sb.AppendLine("ДИНАМИКА МЕЖДУ ВЫГРУЗКАМИ:");
            for (int i = 0; i < _model.Snapshots.Count - 1; i++)
            {
                string a = _model.Snapshots[i].Label, b = _model.Snapshots[i + 1].Label;
                sb.AppendLine($"  {a} → {b}: снято {_model.LeftCounts[i]}, новых {_model.NewCounts[i]}" +
                    (_model.Layout == LayoutType.Repairs ? $", изменили состояние {_model.ChangedCounts[i]}" : ""));
            }
            sb.AppendLine($"  Всего снято: {_model.LeftCounts.Sum()}; новых: {_model.NewCounts.Sum()}" +
                (_model.Layout == LayoutType.Repairs ? $"; изменений: {_model.ChangedCounts.Sum()}" : ""));
            if (!string.IsNullOrWhiteSpace(_model.Description))
            {
                sb.AppendLine();
                sb.AppendLine("ОПИСАНИЕ ОТЧЁТА:");
                sb.AppendLine(_model.Description);
            }
            StatsPreview.Text = sb.ToString();
        }

        private void UpdateInfo()
        {
            PreviewInfo.Text = $"Категория: {_heading}\n{_subtitle}\n\n" +
                               $"Обращений: {_model.Rows.Count} · Обработано: {_model.ProcessedTotal} · " +
                               $"На контроле: {_model.OnControlTotal}\nГрафиков: {_charts.Count}";
        }

        // ---------- тема ----------
        private void Theme_Changed(object sender, SelectionChangedEventArgs e)
        {
            ApplyTheme(SelectedTheme);
        }

        private void ApplyTheme(string theme)
        {
            Color header;
            switch ((theme ?? "").Trim())
            {
                case "Зелёная": header = Color.FromRgb(0x2E, 0x7D, 0x32); break;
                case "Графит": header = Color.FromRgb(0x33, 0x33, 0x33); break;
                case "Светлая": header = Color.FromRgb(0x55, 0x7A, 0x95); break;
                default: header = Color.FromRgb(0x1F, 0x4E, 0x78); break;
            }

            var brush = new SolidColorBrush(header);
            TablePreview.ColumnHeaderStyle = new Style(typeof(DataGridColumnHeader))
            {
                Setters =
                {
                    new Setter(Control.BackgroundProperty, brush),
                    new Setter(Control.ForegroundProperty, Brushes.White),
                    new Setter(Control.FontWeightProperty, FontWeights.Bold),
                    new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4))
                }
            };
        }

        private void Option_Changed(object sender, RoutedEventArgs e)
        {
            UpdateInfo();
        }

        // ---------- сохранение ----------
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static BitmapImage ToBitmap(byte[] png)
        {
            if (png == null || png.Length == 0) return null;
            var bi = new BitmapImage();
            using (var ms = new MemoryStream(png))
            {
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
            }
            bi.Freeze();
            return bi;
        }
    }
}
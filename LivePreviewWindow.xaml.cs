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
    /// Поддерживает три типа: Обращения (ReportModel), Универсальное (UniversalReportModel),
    /// Динамика (TrendReportModel).
    /// </summary>
    public partial class LivePreviewWindow : Window
    {
        private enum PreviewKind { Obrasheniya, Universal, Trend }

        private readonly PreviewKind _kind;
        private readonly ReportModel _obrModel;
        private readonly UniversalReportModel _univModel;
        private readonly TrendReportModel _trendModel;
        private List<ChartImage> _charts;
        private readonly string _subtitle;
        private readonly string _heading;

        private sealed class ChartToggleItem
        {
            public string Title;
            public ChartImage Source;
        }

        private sealed class ChartViewItem
        {
            public string Title { get; set; }
            public string Description { get; set; }
            public ImageSource Image { get; set; }
        }

        // ---- результаты выбора пользователя ----
        public bool ChartsEnabled => ShowChartsCb.IsChecked == true;
        public bool StatsEnabled => ShowStatsCb.IsChecked == true;
        public bool CommentsEnabled => ShowCommentsCb.IsChecked == true;
        public string SelectedTheme => (ThemeCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "Синяя";
        public List<ChartImage> SelectedCharts => !ChartsEnabled
            ? new List<ChartImage>()
            : ChartTogglePanel.Children.OfType<CheckBox>()
                .Where(cb => cb.IsChecked == true && cb.Tag is ChartToggleItem)
                .Select(cb => ((ChartToggleItem)cb.Tag).Source)
                .ToList();

        // ---------- конструкторы для каждого типа ----------

        public LivePreviewWindow(ReportModel model, List<ChartImage> charts, string subtitle,
            string heading, string theme = "Синяя")
            : this(PreviewKind.Obrasheniya, model, null, null, charts, subtitle, heading, theme)
        {
        }

        public LivePreviewWindow(UniversalReportModel model, string subtitle,
            string heading, string theme = "Синяя")
            : this(PreviewKind.Universal, null, model, null, null, subtitle, heading, theme)
        {
        }

        public LivePreviewWindow(TrendReportModel model, List<ChartImage> charts, string subtitle,
            string heading, string theme = "Синяя")
            : this(PreviewKind.Trend, null, null, model, charts, subtitle, heading, theme)
        {
        }

        private LivePreviewWindow(PreviewKind kind, ReportModel obr, UniversalReportModel univ,
            TrendReportModel trend, List<ChartImage> charts, string subtitle, string heading, string theme)
        {
            InitializeComponent();
            _kind = kind;
            _obrModel = obr;
            _univModel = univ;
            _trendModel = trend;
            _charts = charts ?? new List<ChartImage>();
            _subtitle = subtitle;
            _heading = heading;

            ThemeCombo.SelectedIndex = Math.Max(0, Array.IndexOf(new[] { "Синяя", "Зелёная", "Графит", "Светлая" }, theme));

            // чекбокс «Комментарии» имеет смысл только для обращений
            if (_kind != PreviewKind.Obrasheniya)
                ShowCommentsCb.Visibility = Visibility.Collapsed;

            if (_kind == PreviewKind.Universal)
                RankingOptionsPanel.Visibility = Visibility.Collapsed;
            else if (_kind == PreviewKind.Trend)
            {
                RankingControlCb.Content = "Закрытые обращения";
                RankingClosedCb.Content = "Закрытые наряды";
                RankingNewCb.Content = "Всего закрыто";
                RankingNoMovementCb.Visibility = Visibility.Collapsed;
                RankingAverageDaysCb.Visibility = Visibility.Collapsed;
            }
            else if (_obrModel.Layout != LayoutType.Repairs)
            {
                RankingAverageDaysCb.Visibility = Visibility.Collapsed;
            }

            // чекбокс «Графики» — скрываем если графиков нет
            if (_charts.Count == 0)
            {
                ShowChartsCb.Visibility = Visibility.Collapsed;
            }

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

        // ---------- таблица ----------
        private void RenderTable()
        {
            switch (_kind)
            {
                case PreviewKind.Obrasheniya: RenderObrTable(); break;
                case PreviewKind.Universal:   RenderUnivTable(); break;
                case PreviewKind.Trend:       RenderTrendTable(); break;
            }
            ApplyTheme(SelectedTheme);
        }

        private void RenderObrTable()
        {
            var headers = BuildObrHeaders();
            TablePreview.Columns.Clear();
            bool showComments = ShowCommentsCb.IsChecked == true;
            for (int i = 0; i < headers.Count; i++)
            {
                if (i == headers.Count - 1 && !showComments) continue;
                AddColumn(headers[i]);
            }

            var items = new System.Collections.ObjectModel.ObservableCollection<Dictionary<string, object>>();
            foreach (var row in _obrModel.Rows)
            {
                var dict = new Dictionary<string, object>();
                int idx = 0;
                AddCell(dict, headers[idx++], row.Index);
                AddCell(dict, headers[idx++], row.Number);
                AddCell(dict, headers[idx++], row.Responsible);

                if (_obrModel.Layout == LayoutType.Repairs)
                {
                    AddCell(dict, headers[idx++], row.ObjectName);
                    AddCell(dict, headers[idx++], row.Classifier);
                    AddCell(dict, headers[idx++], row.Severity);
                    AddCell(dict, headers[idx++], row.Days);
                }
                else
                {
                    AddCell(dict, headers[idx++], row.ObjectName);
                    if (_obrModel.HasService) AddCell(dict, headers[idx++], row.Service);
                }
                foreach (var st in row.StatusByDate) AddCell(dict, headers[idx++], st);
                AddCell(dict, headers[idx++], row.Itog);
                AddCell(dict, headers[idx++], row.Comment);
                items.Add(dict);
            }
            TablePreview.ItemsSource = items;
        }

        private void RenderUnivTable()
        {
            bool tracked = !string.IsNullOrEmpty(_univModel.TrackedHeader);
            var headers = new List<string> { "№ п/п", _univModel.KeyHeader };
            headers.AddRange(_univModel.DisplayHeaders);
            if (tracked)
                foreach (var d in _univModel.DateLabels) headers.Add(_univModel.TrackedHeader + " " + d);
            headers.Add("Итог");

            TablePreview.Columns.Clear();
            foreach (var h in headers) AddColumn(h);

            var items = new System.Collections.ObjectModel.ObservableCollection<Dictionary<string, object>>();
            foreach (var row in _univModel.Rows)
            {
                var dict = new Dictionary<string, object>();
                int idx = 0;
                AddCell(dict, headers[idx++], row.Index);
                AddCell(dict, headers[idx++], row.Key);
                foreach (var dv in row.DisplayValues) AddCell(dict, headers[idx++], dv);
                if (tracked) foreach (var tv in row.TrackedByDate) AddCell(dict, headers[idx++], tv);
                AddCell(dict, headers[idx++], row.Itog);
                items.Add(dict);
            }
            TablePreview.ItemsSource = items;
        }

        private void RenderTrendTable()
        {
            int n = _trendModel.DateLabels.Count;
            var headers = new List<string> { "№", "Ответственный" };
            for (int i = 0; i < n; i++) headers.Add("Обр. " + _trendModel.DateLabels[i]);
            headers.Add("Тренд обр.");
            for (int i = 0; i < n; i++) headers.Add("Нар. " + _trendModel.DateLabels[i]);
            headers.Add("Тренд нар.");

            TablePreview.Columns.Clear();
            foreach (var h in headers) AddColumn(h);

            var items = new System.Collections.ObjectModel.ObservableCollection<Dictionary<string, object>>();
            foreach (var row in _trendModel.Rows)
            {
                var dict = new Dictionary<string, object>();
                int idx = 0;
                AddCell(dict, headers[idx++], row.Index);
                AddCell(dict, headers[idx++], row.Responsible);
                for (int i = 0; i < n; i++) AddCell(dict, headers[idx++], row.ObrClosed[i]);
                AddCell(dict, headers[idx++], FormatDelta(row.ObrDelta));
                for (int i = 0; i < n; i++) AddCell(dict, headers[idx++], row.NarClosed[i]);
                AddCell(dict, headers[idx++], FormatDelta(row.NarDelta));
                items.Add(dict);
            }

            // строка ИТОГО
            {
                var dict = new Dictionary<string, object>();
                int idx = 0;
                AddCell(dict, headers[idx++], "");
                AddCell(dict, headers[idx++], "ИТОГО");
                for (int i = 0; i < n; i++) AddCell(dict, headers[idx++], _trendModel.TotalObrClosed[i]);
                AddCell(dict, headers[idx++], FormatDelta(_trendModel.TotalObrDelta));
                for (int i = 0; i < n; i++) AddCell(dict, headers[idx++], _trendModel.TotalNarClosed[i]);
                AddCell(dict, headers[idx++], FormatDelta(_trendModel.TotalNarDelta));
                items.Add(dict);
            }
            TablePreview.ItemsSource = items;
        }

        private void AddColumn(string header)
        {
            string key = SafeKey(header);
            TablePreview.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding($"[{key}]"),
                Width = new DataGridLength(header.Length > 25 ? 200 : 140)
            });
        }

        private List<string> BuildObrHeaders()
        {
            var headers = new List<string> { "№ п/п", "№ обращения", "Ответственный" };
            if (_obrModel.Layout == LayoutType.Repairs)
            {
                headers.Add("Филиал");
                headers.Add("Классификатор");
                headers.Add("Критичность");
                headers.Add("Дней в сост.");
            }
            else
            {
                headers.Add("Объект (клиент)");
                if (_obrModel.HasService) headers.Add("Услуга");
            }
            foreach (var s in _obrModel.Snapshots)
                headers.Add((_obrModel.Layout == LayoutType.Repairs ? "Состояние " : "Статус ") + s.Label);
            headers.Add("Итог");
            headers.Add("Комментарий");
            return headers;
        }

        private static string SafeKey(string header) =>
            header.Replace(" ", "_").Replace("/", "_").Replace("№", "N").Replace("(", "").Replace(")", "")
                  .Replace(".", "_").Replace(",", "_").Replace("-", "_");

        private static void AddCell(Dictionary<string, object> dict, string header, object value)
        {
            dict[SafeKey(header)] = value ?? "";
        }

        private static string FormatDelta(int delta) =>
            delta > 0 ? "+" + delta : delta.ToString();

        // ---------- графики ----------
        private void RenderCharts()
        {
            var visible = SelectedCharts;

            ChartsPreview.ItemsSource = visible.Select(c => new ChartViewItem
            {
                Title = c.Title,
                Description = c.Description,
                Image = ToBitmap(c.Png)
            }).ToList();
        }

        // ---------- статистика ----------
        private void RenderStats()
        {
            switch (_kind)
            {
                case PreviewKind.Obrasheniya: RenderObrStats(); break;
                case PreviewKind.Universal:   RenderUnivStats(); break;
                case PreviewKind.Trend:       RenderTrendStats(); break;
            }
        }

        private void RenderObrStats()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ОБЩАЯ СТАТИСТИКА И ДИНАМИКА");
            sb.AppendLine(new string('=', 46));
            sb.AppendLine();
            sb.AppendLine($"Всего обращений: {_obrModel.Rows.Count}");
            if (_obrModel.Layout == LayoutType.Repairs)
                sb.AppendLine($"Обработано — состояние изменилось: {_obrModel.ProcessedTotal}");
            sb.AppendLine($"Закрыто — нет в последней выгрузке: {_obrModel.ClosedTotal}");
            sb.AppendLine($"На контроле исполнения — есть в последней выгрузке: {_obrModel.OnControlTotal}");
            if (_obrModel.Layout == LayoutType.Repairs)
                sb.AppendLine($"  из них во внешних состояниях: {_obrModel.OnControlExternal}");
            sb.AppendLine();
            sb.AppendLine("ДИНАМИКА МЕЖДУ ВЫГРУЗКАМИ:");
            for (int i = 0; i < _obrModel.Snapshots.Count - 1; i++)
            {
                string a = _obrModel.Snapshots[i].Label, b = _obrModel.Snapshots[i + 1].Label;
                sb.AppendLine($"  {a} → {b}: закрытые {_obrModel.LeftCounts[i]}, новых {_obrModel.NewCounts[i]}" +
                    (_obrModel.Layout == LayoutType.Repairs ? $", изменили состояние {_obrModel.ChangedCounts[i]}" : ""));
            }
            sb.AppendLine($"  Всего закрытых: {_obrModel.LeftCounts.Sum()}; новых: {_obrModel.NewCounts.Sum()}" +
                (_obrModel.Layout == LayoutType.Repairs ? $"; изменений: {_obrModel.ChangedCounts.Sum()}" : ""));
            sb.AppendLine();
            sb.AppendLine("СРАВНЕНИЕ С ПРЕДЫДУЩИМ ПЕРИОДОМ:");
            sb.AppendLine(ReportChartRenderer.BuildPeriodComparisonText(_obrModel));
            if (!string.IsNullOrWhiteSpace(_obrModel.Description))
            {
                sb.AppendLine();
                sb.AppendLine("ОПИСАНИЕ ОТЧЁТА:");
                sb.AppendLine(_obrModel.Description);
            }
            StatsPreview.Text = sb.ToString();
        }

        private void RenderUnivStats()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ИТОГИ СРАВНЕНИЯ");
            sb.AppendLine(new string('=', 46));
            sb.AppendLine();
            sb.AppendLine($"Всего уникальных значений ключа: {_univModel.Rows.Count}");
            sb.AppendLine($"  Добавлено: {_univModel.Added}");
            sb.AppendLine($"  Удалено: {_univModel.Removed}");
            sb.AppendLine($"  Изменено: {_univModel.Changed}");
            sb.AppendLine($"  Без изменений: {_univModel.Same}");
            sb.AppendLine();
            sb.AppendLine("ЧТО ОЗНАЧАЮТ ИТОГИ:");
            sb.AppendLine("  Добавлено — строка появилась после первой выгрузки и есть в последней.");
            sb.AppendLine("  Удалено — строки нет в последней выгрузке.");
            sb.AppendLine("  Изменено — изменилось значение отслеживаемого или выбранного столбца.");
            sb.AppendLine("  Без изменений — строка есть в первой и последней выгрузках без изменений.");
            sb.AppendLine();
            sb.AppendLine($"Ключевой столбец: {_univModel.KeyHeader}");
            if (!string.IsNullOrEmpty(_univModel.TrackedHeader))
                sb.AppendLine($"Отслеживаемый столбец: {_univModel.TrackedHeader}");
            sb.AppendLine($"Сравниваемые даты: {string.Join(", ", _univModel.DateLabels)}");
            if (!string.IsNullOrWhiteSpace(_univModel.Description))
            {
                sb.AppendLine();
                sb.AppendLine("ОПИСАНИЕ ОТЧЁТА:");
                sb.AppendLine(_univModel.Description);
            }
            StatsPreview.Text = sb.ToString();
        }

        private void RenderTrendStats()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ИТОГИ ДИНАМИКИ");
            sb.AppendLine(new string('=', 46));
            sb.AppendLine();
            for (int i = 0; i < _trendModel.DateLabels.Count; i++)
                sb.AppendLine($"{_trendModel.DateLabels[i]}: решено обращений {_trendModel.TotalObrClosed[i]}, " +
                              $"решено нарядов {_trendModel.TotalNarClosed[i]}");
            sb.AppendLine();
            sb.AppendLine("Закрытые обращения и наряды — значения счётчиков «(ЗАКРЫТО)» из исходных выгрузок.");
            sb.AppendLine();
            sb.AppendLine("СРАВНЕНИЕ С ПРЕДЫДУЩИМ ПЕРИОДОМ:");
            sb.AppendLine(TrendChartRenderer.BuildPeriodComparisonText(_trendModel));
            sb.AppendLine();
            sb.AppendLine($"Тренд обращений (последний − первый): {FormatDelta(_trendModel.TotalObrDelta)}");
            sb.AppendLine($"Тренд нарядов (последний − первый): {FormatDelta(_trendModel.TotalNarDelta)}");
            sb.AppendLine($"Ответственных в отчёте: {_trendModel.Rows.Count}");
            if (!string.IsNullOrWhiteSpace(_trendModel.Description))
            {
                sb.AppendLine();
                sb.AppendLine("ОПИСАНИЕ ОТЧЁТА:");
                sb.AppendLine(_trendModel.Description);
            }
            StatsPreview.Text = sb.ToString();
        }

        private void UpdateInfo()
        {
            string detail;
            switch (_kind)
            {
                case PreviewKind.Obrasheniya:
                    detail = _obrModel.Layout == LayoutType.Repairs
                        ? $"Обращений: {_obrModel.Rows.Count} · Обработано: {_obrModel.ProcessedTotal} · " +
                          $"Закрыто: {_obrModel.ClosedTotal} · На контроле: {_obrModel.OnControlTotal}"
                        : $"Обращений: {_obrModel.Rows.Count} · Закрыто: {_obrModel.ClosedTotal} · " +
                          $"На контроле: {_obrModel.OnControlTotal}";
                    break;
                case PreviewKind.Universal:
                    detail = $"Строк: {_univModel.Rows.Count} · Добавлено: {_univModel.Added} · " +
                             $"Удалено: {_univModel.Removed} · Изменено: {_univModel.Changed}";
                    break;
                case PreviewKind.Trend:
                    detail = $"Ответственных: {_trendModel.Rows.Count} · Периодов: {_trendModel.DateLabels.Count} · " +
                             $"Тренд обр.: {FormatDelta(_trendModel.TotalObrDelta)}";
                    break;
                default:
                    detail = "";
                    break;
            }
            PreviewInfo.Text = $"{_heading}\n{_subtitle}\n\n{detail}\nГрафиков: {_charts.Count}";
        }

        // ---------- тема ----------
        private void Theme_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded && _kind == PreviewKind.Obrasheniya)
                RegenerateObrCharts();
            else if (IsLoaded && _kind == PreviewKind.Trend)
                RegenerateTrendCharts();
            ApplyTheme(SelectedTheme);
        }

        private void RankingOptions_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            if (_kind == PreviewKind.Obrasheniya) RegenerateObrCharts();
            else if (_kind == PreviewKind.Trend) RegenerateTrendCharts();
        }

        private void RegenerateObrCharts()
        {
            var metrics = new List<ResponsibleRankingMetric>();
            if (RankingControlCb.IsChecked == true) metrics.Add(ResponsibleRankingMetric.OnControl);
            if (RankingClosedCb.IsChecked == true) metrics.Add(ResponsibleRankingMetric.Closed);
            if (RankingNewCb.IsChecked == true) metrics.Add(ResponsibleRankingMetric.New);
            if (RankingNoMovementCb.IsChecked == true) metrics.Add(ResponsibleRankingMetric.NoMovement);
            if (_obrModel.Layout == LayoutType.Repairs && RankingAverageDaysCb.IsChecked == true)
                metrics.Add(ResponsibleRankingMetric.AverageStateDays);

            _charts = ReportChartRenderer.RenderAll(_obrModel, ReportTheme.Get(SelectedTheme),
                SelectedRankingLimit(), metrics);
            ShowChartsCb.Visibility = _charts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            BuildChartToggles();
            RenderCharts();
            UpdateInfo();
        }

        private void RegenerateTrendCharts()
        {
            var metrics = new List<TrendRankingMetric>();
            if (RankingControlCb.IsChecked == true) metrics.Add(TrendRankingMetric.ClosedAppeals);
            if (RankingClosedCb.IsChecked == true) metrics.Add(TrendRankingMetric.ClosedWorkOrders);
            if (RankingNewCb.IsChecked == true) metrics.Add(TrendRankingMetric.TotalClosed);

            _charts = TrendChartRenderer.RenderAll(_trendModel, ReportTheme.Get(SelectedTheme),
                SelectedRankingLimit(), metrics);
            ShowChartsCb.Visibility = _charts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            BuildChartToggles();
            RenderCharts();
            UpdateInfo();
        }

        private int? SelectedRankingLimit()
        {
            if (!(RankingLimitCombo.SelectedItem is ComboBoxItem item)) return 10;
            string tag = item.Tag as string;
            if (string.Equals(tag, "all", StringComparison.OrdinalIgnoreCase)) return null;
            return int.TryParse(tag, out var parsed) ? parsed : 10;
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
            // событие может прийти во время InitializeComponent, когда элементы ещё не готовы
            if (!IsLoaded || PreviewTabs == null) return;

            // видимость вкладок
            foreach (var item in PreviewTabs.Items.OfType<TabItem>())
            {
                string header = item.Header as string;
                if (header == "Графики")
                    item.Visibility = ShowChartsCb.IsChecked == true && _charts.Count > 0
                        ? Visibility.Visible : Visibility.Collapsed;
                else if (header == "Статистика")
                    item.Visibility = ShowStatsCb.IsChecked == true
                        ? Visibility.Visible : Visibility.Collapsed;
            }

            // перестроить таблицу (колонка «Комментарий» зависит от чекбокса)
            RenderTable();
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

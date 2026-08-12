using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using ObrasReport.Core;
using ObrasReport.Models;

namespace ObrasReport
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<FileItem> _items = new ObservableCollection<FileItem>();
        private readonly ObservableCollection<ColumnChoice> _columns = new ObservableCollection<ColumnChoice>();

        public MainWindow()
        {
            InitializeComponent();
            Grid.ItemsSource = _items;
            ColumnsList.ItemsSource = _columns;
        }

        /// <summary>Пункт списка колонок для галочек «включить в отчёт».</summary>
        public class ColumnChoice
        {
            public int Pos { get; set; }
            public string Name { get; set; }
            public bool IsSelected { get; set; }
        }

        public class FileItem : INotifyPropertyChanged
        {
            public Snapshot Snapshot { get; set; }
            public UniversalTable Table { get; set; }   // универсальный разбор (любая таблица)
            public TrendSnapshot Trend { get; set; }    // разбор формата «закрытые обращения/наряды»
            public string FileName => Snapshot.FileName;

            // ---- выбор даты через день / месяц / год (обычные выпадающие списки) ----
            public static readonly IReadOnlyList<int> DayList = Enumerable.Range(1, 31).ToList();
            public static readonly IReadOnlyList<int> MonthList = Enumerable.Range(1, 12).ToList();
            public static readonly IReadOnlyList<int> YearList =
                Enumerable.Range(DateTime.Now.Year - 3, 6).ToList();

            public IReadOnlyList<int> Days => DayList;
            public IReadOnlyList<int> Months => MonthList;
            public IReadOnlyList<int> Years => YearList;

            private int? _day, _month, _year;

            public int? Day
            {
                get => _day ?? (Snapshot.DateDetected ? Snapshot.SortDate.Day : (int?)null);
                set { _day = value; Recompute(); OnPropertyChanged(nameof(Day)); }
            }
            public int? Month
            {
                get => _month ?? (Snapshot.DateDetected ? Snapshot.SortDate.Month : (int?)null);
                set { _month = value; Recompute(); OnPropertyChanged(nameof(Month)); }
            }
            public int? Year
            {
                get => _year ?? (Snapshot.DateDetected ? Snapshot.SortDate.Year : (int?)null);
                set { _year = value; Recompute(); OnPropertyChanged(nameof(Year)); }
            }

            private void Recompute()
            {
                int? d = Day, m = Month, y = Year;
                if (d.HasValue && m.HasValue && y.HasValue)
                {
                    try
                    {
                        var dt = new DateTime(y.Value, m.Value, d.Value);
                        Snapshot.SortDate = dt;
                        Snapshot.Label = dt.ToString("dd.MM");
                        Snapshot.DateDetected = true;
                        return;
                    }
                    catch { /* неверная дата, напр. 31.02 */ }
                }
                Snapshot.Label = "";
                Snapshot.DateDetected = false;
            }

            // ---- категория отчёта (для группировки и раздельных отчётов) ----
            public const string CatRepairs = ReportConstants.CatRepairs;
            public const string CatNonRepair = ReportConstants.CatNonRepair;
            public const string CatVideo = ReportConstants.CatVideo;

            private static readonly IReadOnlyList<string> RepairsCats = new[] { CatRepairs };
            private static readonly IReadOnlyList<string> ColoredCats = new[] { CatNonRepair, CatVideo };

            public IReadOnlyList<string> CategoryOptions =>
                Snapshot.Layout == LayoutType.Repairs ? RepairsCats : ColoredCats;

            private string _category;
            public string Category
            {
                get => _category ?? (_category = AutoCategory());
                set { _category = value; OnPropertyChanged(nameof(Category)); }
            }

            private string AutoCategory()
            {
                if (Snapshot.Layout == LayoutType.Repairs) return CatRepairs;
                string n = (Snapshot.FileName ?? "").ToUpperInvariant();
                if (n.Contains("ВИДЕОНАБЛЮД")) return CatVideo;
                return CatNonRepair;
            }

            public int Count => Snapshot.Records.Count > 0
                ? Snapshot.Records.Count
                : (Table != null ? Table.DataRows.Count : 0);

            public event PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged(string n) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        // Порядок выгрузок в отчёте определяется автоматически по дате (ReportEngine сортирует по SortDate),
        // поэтому «живая» пересортировка списка при выборе даты не нужна.

        // ---------- добавление ----------
        private void AddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Книги Excel (*.xlsx;*.xlsm;*.xls;*.xlsb)|*.xlsx;*.xlsm;*.xls;*.xlsb|Все файлы (*.*)|*.*",
                Title = "Выберите выгрузки обращений"
            };
            if (dlg.ShowDialog() == true)
                AddPaths(dlg.FileNames);
        }

        private void AddPaths(IEnumerable<string> paths)
        {
            var sb = new StringBuilder();
            int added = 0;
            foreach (var path in paths)
            {
                if (!XlsxReader.IsSupportedExcel(path))
                {
                    sb.AppendLine($"Пропущен (не Excel): {Path.GetFileName(path)}");
                    continue;
                }
                if (_items.Any(i => string.Equals(i.Snapshot.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                {
                    sb.AppendLine($"Уже добавлен: {Path.GetFileName(path)}");
                    continue;
                }
                try
                {
                    var snap = RegistryParser.Parse(path);
                    UniversalTable table = null;
                    try { table = UniversalParser.Parse(path); } catch { /* необязательно */ }
                    TrendSnapshot trend = null;
                    try { trend = ClosedTrendParser.TryParse(path); } catch { /* необязательно */ }
                    _items.Add(new FileItem { Snapshot = snap, Table = table, Trend = trend });
                    added++;
                    string dateShown = snap.DateDetected ? snap.Label : "дата не распознана — укажите в колонке «Дата»";
                    string kind = snap.Records.Count > 0 ? ToText(snap.Layout) + $", обращений: {snap.Records.Count}"
                                                         : $"таблица, строк: {(table != null ? table.DataRows.Count : 0)}";
                    sb.AppendLine($"Загружено: {dateShown} · {kind} · {snap.FileName}");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"ОШИБКА при чтении {Path.GetFileName(path)}: {ex.Message}");
                }
            }
            SortByDate();
            RefreshMode();
            if (added > 0) sb.AppendLine($"Добавлено файлов: {added}. Всего: {_items.Count}.");
            Log.Text = sb.ToString().TrimEnd();
        }

        private static string ToText(LayoutType t) => ReportConstants.LayoutText(t);

        private void SortByDate()
        {
            var sorted = _items.OrderBy(i => i.Snapshot.SortDate)
                               .ThenBy(i => i.Snapshot.FileName, StringComparer.OrdinalIgnoreCase)
                               .ToList();
            _items.Clear();
            foreach (var i in sorted) _items.Add(i);
        }

        // ---------- drag & drop ----------
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                AddPaths((string[])e.Data.GetData(DataFormats.FileDrop));
        }

        // ---------- операции со списком ----------
        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            foreach (var it in Grid.SelectedItems.Cast<FileItem>().ToList())
                _items.Remove(it);
            RefreshMode();
        }

        private void Clear_Click(object sender, RoutedEventArgs e) { _items.Clear(); RefreshMode(); }

        // ---------- выбор шаблона / универсальный режим ----------
        private void Template_Changed(object sender, SelectionChangedEventArgs e) => RefreshMode();

        /// <summary>Итоговый шаблон с учётом «Авто».</summary>
        private ReportTemplate Effective()
        {
            int sel = TemplateCombo?.SelectedIndex ?? 0;
            if (sel == 1) return ReportTemplate.Obrasheniya;
            if (sel == 2) return ReportTemplate.Universal;
            if (sel == 3) return ReportTemplate.ClosedTrend;
            // Авто
            if (_items.Count > 0 &&
                _items.All(i => i.Snapshot.Layout != LayoutType.Unknown && i.Snapshot.Records.Count > 0))
                return ReportTemplate.Obrasheniya;
            if (_items.Count > 0 && _items.All(i => i.Trend != null))
                return ReportTemplate.ClosedTrend;
            return ReportTemplate.Universal;
        }

        private void RefreshMode()
        {
            if (MappingPanel == null) return;
            bool universal = Effective() == ReportTemplate.Universal;
            MappingPanel.Visibility = universal ? Visibility.Visible : Visibility.Collapsed;
            if (universal) PopulateColumns();
            UpdateAnalyzer();
        }

        // ---------- умный анализатор ----------
        private void UpdateAnalyzer()
        {
            if (AnalyzerPanel == null) return;

            if (_items.Count == 0)
            {
                AnalyzerPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var inputs = _items.Select(i => new AnalyzerInput
            {
                FileName = i.FileName,
                Layout = i.Snapshot.Layout,
                RecordCount = i.Count,
                IsTrend = i.Trend != null,
                HasTable = i.Table != null && i.Table.Columns.Count > 0,
                Category = i.Category
            }).ToList();

            var result = ReportAnalyzer.Analyze(inputs);

            AnalyzerSummary.Text = result.Summary;
            AnalyzerRecommendation.Text = result.Recommendation;
            AnalyzerWarnings.ItemsSource = result.Warnings.Count > 0 ? result.Warnings : null;

            // кнопка «Применить» — переводит ComboBox шаблонов на рекомендованный
            int targetIndex = 0;
            switch (result.RecommendedTemplate)
            {
                case ReportTemplate.Obrasheniya: targetIndex = 1; break;
                case ReportTemplate.Universal:   targetIndex = 2; break;
                case ReportTemplate.ClosedTrend: targetIndex = 3; break;
            }
            ApplyRecommendationBtn.Visibility =
                TemplateCombo.SelectedIndex != targetIndex ? Visibility.Visible : Visibility.Collapsed;

            AnalyzerPanel.Visibility = Visibility.Visible;
        }

        private void ApplyRecommendation_Click(object sender, RoutedEventArgs e)
        {
            var inputs = _items.Select(i => new AnalyzerInput
            {
                FileName = i.FileName,
                Layout = i.Snapshot.Layout,
                RecordCount = i.Count,
                IsTrend = i.Trend != null,
                HasTable = i.Table != null && i.Table.Columns.Count > 0,
                Category = i.Category
            }).ToList();
            var result = ReportAnalyzer.Analyze(inputs);

            int targetIndex = 0;
            switch (result.RecommendedTemplate)
            {
                case ReportTemplate.Obrasheniya: targetIndex = 1; break;
                case ReportTemplate.Universal:   targetIndex = 2; break;
                case ReportTemplate.ClosedTrend: targetIndex = 3; break;
            }
            if (TemplateCombo.SelectedIndex != targetIndex)
                TemplateCombo.SelectedIndex = targetIndex;
        }

        private void OpenWizard_Click(object sender, RoutedEventArgs e)
        {
            var wizard = new WizardWindow(this) { Owner = this };
            wizard.ShowDialog();
        }

        /// <summary>Список категорий по загруженным файлам (для мастера).</summary>
        public List<string> DetectedCategories() =>
            _items.Select(i => i.Category).Where(c => !string.IsNullOrWhiteSpace(c))
                  .Distinct().OrderBy(c => c).ToList();

        /// <summary>Применяет выбор мастера: шаблон, категорию, опции и тему.</summary>
        public void ApplyWizard(ReportTemplate template, string category,
            bool showCharts, bool showStats, bool showComments, string theme)
        {
            // шаблон
            int idx = 0;
            switch (template)
            {
                case ReportTemplate.Obrasheniya: idx = 1; break;
                case ReportTemplate.Universal:   idx = 2; break;
                case ReportTemplate.ClosedTrend: idx = 3; break;
            }
            if (TemplateCombo.SelectedIndex != idx)
                TemplateCombo.SelectedIndex = idx;

            // категория (пока только запоминаем — фильтрация в Generate_Click)
            _wizardCategory = category;
            _wizardShowCharts = showCharts;
            _wizardShowStats = showStats;
            _wizardShowComments = showComments;
            _wizardTheme = theme;

            Log.Text = "Мастер применил настройки. Нажмите «Сформировать отчёт» для предпросмотра.";
        }

        private string _wizardCategory;
        private bool _wizardShowCharts = true;
        private bool _wizardShowStats = true;
        private bool _wizardShowComments = true;
        private string _wizardTheme = "Синяя";

        /// <summary>Заполняет списки колонок для универсального режима из первого файла.</summary>
        private void PopulateColumns()
        {
            var first = _items.FirstOrDefault(i => i.Table != null && i.Table.Columns.Count > 0);
            if (first == null) { _columns.Clear(); KeyCombo.ItemsSource = null; TrackedCombo.ItemsSource = null; return; }

            // не пересобираем, если состав колонок совпадает (чтобы не терять выбор)
            var names = first.Table.Columns.Select(c => c.Name).ToList();
            var current = _columns.Select(c => c.Name).ToList();
            if (names.SequenceEqual(current) && KeyCombo.ItemsSource != null) return;

            var cols = first.Table.Columns;
            KeyCombo.ItemsSource = cols;
            KeyCombo.DisplayMemberPath = "Name";
            var trackedOptions = new List<ColumnInfo> { new ColumnInfo { Pos = 0, Name = "(нет)" } };
            trackedOptions.AddRange(cols);
            TrackedCombo.ItemsSource = trackedOptions;
            TrackedCombo.DisplayMemberPath = "Name";

            _columns.Clear();
            foreach (var c in cols)
                _columns.Add(new ColumnChoice { Pos = c.Pos, Name = c.Name, IsSelected = LooksDisplay(c.Name) });

            KeyCombo.SelectedItem = cols.FirstOrDefault(c => LooksKey(c.Name)) ?? cols.FirstOrDefault();
            TrackedCombo.SelectedItem = trackedOptions.FirstOrDefault(c => LooksTracked(c.Name)) ?? trackedOptions[0];
        }

        private static bool LooksKey(string n)
        {
            n = (n ?? "").ToLowerInvariant();
            return n.Contains("№") || n.Contains("номер") || n.Contains("id") || n.Contains("ключ")
                   || n.Contains("обращение") || n.Contains("инв");
        }
        private static bool LooksTracked(string n)
        {
            n = (n ?? "").ToLowerInvariant();
            return n.Contains("состояние") || n.Contains("статус");
        }
        private static bool LooksDisplay(string n)
        {
            n = (n ?? "").ToLowerInvariant();
            return n.Contains("ответствен") || n.Contains("клиент") || n.Contains("объект")
                   || n.Contains("услуга") || n.Contains("филиал") || n.Contains("классификатор");
        }

        private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);
        private void Down_Click(object sender, RoutedEventArgs e) => Move(1);

        private void Move(int delta)
        {
            if (Grid.SelectedItem is FileItem it)
            {
                int i = _items.IndexOf(it);
                int j = i + delta;
                if (j >= 0 && j < _items.Count)
                {
                    _items.Move(i, j);
                    Grid.SelectedItem = it;
                }
            }
        }

        // ---------- формирование отчёта ----------
        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count < 2)
            {
                MessageBox.Show("Добавьте не менее двух выгрузок.",
                    "Недостаточно данных", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var noDate = _items.Where(i => string.IsNullOrWhiteSpace(i.Snapshot.Label)).ToList();
            if (noDate.Count > 0)
            {
                MessageBox.Show(
                    "Не указана дата для файлов:\n\n" +
                    string.Join("\n", noDate.Select(i => "• " + i.FileName)) +
                    "\n\nВыберите дату списками день · месяц · год.",
                    "Не указана дата", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Effective() == ReportTemplate.Universal) { GenerateUniversal(); return; }
            if (Effective() == ReportTemplate.ClosedTrend) { GenerateTrend(); return; }

            // группировка по категории — по одному отчёту на категорию
            var groups = _items.GroupBy(i => i.Category).ToList();
            var ready = groups.Where(g => g.Count() >= 2).ToList();
            var tooFew = groups.Where(g => g.Count() < 2).ToList();

            if (ready.Count == 0)
            {
                MessageBox.Show(
                    "Ни в одной категории нет двух и более выгрузок:\n\n" +
                    string.Join("\n", groups.Select(g => $"• {g.Key}: {g.Count()} файл(ов)")) +
                    "\n\nДля сравнения нужно минимум 2 файла одной категории.",
                    "Недостаточно данных", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string desc = DescriptionBox.Text?.Trim();
            var created = new List<Tuple<string, string, ReportModel>>();
            var errors = new List<string>();

            if (ready.Count == 1)
            {
                var g = ready[0];
                try
                {
                    var model = ReportEngine.Build(g.Select(i => i.Snapshot).ToList());
                    model.Description = desc;
                    model.CategoryLabel = g.Key;

                    var charts = RenderObrCharts(model, out string chartWarn);
                    string periods = string.Join(", ", model.Snapshots.Select(s => s.Label));
                    string subtitle = $"Категория: {g.Key}. Периоды: {periods}." +
                                     (string.IsNullOrWhiteSpace(desc) ? "" : " Описание: " + desc);

                    var preview = new LivePreviewWindow(
                        model, charts, subtitle,
                        heading: "Графики и диаграммы — " + g.Key,
                        theme: _wizardTheme)
                    { Owner = this };
                    if (preview.ShowDialog() != true)
                    {
                        Log.Text = "Сохранение отчёта отменено.";
                        return;
                    }

                    var dlg = new SaveFileDialog
                    {
                        Filter = "Книга Excel (*.xlsx)|*.xlsx",
                        FileName = SuggestName(model),
                        Title = "Сохранить отчёт",
                        InitialDirectory = Path.GetDirectoryName(g.First().Snapshot.FilePath)
                    };
                    if (dlg.ShowDialog() != true) return;

                    ExcelReportWriter.Write(model, dlg.FileName, preview.ChartsEnabled ? charts : null);
                    created.Add(Tuple.Create(g.Key, dlg.FileName, model));
                }
                catch (Exception ex) { errors.Add($"{g.Key}: {ex.Message}"); }
            }
            else
            {
                // несколько категорий — отдельный отчёт (и свои графики) на каждую
                string targetDir;
                using (var fbd = new System.Windows.Forms.FolderBrowserDialog())
                {
                    fbd.Description = "Выберите папку для сохранения отчётов (по одному на категорию)";
                    fbd.SelectedPath = Path.GetDirectoryName(ready[0].First().Snapshot.FilePath);
                    if (fbd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    targetDir = fbd.SelectedPath;
                }

                foreach (var g in ready)
                {
                    try
                    {
                        var model = ReportEngine.Build(g.Select(i => i.Snapshot).ToList());
                        model.Description = desc;
                        model.CategoryLabel = g.Key;

                        var charts = RenderObrCharts(model, out string chartWarn);
                        string periods = string.Join(", ", model.Snapshots.Select(s => s.Label));
                        string subtitle = $"Категория: {g.Key}. Периоды: {periods}." +
                                         (string.IsNullOrWhiteSpace(desc) ? "" : " Описание: " + desc);

                        var preview = new LivePreviewWindow(
                            model, charts, subtitle,
                            heading: "Графики и диаграммы — " + g.Key,
                            theme: _wizardTheme)
                        { Owner = this };
                        if (preview.ShowDialog() != true)
                        {
                            errors.Add($"{g.Key}: сохранение пропущено пользователем");
                            continue;
                        }

                        string path = Path.Combine(targetDir, SuggestName(model));
                        ExcelReportWriter.Write(model, path, preview.ChartsEnabled ? charts : null);
                        created.Add(Tuple.Create(g.Key, path, model));
                    }
                    catch (Exception ex) { errors.Add($"{g.Key}: {ex.Message}"); }
                }
            }

            // сводка
            var sb = new StringBuilder();
            sb.AppendLine($"Сформировано отчётов: {created.Count}.");
            foreach (var c in created)
            {
                var m = c.Item3;
                sb.AppendLine($"• {c.Item1}: обращений {m.Rows.Count}, обработано {m.ProcessedTotal}, " +
                              $"на контроле {m.OnControlTotal} — {Path.GetFileName(c.Item2)}");
            }
            foreach (var g in tooFew)
                sb.AppendLine($"⚠ Пропущено «{g.Key}» — только {g.Count()} файл(ов), нужно ≥2.");
            foreach (var er in errors)
                sb.AppendLine($"ОШИБКА {er}");
            Log.Text = sb.ToString().TrimEnd();

            if (created.Count > 0)
            {
                string msg = $"Готово. Сформировано отчётов: {created.Count}.\n\n" +
                    string.Join("\n", created.Select(c => "• " + c.Item1)) +
                    (tooFew.Count > 0 ? "\n\nПропущено (менее 2 файлов): " + string.Join(", ", tooFew.Select(g => g.Key)) : "") +
                    (errors.Count > 0 ? "\n\nОшибки: " + errors.Count : "") +
                    "\n\nОткрыть папку с отчётами?";
                var res = MessageBox.Show(msg, "Готово", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (res == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{created[0].Item2}\"") { UseShellExecute = true });
            }
            else if (errors.Count > 0)
            {
                MessageBox.Show("Не удалось сформировать отчёты:\n\n" + string.Join("\n", errors),
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------- универсальный (нейтральный) отчёт ----------
        private void GenerateUniversal()
        {
            var missing = _items.Where(i => i.Table == null || i.Table.Columns.Count == 0).ToList();
            if (missing.Count > 0)
            {
                MessageBox.Show("Не удалось прочитать таблицу в файлах:\n\n" +
                    string.Join("\n", missing.Select(i => "• " + i.FileName)),
                    "Ошибка чтения", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!(KeyCombo.SelectedItem is ColumnInfo key) || key.Pos <= 0)
            {
                MessageBox.Show("Выберите ключевой столбец (ID) для сопоставления строк.",
                    "Не выбран ключ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int trackedPos = (TrackedCombo.SelectedItem as ColumnInfo)?.Pos ?? 0;
            var displayPos = _columns.Where(c => c.IsSelected).Select(c => c.Pos).ToList();

            var master = _items.First(i => i.Table != null && i.Table.Columns.Count > 0).Table.Columns;
            Func<int, string> colName = pos =>
                master.FirstOrDefault(c => c.Pos == pos)?.Name ?? ("Столбец " + pos);

            var inputs = _items.Select(i => new UniversalEngine.Input
            {
                Table = i.Table,
                Date = i.Snapshot.SortDate,
                Label = i.Snapshot.Label
            }).ToList();

            try
            {
                var model = UniversalEngine.Build(inputs, key.Pos, trackedPos, displayPos, colName);
                model.Title = "Универсальное сравнение таблиц";
                model.Description = DescriptionBox.Text?.Trim();

                var dlg = new SaveFileDialog
                {
                    Filter = "Книга Excel (*.xlsx)|*.xlsx",
                    FileName = SuggestUniversalName(model),
                    Title = "Сохранить отчёт",
                    InitialDirectory = Path.GetDirectoryName(_items[0].Snapshot.FilePath)
                };
                if (dlg.ShowDialog() != true) return;

                UniversalReportWriter.Write(model, dlg.FileName);

                Log.Text = $"Универсальный отчёт сформирован: {dlg.FileName}\n" +
                           $"Ключ: {model.KeyHeader}. Строк: {model.Rows.Count}. " +
                           $"Добавлено {model.Added}, удалено {model.Removed}, изменено {model.Changed}, без изменений {model.Same}.";

                var res = MessageBox.Show(
                    $"Отчёт сформирован:\n{dlg.FileName}\n\n" +
                    $"Добавлено: {model.Added}\nУдалено: {model.Removed}\nИзменено: {model.Changed}\nБез изменений: {model.Same}\n\nОткрыть файл?",
                    "Готово", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (res == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка формирования отчёта", MessageBoxButton.OK, MessageBoxImage.Error);
                Log.Text = "ОШИБКА: " + ex.Message;
            }
        }

        private static List<ChartImage> RenderObrCharts(ReportModel model, out string warning)
        {
            warning = null;
            try
            {
                var charts = ReportChartRenderer.RenderAll(model);
                if (charts == null || charts.Count == 0)
                    warning = "Графики и диаграммы не удалось построить. Таблицы будут сохранены без визуализаций.";
                return charts ?? new List<ChartImage>();
            }
            catch (Exception ex)
            {
                warning = "Ошибка построения графиков/диаграмм: " + ex.Message;
                return new List<ChartImage>();
            }
        }

        private static string SuggestUniversalName(UniversalReportModel m)
        {
            string dates = string.Join("-", m.DateLabels);
            string name = $"Универсальное сравнение {dates}.xlsx";
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        }

        // ---------- отчёт динамики (закрытые обращения/наряды) ----------
        private void GenerateTrend()
        {
            var missing = _items.Where(i => i.Trend == null).ToList();
            if (missing.Count > 0)
            {
                MessageBox.Show("Не распознаны как «закрытые обращения/наряды»:\n\n" +
                    string.Join("\n", missing.Select(i => "• " + i.FileName)) +
                    "\n\nНужны выгрузки со счётчиками «Количество обращений/нарядов».",
                    "Формат не подходит", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var snaps = _items.Select(i =>
                {
                    i.Trend.Date = i.Snapshot.SortDate;   // дата из имени файла или выбранная вручную
                    i.Trend.Label = i.Snapshot.Label;
                    return i.Trend;
                }).ToList();

                var model = ClosedTrendEngine.Build(snaps);
                model.Title = "Динамика решённых обращений и нарядов";
                model.Description = DescriptionBox.Text?.Trim();

                List<ChartImage> charts = null;
                string chartWarn = null;
                try
                {
                    charts = TrendChartRenderer.RenderAll(model);
                    if (charts == null || charts.Count == 0)
                        chartWarn = "Графики и диаграммы не удалось построить. Таблицы будут сохранены без визуализаций (или с пустым листом «Графики»).";
                }
                catch (Exception cex)
                {
                    charts = new List<ChartImage>();
                    chartWarn = "Ошибка построения графиков/диаграмм: " + cex.Message +
                                " Таблицы можно сохранить без визуализаций.";
                }

                string periods = string.Join(", ", model.DateLabels);
                string subtitle = $"Периоды: {periods}." +
                                  (string.IsNullOrWhiteSpace(model.Description) ? "" : " Описание: " + model.Description);
                var preview = new ChartPreviewWindow(subtitle, charts, chartWarn,
                    heading: "Графики и диаграммы динамики")
                { Owner = this };
                if (preview.ShowDialog() != true || !preview.SaveConfirmed)
                {
                    Log.Text = "Сохранение отчёта динамики отменено.";
                    return;
                }

                var dlg = new SaveFileDialog
                {
                    Filter = "Книга Excel (*.xlsx)|*.xlsx",
                    FileName = SuggestTrendName(model),
                    Title = "Сохранить отчёт",
                    InitialDirectory = Path.GetDirectoryName(_items[0].Snapshot.FilePath)
                };
                if (dlg.ShowDialog() != true) return;

                ClosedTrendWriter.Write(model, dlg.FileName, charts);

                Log.Text = $"Отчёт динамики сформирован: {dlg.FileName}\n" +
                           $"Периоды: {string.Join(", ", model.DateLabels)}. Ответственных: {model.Rows.Count}.\n" +
                           $"Решено обращений: {string.Join(" → ", model.TotalObrClosed)} (тренд {(model.TotalObrDelta > 0 ? "+" : "")}{model.TotalObrDelta}). " +
                           $"Решено нарядов: {string.Join(" → ", model.TotalNarClosed)} (тренд {(model.TotalNarDelta > 0 ? "+" : "")}{model.TotalNarDelta}).\n" +
                           $"Графиков в отчёте: {(charts?.Count ?? 0)}." +
                           (chartWarn != null ? "\n⚠ " + chartWarn : "");

                var res = MessageBox.Show(
                    $"Отчёт сформирован:\n{dlg.FileName}\n\n" +
                    $"Решено обращений: {string.Join(" → ", model.TotalObrClosed)} (тренд {(model.TotalObrDelta > 0 ? "+" : "")}{model.TotalObrDelta})\n" +
                    $"Решено нарядов: {string.Join(" → ", model.TotalNarClosed)} (тренд {(model.TotalNarDelta > 0 ? "+" : "")}{model.TotalNarDelta})\n" +
                    $"Графиков: {(charts?.Count ?? 0)}\n\nОткрыть файл?",
                    "Готово", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (res == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка формирования отчёта", MessageBoxButton.OK, MessageBoxImage.Error);
                Log.Text = "ОШИБКА: " + ex.Message;
            }
        }

        private static string SuggestTrendName(TrendReportModel m)
        {
            string name = $"Динамика решённых обращений и нарядов {string.Join("-", m.DateLabels)}.xlsx";
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        }

        private static string SuggestName(ReportModel m)
        {
            string cat = string.IsNullOrWhiteSpace(m.CategoryLabel)
                ? (m.Layout == LayoutType.Repairs ? "По ремонту" : "Не по ремонту")
                : m.CategoryLabel;
            string dates = string.Join("-", m.Snapshots.Select(s => s.Label));
            string name = $"Отчёт по обработке обращений ({cat}) {dates}.xlsx";
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        }
    }
}

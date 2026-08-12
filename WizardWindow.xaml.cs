using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ObrasReport.Core;
using ObrasReport.Models;

namespace ObrasReport
{
    /// <summary>
    /// Пошаговый мастер: помогает выбрать цель отчёта, категорию и оформление,
    /// затем применяет выбор в главном окне и открывает живой предпросмотр.
    /// </summary>
    public partial class WizardWindow : Window
    {
        private readonly MainWindow _main;
        private int _step;
        private readonly List<Func<UIElement>> _steps;

        // ответы
        private ReportTemplate _template = ReportTemplate.Auto;
        private string _category = null;                 // null = все категории
        private bool _showCharts = true;
        private bool _showStats = true;
        private bool _showComments = true;
        private string _theme = "Синяя";

        private const int StepCount = 4;

        public WizardWindow(MainWindow main)
        {
            InitializeComponent();
            _main = main;

            _steps = new List<Func<UIElement>>
            {
                StepTemplate,
                StepCategory,
                StepContents,
                StepTheme
            };
            ShowStep(0);
        }

        private void ShowStep(int step)
        {
            _step = step;
            StepIndicator.Text = $"Шаг {step + 1} из {StepCount}";
            StepTitle.Text = StepTitleText(step);
            StepContentPanel.Children.Clear();
            StepContentPanel.Children.Add(_steps[step]());

            BackBtn.Visibility = step == 0 ? Visibility.Collapsed : Visibility.Visible;
            NextBtn.Content = step == StepCount - 1 ? "Готово ✓" : "Далее →";
        }

        private static string StepTitleText(int step)
        {
            switch (step)
            {
                case 0: return "— Что хотите получить?";
                case 1: return "— Какая категория?";
                case 2: return "— Что показать в отчёте?";
                default: return "— Как оформить?";
            }
        }

        // ---------- шаг 1: цель ----------
        private UIElement StepTemplate()
        {
            var panel = new StackPanel();

            var auto = new RadioButton { Content = "Определить автоматически (рекомендуется)", IsChecked = true, Margin = new Thickness(0, 0, 0, 8), FontWeight = FontWeights.SemiBold };
            var obr = new RadioButton { Content = "Отчёт по обращениям (Обработано / На контроле)", Margin = new Thickness(0, 0, 0, 8) };
            var univ = new RadioButton { Content = "Универсальное сравнение таблиц (по ключу)", Margin = new Thickness(0, 0, 0, 8) };
            var trend = new RadioButton { Content = "Динамика закрытых обращений/нарядов", Margin = new Thickness(0, 0, 0, 8) };

            auto.Tag = ReportTemplate.Auto;
            obr.Tag = ReportTemplate.Obrasheniya;
            univ.Tag = ReportTemplate.Universal;
            trend.Tag = ReportTemplate.ClosedTrend;

            if (_template == ReportTemplate.Obrasheniya) obr.IsChecked = true;
            if (_template == ReportTemplate.Universal) univ.IsChecked = true;
            if (_template == ReportTemplate.ClosedTrend) trend.IsChecked = true;

            foreach (var rb in new[] { auto, obr, univ, trend })
            {
                var clone = rb;
                rb.Checked += (s, e) => _template = (ReportTemplate)clone.Tag;
                panel.Children.Add(rb);
            }

            panel.Children.Add(new TextBlock
            {
                Text = "Программа проанализирует ваши файлы и подскажет оптимальный вариант. Выбор можно изменить позже вручную.",
                Foreground = System.Windows.Media.Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(26, 6, 0, 0),
                FontSize = 12
            });
            return panel;
        }

        // ---------- шаг 2: категория ----------
        private UIElement StepCategory()
        {
            var panel = new StackPanel();
            var categories = _main.DetectedCategories();
            bool hasCategories = categories.Count > 0;

            var all = new RadioButton { Content = "Все категории (по одной на отчёт)", IsChecked = _category == null, Margin = new Thickness(0, 0, 0, 8), FontWeight = FontWeights.SemiBold };
            all.Tag = null;
            all.Checked += (s, e) => _category = null;
            panel.Children.Add(all);

            if (hasCategories)
            {
                foreach (var cat in categories)
                {
                    var rb = new RadioButton { Content = cat, Margin = new Thickness(0, 0, 0, 8), IsChecked = _category == cat };
                    var c = cat;
                    rb.Checked += (s, e) => _category = c;
                    panel.Children.Add(rb);
                }
            }

            panel.Children.Add(new TextBlock
            {
                Text = hasCategories
                    ? "Категории определены по загруженным файлам. Можно сформировать отчёт по каждой отдельно."
                    : "Категории не определены — будет выбран режим по умолчанию.",
                Foreground = System.Windows.Media.Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(26, 6, 0, 0),
                FontSize = 12
            });
            return panel;
        }

        // ---------- шаг 3: содержимое ----------
        private UIElement StepContents()
        {
            var panel = new StackPanel();
            var charts = new CheckBox { Content = "Графики и диаграммы", IsChecked = _showCharts, Margin = new Thickness(0, 0, 0, 8), FontWeight = FontWeights.SemiBold };
            var stats = new CheckBox { Content = "Лист «Статистика» с итогами", IsChecked = _showStats, Margin = new Thickness(0, 0, 0, 8) };
            var comments = new CheckBox { Content = "Комментарии к обращениям", IsChecked = _showComments, Margin = new Thickness(0, 0, 0, 8) };

            charts.Checked += (s, e) => _showCharts = true;  charts.Unchecked += (s, e) => _showCharts = false;
            stats.Checked += (s, e) => _showStats = true;    stats.Unchecked += (s, e) => _showStats = false;
            comments.Checked += (s, e) => _showComments = true; comments.Unchecked += (s, e) => _showComments = false;

            panel.Children.Add(charts);
            panel.Children.Add(stats);
            panel.Children.Add(comments);

            panel.Children.Add(new TextBlock
            {
                Text = "Всё это можно изменить в предпросмотре перед сохранением.",
                Foreground = System.Windows.Media.Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(26, 6, 0, 0),
                FontSize = 12
            });
            return panel;
        }

        // ---------- шаг 4: оформление ----------
        private UIElement StepTheme()
        {
            var panel = new StackPanel();
            var themes = new[] { "Синяя", "Зелёная", "Графит", "Светлая" };
            foreach (var t in themes)
            {
                var rb = new RadioButton { Content = t, Margin = new Thickness(0, 0, 0, 8), IsChecked = _theme == t, FontWeight = FontWeights.SemiBold };
                var clone = t;
                rb.Checked += (s, e) => _theme = clone;
                panel.Children.Add(rb);
            }

            panel.Children.Add(new TextBlock
            {
                Text = "Тема применяется к таблице, статистике и графикам. Потом можно переключить в предпросмотре.",
                Foreground = System.Windows.Media.Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(26, 6, 0, 0),
                FontSize = 12
            });
            return panel;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_step > 0) ShowStep(_step - 1);
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_step < StepCount - 1)
            {
                ShowStep(_step + 1);
            }
            else
            {
                // применяем выбор
                _main.ApplyWizard(_template, _category, _showCharts, _showStats, _showComments, _theme);
                Close();
            }
        }
    }
}

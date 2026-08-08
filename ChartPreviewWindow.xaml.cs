using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using ObrasReport.Core;

namespace ObrasReport
{
    public partial class ChartPreviewWindow : Window
    {
        public bool SaveConfirmed { get; private set; }

        /// <param name="subtitle">Строка под заголовком (категория, периоды, описание).</param>
        public ChartPreviewWindow(string subtitle, List<ChartImage> charts, string warning = null,
            string heading = null, string saveButtonText = null)
        {
            InitializeComponent();

            if (!string.IsNullOrWhiteSpace(heading))
            {
                Title = heading;
                HeadingText.Text = heading;
            }

            SubtitleText.Text = string.IsNullOrWhiteSpace(subtitle) ? "" : subtitle;

            if (!string.IsNullOrWhiteSpace(saveButtonText))
                SaveButton.Content = saveButtonText;

            if (!string.IsNullOrWhiteSpace(warning))
            {
                WarningText.Text = warning;
                WarningText.Visibility = Visibility.Visible;
            }

            if (charts == null || charts.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(warning))
                {
                    WarningText.Text = "Графики и диаграммы не удалось построить. Таблицы отчёта можно сохранить без листа «Графики».";
                    WarningText.Visibility = Visibility.Visible;
                }
                ChartsList.ItemsSource = null;
            }
            else
            {
                ChartsList.ItemsSource = charts.Select(c => new ChartItem
                {
                    Title = c.Title,
                    Image = ToBitmap(c.Png)
                }).ToList();
            }
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

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            SaveConfirmed = false;
            DialogResult = false;
            Close();
        }

        private class ChartItem
        {
            public string Title { get; set; }
            public BitmapImage Image { get; set; }
        }
    }
}

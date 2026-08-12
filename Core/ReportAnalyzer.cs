using System.Collections.Generic;
using System.Linq;
using ObrasReport.Models;

namespace ObrasReport.Core
{
    /// <summary>Лёгкое описание одного загруженного файла для анализатора (без привязки к UI).</summary>
    public class AnalyzerInput
    {
        public string FileName;
        public LayoutType Layout;
        public int RecordCount;      // обращений (или строк таблицы)
        public bool IsTrend;         // распознан как «закрытые обращения/наряды»
        public bool HasTable;        // универсальный разбор дал колонки
        public string Category;      // выбранная пользователем категория (может быть null)
    }

    /// <summary>Результат анализа загруженных данных и рекомендация по отчёту.</summary>
    public class AnalysisResult
    {
        public ReportTemplate RecommendedTemplate;
        public string Summary;                 // что программа определила
        public string Recommendation;          // что рекомендуется сделать
        public List<string> Warnings = new List<string>();
        public List<string> DetectedCategories = new List<string>();
        public int FileCount;
        public bool HasRepairs, HasColored, HasTrend, HasUniversal;
    }

    /// <summary>
    /// «Умный» анализатор: сам понимает, что загружено, и предлагает оптимальный
    /// шаблон отчёта, категории и предупреждения. Используется мастером и предпросмотром.
    /// </summary>
    public static class ReportAnalyzer
    {
        public static AnalysisResult Analyze(IReadOnlyList<AnalyzerInput> items)
        {
            var r = new AnalysisResult { FileCount = items.Count };

            if (items.Count == 0)
            {
                r.Summary = "Файлы не загружены.";
                r.Recommendation = "Добавьте две и более выгрузки Excel, чтобы программа подсказала оптимальный отчёт.";
                return r;
            }

            r.HasRepairs = items.Any(i => i.Layout == LayoutType.Repairs);
            r.HasColored = items.Any(i => i.Layout == LayoutType.Colored);
            r.HasTrend = items.All(i => i.IsTrend);
            r.HasUniversal = items.All(i => i.HasTable);

            // категории (для режима обращений)
            r.DetectedCategories = items
                .Where(i => !string.IsNullOrWhiteSpace(i.Category))
                .Select(i => i.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            // ---- рекомендация шаблона ----
            if (r.HasTrend && items.Count >= 2)
            {
                r.RecommendedTemplate = ReportTemplate.ClosedTrend;
                r.Summary = "Все файлы распознаны как выгрузки «закрытые обращения/наряды» со счётчиками.";
                r.Recommendation = "Рекомендуется режим «Динамика» — покажет прогресс закрытия обращений и нарядов по периодам.";
            }
            else if (r.HasRepairs || r.HasColored)
            {
                r.RecommendedTemplate = ReportTemplate.Obrasheniya;
                r.Summary = "Определён формат выгрузок обращений 1С" +
                            (r.HasRepairs ? " («по ремонтам»)" : "") +
                            (r.HasColored ? " («не по ремонту»)" : "") + ".";
                r.Recommendation = "Рекомендуется режим «Обращения» — классификация «Обработано / На контроле исполнения» по категориям.";
            }
            else if (r.HasUniversal)
            {
                r.RecommendedTemplate = ReportTemplate.Universal;
                r.Summary = "Файлы не похожи на реестр обращений 1С, но читаются как таблицы.";
                r.Recommendation = "Рекомендуется «Универсальное сравнение» — сопоставление строк по ключевому столбцу.";
            }
            else
            {
                r.RecommendedTemplate = ReportTemplate.Universal;
                r.Summary = "Формат файлов не удалось однозначно определить.";
                r.Recommendation = "Попробуйте «Универсальное сравнение» или укажите шаблон вручную.";
            }

            // ---- предупреждения ----
            if (items.Count < 2)
                r.Warnings.Add("Загружен только 1 файл — для сравнения нужно минимум 2.");

            if (r.HasRepairs && r.HasColored)
                r.Warnings.Add("Смешаны выгрузки «по ремонтам» и «не по ремонту». Отчёт строится по одной категории за раз.");

            if (r.DetectedCategories.Count > 1)
                r.Warnings.Add($"Обнаружено несколько категорий: {string.Join(", ", r.DetectedCategories)}. Будет сформирован отдельный отчёт на каждую.");

            if (items.Any(i => i.RecordCount == 0))
                r.Warnings.Add("Некоторые файлы не содержат распознанных обращений/строк — проверьте формат.");

            return r;
        }
    }
}

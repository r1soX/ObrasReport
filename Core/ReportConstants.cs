using ObrasReport.Models;

namespace ObrasReport.Core
{
    /// <summary>
    /// Централизованные константы: итоги, категории, критичность, подписи колонок.
    /// Убирает «магические строки», разбросанные по движку, парсерам и UI.
    /// </summary>
    public static class ReportConstants
    {
        // ---- итоги классификации ----
        public const string ItogDone = "Обработано";
        public const string ItogControl = "На контроле исполнения";

        // ---- категории отчёта ----
        public const string CatRepairs = "По ремонту";
        public const string CatNonRepair = "Не по ремонту";
        public const string CatVideo = "Видеонаблюдение";

        // ---- критичность (ремонты) ----
        public const string SeverityBlack = "Чёрная";
        public const string SeverityRed = "Красная";
        public const string SeverityYellow = "Жёлтая";

        // ---- подписи колонок выгрузок (RegistryParser / UniversalParser) ----
        public const string ColResponsible = "Ответственный";
        public const string ColObrashchenie = "Обращение";
        public const string ColClient = "Клиент";
        public const string ColFilial = "Филиал";
        public const string ColClassifier = "Классификатор";
        public const string ColKe = "КЕ";
        public const string ColConfigUnit = "Конфигурационная единица";
        public const string ColService = "Услуга";
        public const string ColStatus = "Состояние";
        public const string ColStatusDays = "Состояние дней";
        public const string ColStatusDate = "Обращение.Дата последнего изменения";
        public const string ColYellow = "Жёлтая";
        public const string ColBlack = "Чёрная";

        // ---- служебные строки реестра 1С (колонка №1) ----
        public static readonly System.Collections.Generic.HashSet<string> RegistryHeaders =
            new System.Collections.Generic.HashSet<string>
            {
                "Реестр обращений", "Параметры:", ColResponsible, ColObrashchenie, "Ссылка", "Итого"
            };

        public static string CategoryLabel(LayoutType layout) =>
            layout == LayoutType.Repairs ? CatRepairs : CatNonRepair;

        public static string LayoutText(LayoutType t) =>
            t == LayoutType.Repairs ? "По ремонтам"
            : t == LayoutType.Colored ? "Не по ремонту"
            : "Формат не определён";
    }
}
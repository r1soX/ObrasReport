using System;
using System.Collections.Generic;

namespace ObrasReport.Models
{
    /// <summary>Вид отчёта (шаблон), выбираемый пользователем.</summary>
    public enum ReportTemplate
    {
        Auto = 0,        // определить автоматически
        Obrasheniya,     // обращения 1С (текущая логика Обработано/На контроле)
        Universal,       // универсальное нейтральное сравнение любой таблицы
        ClosedTrend      // динамика закрытых обращений/нарядов по периодам
    }

    /// <summary>Счётчики по ответственному (закрытые обращения/наряды).</summary>
    public class TrendMetrics
    {
        public int ObrTotal, ObrClosed, NarTotal, NarClosed;
    }

    /// <summary>Один период (файл) для отчёта динамики.</summary>
    public class TrendSnapshot
    {
        public string FilePath;
        public string FileName;
        public string Label;
        public DateTime Date;
        public Dictionary<string, TrendMetrics> ByResp = new Dictionary<string, TrendMetrics>();
    }

    /// <summary>Строка отчёта динамики (по одному ответственному).</summary>
    public class TrendRow
    {
        public int Index;
        public string Responsible;
        public List<int> ObrClosed = new List<int>(); // по периодам
        public List<int> NarClosed = new List<int>();
        public int ObrDelta, NarDelta;                // тренд: последний − первый
    }

    /// <summary>Модель отчёта динамики.</summary>
    public class TrendReportModel
    {
        public string Title;
        public string Description;
        public List<string> DateLabels = new List<string>();
        public List<TrendRow> Rows = new List<TrendRow>();
        public List<int> TotalObrClosed = new List<int>(); // сумма по всем, по периодам
        public List<int> TotalNarClosed = new List<int>();
        public int TotalObrDelta, TotalNarDelta;
    }

    /// <summary>Колонка универсальной таблицы: позиция + название из заголовка.</summary>
    public class ColumnInfo
    {
        public int Pos { get; set; }      // индекс колонки (1-based)
        public string Name { get; set; }  // подпись из строки заголовков
        public override string ToString() => Name;
    }

    /// <summary>Произвольная таблица из любой книги Excel (для универсального режима).</summary>
    public class UniversalTable
    {
        public string FilePath;
        public string FileName;
        public List<ColumnInfo> Columns = new List<ColumnInfo>();
        public List<Dictionary<int, string>> DataRows = new List<Dictionary<int, string>>();
    }

    /// <summary>Строка универсального (нейтрального) отчёта.</summary>
    public class UniversalRow
    {
        public int Index;
        public string Key;
        public List<string> DisplayValues = new List<string>(); // по выбранным колонкам
        public List<string> TrackedByDate = new List<string>(); // по датам
        public string Itog;   // Добавлено / Удалено / Изменено / Без изменений
        public string Kind;   // added / removed / changed / same
    }

    /// <summary>Готовая модель универсального отчёта.</summary>
    public class UniversalReportModel
    {
        public string Title;
        public string Description;
        public List<string> DateLabels = new List<string>();
        public string KeyHeader;
        public string TrackedHeader;
        public List<string> DisplayHeaders = new List<string>();
        public List<UniversalRow> Rows = new List<UniversalRow>();
        public int Added, Removed, Changed, Same;
    }

    /// <summary>Тип реестра (определяется автоматически по колонкам).</summary>
    public enum LayoutType
    {
        Unknown = 0,
        /// <summary>«По ремонтам»: Классификатор / КЕ / Филиал / Состояние / Состояние дней + критичность.</summary>
        Repairs,
        /// <summary>«Не по ремонту (цветные)»: Клиент / Состояние / Конфигурационная единица.</summary>
        Colored
    }

    /// <summary>Одно обращение внутри одной выгрузки.</summary>
    public class ObrRecord
    {
        public string Number;        // № обращения (с ведущими нулями)
        public string Responsible;   // Ответственный (заголовок группы)
        public string Status;        // Состояние
        public string ObjectName;    // Филиал (ремонты) или Клиент (цветные)
        public string Classifier;    // Классификатор (ремонты)
        public string ConfigUnit;    // КЕ / Конфигурационная единица
        public string Severity;      // Чёрная / Красная / Жёлтая (ремонты)
        public string Days;          // Состояние дней (ремонты)
        public string Service;       // Услуга (видеонаблюдение)
    }

    /// <summary>Выгрузка (один файл на определённую дату).</summary>
    public class Snapshot
    {
        public string FilePath;
        public string FileName;
        public string Label;                 // короткая метка даты, напр. «01.07»
        public DateTime SortDate;            // для сортировки
        public bool DateDetected;            // дата распознана из имени файла
        public LayoutType Layout;
        public Dictionary<string, ObrRecord> Records = new Dictionary<string, ObrRecord>();
        public int RawRowCount;              // строк-обращений с учётом дублей позиций

        public override string ToString() => $"{Label} — {FileName}";
    }

    /// <summary>Строка итоговой таблицы отчёта.</summary>
    public class ReportRow
    {
        public int Index;
        public string Number;
        public string Responsible;
        public string ObjectName;
        public string Classifier;
        public string Severity;
        public string Days;
        public string Service;   // Услуга (видеонаблюдение)
        public List<string> StatusByDate = new List<string>(); // выровнено по порядку выгрузок
        public string Itog;      // «Обработано» / «На контроле исполнения»
        public string Comment;
        public bool Processed;   // для подсветки
        public bool Closed;      // обращение отсутствует в последней выгрузке
    }

    /// <summary>Статистика по одной дате.</summary>
    public class DateStat
    {
        public string Label;
        public int TotalUnique;
        public int Black, Red, Yellow;
    }

    /// <summary>Готовая модель отчёта.</summary>
    public class ReportModel
    {
        public LayoutType Layout;
        public string CategoryLabel; // категория (для заголовка и имени файла)
        public string Description;   // произвольное описание отчёта (задаёт пользователь)
        public bool HasService;      // есть ли данные «Услуга» (видеонаблюдение)
        public List<Snapshot> Snapshots = new List<Snapshot>();
        public List<ReportRow> Rows = new List<ReportRow>();
        public List<DateStat> DateStats = new List<DateStat>();

        // сводные показатели
        public int ProcessedTotal;
        public int ClosedTotal;          // отсутствуют в последней выгрузке
        public int OnControlTotal;
        public int OnControlExternal;   // из них во «внешних» состояниях (ремонты)

        // между соседними выгрузками (индекс i = переход i -> i+1)
        public List<int> LeftCounts = new List<int>();
        public List<int> NewCounts = new List<int>();
        public List<int> ChangedCounts = new List<int>();
    }
}

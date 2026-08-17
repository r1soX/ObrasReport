using System;
using System.Collections.Generic;
using System.Linq;
using ObrasReport.Models;

namespace ObrasReport.Core
{
    /// <summary>Сопоставляет выгрузки по № обращения и формирует модель отчёта.</summary>
    public static class ReportEngine
    {
        // состояния «по ремонтам», движение которых обеспечивают внешние стороны
        // (согласования, закупы, финансы, сервисный центр, ИТ-отдел, вторая линия)
        private static readonly Dictionary<string, string> ExternalStates = new Dictionary<string, string>
        {
            { "Согласование списания", "ожидает согласования списания" },
            { "Списание согласовано", "списание согласовано, ожидает дальнейшей обработки" },
            { "Списание отклонено", "по списанию принято решение" },
            { "Требуется передача в ИТ отдел", "ожидает передачи в ИТ-отдел" },
            { "Передано в ИТ отдел", "передано в ИТ-отдел" },
            { "Передано в СЦ", "передано в сервисный центр" },
            { "В ремонте в СЦ", "находится в ремонте в сервисном центре" },
            { "Передано на вторую линию", "передано на вторую линию" },
            { "Закуп оборудования", "ожидает закупа оборудования" },
            { "Закуп расходных материалов", "ожидает закупа расходных материалов" },
            { "Закуп комплектующих", "ожидает закупа комплектующих" },
            { "Согласование закупа запчастей", "ожидает согласования закупа запчастей" },
            { "Закуп запчастей согласован", "закуп запчастей согласован" },
            { "Согласование ремонта", "ожидает согласования ремонта" },
            { "Стоимость ремонта согласована", "стоимость согласована, ожидает оплаты/ремонта" },
            { "Стоимость ремонта не согласована", "ожидает согласования стоимости ремонта" },
            { "На согласовании", "находится на согласовании" },
            { "Запуск счета на оплату", "ожидает оплаты по счёту" },
            { "Ожидание выдачи подменного оборудования", "ожидает выдачи подменного оборудования" },
            { "Возвращено", "возвращено, ожидает уточнения инициатора" },
        };

        private const string ITOG_DONE = ReportConstants.ItogDone;
        private const string ITOG_CLOSED = ReportConstants.ItogClosed;
        private const string ITOG_CONTROL = ReportConstants.ItogControl;

        public static ReportModel Build(IEnumerable<Snapshot> snapshotsInput)
        {
            var snaps = snapshotsInput
                .OrderBy(s => s.SortDate)
                .ThenBy(s => s.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (snaps.Count < 2)
                throw new InvalidOperationException("Для сравнения требуется не менее двух выгрузок.");

            var layout = snaps[0].Layout;
            if (snaps.Any(s => s.Layout != layout))
                throw new InvalidOperationException(
                    "Загружены выгрузки разного формата (по ремонтам и не по ремонту). " +
                    "Сформируйте отчёт по одной категории за раз.");

            var model = new ReportModel { Layout = layout, Snapshots = snaps };

            // статистика по датам
            foreach (var s in snaps)
            {
                model.DateStats.Add(new DateStat
                {
                    Label = s.Label,
                    TotalUnique = s.Records.Count,
                    Black = s.Records.Values.Count(r => r.Severity == "Чёрная"),
                    Red = s.Records.Values.Count(r => r.Severity == "Красная"),
                    Yellow = s.Records.Values.Count(r => r.Severity == "Жёлтая"),
                });
            }

            // переходы между соседними выгрузками
            for (int i = 0; i < snaps.Count - 1; i++)
            {
                var a = snaps[i].Records;
                var b = snaps[i + 1].Records;
                model.LeftCounts.Add(a.Keys.Count(k => !b.ContainsKey(k)));
                model.NewCounts.Add(b.Keys.Count(k => !a.ContainsKey(k)));
                model.ChangedCounts.Add(a.Keys.Count(k => b.ContainsKey(k) &&
                    !string.Equals(a[k].Status, b[k].Status, StringComparison.Ordinal)));
            }

            var allNumbers = new SortedSet<string>();
            foreach (var s in snaps) foreach (var n in s.Records.Keys) allNumbers.Add(n);

            int idx = 0;
            foreach (var num in allNumbers)
            {
                idx++;
                var presentIdx = Enumerable.Range(0, snaps.Count)
                    .Where(i => snaps[i].Records.ContainsKey(num)).ToList();
                var firstRec = snaps[presentIdx.First()].Records[num];
                var lastRec = snaps[presentIdx.Last()].Records[num];

                bool inFirst = snaps[0].Records.ContainsKey(num);
                bool inLast = snaps[snaps.Count - 1].Records.ContainsKey(num);

                var trajectory = presentIdx.Select(i => snaps[i].Records[num].Status).ToList();
                bool statusChanged = trajectory.Distinct().Count() > 1;

                var respTrail = presentIdx.Select(i => snaps[i].Records[num].Responsible).ToList();
                bool respChanged = respTrail.Distinct().Count() > 1;

                var row = new ReportRow
                {
                    Index = idx,
                    Number = num,
                    Responsible = lastRec.Responsible,
                    ObjectName = firstRec.ObjectName,
                    Classifier = lastRec.Classifier,
                    Severity = string.IsNullOrEmpty(lastRec.Severity) ? "—" : lastRec.Severity,
                    Days = inLast ? lastRec.Days : "",
                    Service = lastRec.Service,
                    Closed = !inLast,
                    StatusByDate = snaps.Select(s => s.Records.TryGetValue(num, out var r) ? r.Status : "—нет—").ToList(),
                };

                if (layout == LayoutType.Repairs)
                    ClassifyRepairs(row, inLast, statusChanged, trajectory, lastRec);
                else
                    ClassifyColored(row, snaps, num, inFirst, inLast, statusChanged, trajectory);

                if (respChanged)
                    row.Comment += $" Ответственный переназначен: {respTrail.First()} → {respTrail.Last()}.";

                model.Rows.Add(row);

                if (row.Closed) model.ClosedTotal++;
                else if (row.Processed) model.ProcessedTotal++;
                else
                {
                    model.OnControlTotal++;
                    if (layout == LayoutType.Repairs && IsExternalState(lastRec.Status))
                        model.OnControlExternal++;
                }
            }

            model.HasService = model.Rows.Any(r => !string.IsNullOrWhiteSpace(r.Service));
            return model;
        }

        private static void ClassifyRepairs(ReportRow row, bool inLast, bool statusChanged,
            List<string> trajectory, ObrRecord lastRec)
        {
            if (!inLast)
            {
                row.Itog = ITOG_CLOSED; row.Processed = true;
                row.Comment = "Обращение закрыто — отсутствует в последней выгрузке; ремонт завершён / обращение закрыто по результатам контроля исполнения.";
            }
            else if (statusChanged)
            {
                row.Itog = ITOG_DONE; row.Processed = true;
                row.Comment = $"Зафиксировано изменение состояния: {string.Join(" → ", Distinct(trajectory))} — по обращению велась работа.";
            }
            else
            {
                row.Itog = ITOG_CONTROL; row.Processed = false;
                if (ExternalStates.TryGetValue(lastRec.Status, out var reason))
                    row.Comment = $"Обращение на контроле исполнения. Состояние «{lastRec.Status}» без изменений — {reason}.";
                else
                    row.Comment = $"Обращение на контроле исполнения. Состояние «{lastRec.Status}» без изменений за период.";
            }
        }

        private static void ClassifyColored(ReportRow row, List<Snapshot> snaps, string num,
            bool inFirst, bool inLast, bool statusChanged, List<string> trajectory)
        {
            if (!inLast)
            {
                row.Itog = ITOG_CLOSED; row.Processed = true;
                row.Comment = "Обращение закрыто — отсутствует в последней выгрузке; обработано ответственным сотрудником по результатам контроля исполнения.";
            }
            else
            {
                row.Itog = ITOG_CONTROL; row.Processed = false;
                int firstSeen = Enumerable.Range(0, snaps.Count).First(i => snaps[i].Records.ContainsKey(num));
                if (!inFirst)
                    row.Comment = $"Новое обращение, поступившее в выгрузке от {snaps[firstSeen].Label}; принято на контроль исполнения.";
                else if (statusChanged)
                    row.Comment = $"Обращение на контроле исполнения; изменение статуса: {string.Join(" → ", Distinct(trajectory))}.";
                else
                    row.Comment = "Обращение остаётся на контроле исполнения; требуется дальнейший контроль.";
            }
        }

        public static bool IsExternalState(string status) => ExternalStates.ContainsKey(status);

        private static IEnumerable<string> Distinct(IEnumerable<string> src)
        {
            string prev = null;
            foreach (var s in src) { if (s != prev) yield return s; prev = s; }
        }
    }
}

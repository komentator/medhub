using ClosedXML.Excel;
using MedControl.Models;
using MedControl.Repositories;
using MedControl.Logging;

namespace MedControl.ImportExport;

// §24. Экспорт/импорт .xlsx через ClosedXML, без дублей.
public sealed class ExcelService
{
    private readonly MeasurementRepository _meas;
    private readonly IntakeRepository _intakes;
    private readonly WellBeingRepository _wb;
    private readonly LogService _log;
    public ExcelService(MeasurementRepository meas, IntakeRepository intakes, WellBeingRepository wb, LogService log)
    { _meas = meas; _intakes = intakes; _wb = wb; _log = log; }

    public void Export(string file, DateTime from, DateTime to, bool meas, bool intakes, bool wb, bool summary)
    {
        using var book = new XLWorkbook();
        if (meas)
        {
            var ws = book.Worksheets.Add("Измерения");
            ws.Cell(1, 1).Value = "Дата"; ws.Cell(1, 2).Value = "Время"; ws.Cell(1, 3).Value = "Тонометр";
            ws.Cell(1, 4).Value = "Верхнее"; ws.Cell(1, 5).Value = "Нижнее"; ws.Cell(1, 6).Value = "Пульс";
            ws.Cell(1, 7).Value = "Самочувствие"; ws.Cell(1, 8).Value = "Комментарий";
            var rows = _meas.GetRange(from, to);
            var scores = _wb.GetScoresByMeasurement(rows.Select(r => r.Id));
            int r2 = 2;
            foreach (var m in rows)
            {
                ws.Cell(r2, 1).Value = m.TakenAt.ToString("yyyy-MM-dd");
                ws.Cell(r2, 2).Value = m.TakenAt.ToString("HH:mm");
                ws.Cell(r2, 3).Value = m.TonometerName;
                ws.Cell(r2, 4).Value = m.Systolic; ws.Cell(r2, 5).Value = m.Diastolic; ws.Cell(r2, 6).Value = m.Pulse;
                ws.Cell(r2, 7).Value = scores.TryGetValue(m.Id, out var s) ? string.Join(",", s) : "";
                ws.Cell(r2, 8).Value = m.Comment; r2++;
            }
        }
        if (intakes)
        {
            var ws = book.Worksheets.Add("Приём лекарств");
            string[] h = ["Лекарство", "Назначение", "Дозировка", "Запланировано", "Фактически", "Статус", "Способ", "Самочувствие", "Комментарий"];
            for (int i = 0; i < h.Length; i++) ws.Cell(1, i + 1).Value = h[i];
            var rows = _intakes.GetRange(from, to);
            var scores = _wb.GetScoresByIntake(rows.Select(r => r.Id));
            int r2 = 2;
            foreach (var m in rows)
            {
                ws.Cell(r2, 1).Value = m.MedicationName; ws.Cell(r2, 2).Value = m.MedicationDescription;
                ws.Cell(r2, 3).Value = m.Dosage; ws.Cell(r2, 4).Value = m.ScheduledAt.ToString("yyyy-MM-dd HH:mm");
                ws.Cell(r2, 5).Value = m.ActualAt?.ToString("yyyy-MM-dd HH:mm") ?? "";
                ws.Cell(r2, 6).Value = m.Status.ToString(); ws.Cell(r2, 7).Value = m.ConfirmMethod.ToString();
                ws.Cell(r2, 8).Value = scores.TryGetValue(m.Id, out var s) ? string.Join(",", s) : "";
                ws.Cell(r2, 9).Value = m.Comment; r2++;
            }
        }
        if (wb)
        {
            var ws = book.Worksheets.Add("Самочувствие");
            ws.Cell(1, 1).Value = "Дата"; ws.Cell(1, 2).Value = "Время"; ws.Cell(1, 3).Value = "Оценка"; ws.Cell(1, 4).Value = "Комментарий";
            int r2 = 2;
            foreach (var w in _wb.GetRange(from, to))
            {
                ws.Cell(r2, 1).Value = w.CreatedAt.ToString("yyyy-MM-dd");
                ws.Cell(r2, 2).Value = w.CreatedAt.ToString("HH:mm");
                ws.Cell(r2, 3).Value = w.Score; ws.Cell(r2, 4).Value = w.Comment; r2++;
            }
        }
        if (summary)
        {
            var ws = book.Worksheets.Add("Сводка");
            ws.Cell(1, 1).Value = "Период"; ws.Cell(1, 2).Value = $"{from:yyyy-MM-dd} — {to:yyyy-MM-dd}";
            var ms = _meas.GetRange(from, to);
            var st = Services.StatisticsService.Pressure(ms);
            ws.Cell(2, 1).Value = "Среднее верхнее"; ws.Cell(2, 2).Value = st.AvgSys ?? 0;
            ws.Cell(3, 1).Value = "Среднее нижнее"; ws.Cell(3, 2).Value = st.AvgDia ?? 0;
            ws.Cell(4, 1).Value = "Средний пульс"; ws.Cell(4, 2).Value = st.AvgPulse ?? 0;
            ws.Cell(5, 1).Value = "Измерений"; ws.Cell(5, 2).Value = st.Count;
            var wst = Services.StatisticsService.WellBeing(_wb.GetRange(from, to));
            ws.Cell(6, 1).Value = "Среднее самочувствие"; ws.Cell(6, 2).Value = wst.Avg ?? 0;
            ws.Cell(7, 1).Value = "Оценок"; ws.Cell(7, 2).Value = wst.Count;
        }
        book.SaveAs(file);
    }

    // Импорт: пропускать дубликаты (§24).
    public (int Meas, int Intakes) Import(string file)
    {
        int nm = 0;
        try
        {
            using var book = new XLWorkbook(file);
            if (book.TryGetWorksheet("Измерения", out var ws))
            {
                var existing = _meas.GetRange(new DateTime(2000, 1, 1), DateTime.Now.AddDays(1))
                    .Select(m => DedupHelper.MeasurementKey(m.TakenAt, m.TonometerName, m.Systolic, m.Diastolic, m.Pulse)).ToHashSet();
                var tonos = _meas.GetTonometers().ToDictionary(t => t.Name.ToLowerInvariant(), t => t);
                foreach (var row in ws.RowsUsed().Skip(1))
                {
                    try
                    {
                        var taken = DateTime.Parse(row.Cell(1).GetString() + " " + row.Cell(2).GetString());
                        var tname = row.Cell(3).GetString();
                        int s = (int)row.Cell(4).GetDouble(), d = (int)row.Cell(5).GetDouble(), p = (int)row.Cell(6).GetDouble();
                        var key = DedupHelper.MeasurementKey(taken, tname, s, d, p);
                        if (!existing.Add(key)) continue;
                        if (!tonos.TryGetValue(tname.ToLowerInvariant(), out var t))
                        {
                            t = new Tonometer { Name = tname };
                            _meas.SaveTonometer(t);
                            tonos[tname.ToLowerInvariant()] = t;
                        }
                        _meas.Upsert(new Measurement
                        {
                            TonometerId = t.Id, TonometerName = t.Name, TakenAt = taken,
                            Systolic = s, Diastolic = d, Pulse = p, Comment = row.Cell(8).GetString(),
                            CreatedAt = taken,
                        });
                        nm++;
                    }
                    catch (Exception ex) { _log.Write("Import", ex.Message); }
                }
            }
        }
        catch (Exception ex) { _log.Write("Import", ex.Message); throw; }
        return (nm, 0);
    }
}

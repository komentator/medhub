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

    // Импорт исторических замеров: пропускать дубликаты (§24).
    // Возвращает (импортировано, пропущено): пропущены дубликаты и битые строки.
    public (int Imported, int Skipped) Import(string file)
    {
        int nm = 0, skip = 0;
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
                        if (!TryParseDateTime(row.Cell(1), row.Cell(2), out var taken)) { skip++; continue; }
                        var tname = (row.Cell(3).GetString() ?? "").Trim();
                        if (string.IsNullOrEmpty(tname)) { skip++; continue; }
                        if (!TryParseInt(row.Cell(4), out int s) || !TryParseInt(row.Cell(5), out int d) || !TryParseInt(row.Cell(6), out int p)) { skip++; continue; }
                        var (ok1, _) = Services.ValidationService.ValidatePressure(s, d);
                        var (ok2, _) = Services.ValidationService.ValidatePulse(p);
                        if (!ok1 || !ok2) { skip++; continue; }
                        var key = DedupHelper.MeasurementKey(taken, tname, s, d, p);
                        if (!existing.Add(key)) { skip++; continue; } // дубликат
                        if (!tonos.TryGetValue(tname.ToLowerInvariant(), out var t))
                        {
                            if (tonos.Count >= 10) { skip++; continue; } // лимит §15
                            var (okt, _) = Services.ValidationService.ValidateTonometer(tname);
                            if (!okt) { skip++; continue; }
                            t = new Tonometer { Name = tname };
                            _meas.SaveTonometer(t);
                            tonos[tname.ToLowerInvariant()] = t;
                        }
                        _meas.Upsert(new Measurement
                        {
                            TonometerId = t.Id, TonometerName = t.Name, TakenAt = taken,
                            Systolic = s, Diastolic = d, Pulse = p, Comment = row.Cell(8).GetString() ?? "",
                            CreatedAt = taken,
                        });
                        nm++;
                    }
                    catch (Exception ex) { _log.Write("Import", ex.Message); skip++; }
                }
            }
        }
        catch (Exception ex) { _log.Write("Import", ex.Message); throw; }
        return (nm, skip);
    }

    private static bool TryParseInt(IXLCell cell, out int v)
    {
        v = 0;
        try
        {
            if (cell.DataType == XLDataType.Number) { v = (int)cell.GetDouble(); return true; }
            var s = (cell.GetString() ?? "").Trim().Replace(',', '.');
            if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double d))
            {
                v = (int)d;
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    // Дата/время из двух колонок; понимаем даты Excel, "дд.ММ.гггг", "гггг-ММ-дд",
    // время "ЧЧ:мм" или датой с временем в первой колонке.
    private static bool TryParseDateTime(IXLCell dateCell, IXLCell timeCell, out DateTime taken)
    {
        taken = default;
        try
        {
            if (dateCell.DataType == XLDataType.DateTime)
            {
                taken = dateCell.GetDateTime();
                if (timeCell.DataType == XLDataType.DateTime)
                    taken = taken.Date + timeCell.GetDateTime().TimeOfDay;
                else if (TimeOnly.TryParse((timeCell.GetString() ?? "").Trim(), out var tt))
                    taken = taken.Date + tt.ToTimeSpan();
                else if (TimeSpan.TryParse((timeCell.GetString() ?? "").Trim(), out var ts))
                    taken = taken.Date + ts;
                return taken.Year >= 2000 && taken <= DateTime.Now.AddDays(1);
            }
            string ds = (dateCell.GetString() ?? "").Trim();
            string ts2 = (timeCell.GetString() ?? "").Trim();
            string[] formats = ["dd.MM.yyyy HH:mm", "dd.MM.yyyy H:mm", "yyyy-MM-dd HH:mm",
                "dd.MM.yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy"];
            if (DateTime.TryParseExact(ds + " " + ts2, formats,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out taken) ||
                DateTime.TryParseExact(ds, formats, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out taken))
            {
                if (taken.TimeOfDay == TimeSpan.Zero && TimeOnly.TryParse(ts2, out var tt2))
                    taken = taken.Date + tt2.ToTimeSpan();
                return taken.Year >= 2000 && taken <= DateTime.Now.AddDays(1);
            }
            return false;
        }
        catch { return false; }
    }

    // Шаблон для исторических замеров: лист с примером + лист-инструкция.
    public void CreateTemplate(string file)
    {
        using var book = new XLWorkbook();
        var ws = book.Worksheets.Add("Измерения");
        string[] h = ["Дата", "Время", "Тонометр", "Верхнее", "Нижнее", "Пульс", "Самочувствие", "Комментарий"];
        for (int i = 0; i < h.Length; i++) ws.Cell(1, i + 1).Value = h[i];
        ws.Cell(2, 1).Value = "01.10.2026"; ws.Cell(2, 2).Value = "08:00";
        ws.Cell(2, 3).Value = "Тонометр 1"; ws.Cell(2, 4).Value = 120;
        ws.Cell(2, 5).Value = 80; ws.Cell(2, 6).Value = 70;
        ws.Cell(3, 1).Value = "01.10.2026"; ws.Cell(3, 2).Value = "08:05";
        ws.Cell(3, 3).Value = "Тонометр 2"; ws.Cell(3, 4).Value = 118;
        ws.Cell(3, 5).Value = 78; ws.Cell(3, 6).Value = 72;
        var how = book.Worksheets.Add("Как заполнять");
        how.Cell(1, 1).Value = "Удалите строки-примеры и впишите свои замеры. " +
            "Дата: ДД.ММ.ГГГГ, время: ЧЧ:ММ. Тонометры создаются сами. " +
            "Дубликаты при повторном импорте пропускаются.";
        book.SaveAs(file);
    }
}

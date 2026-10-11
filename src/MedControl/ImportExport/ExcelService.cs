using System.IO;
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
    // Два формата: вертикальный («Дата|Время|Тонометр|…») и блочный дневник
    // («дата|время|верхнее|нижнее|пульс» × 2 тонометра). Определяется по шапке.
    // Возвращает (импортировано, пропущено): пропущены дубликаты и битые строки.
    public (int Imported, int Skipped) Import(string file)
    {
        try
        {
            using var book = new XLWorkbook(file);
            var ws = PickSheet(book);
            if (ws == null) return (0, 0);
            if (IsVertical(ws)) return ImportVertical(ws);
            if (IsDiary(ws)) return ImportDiary(ws);
            return (0, 0);
        }
        catch (Exception ex) { _log.Write("Import", ex.Message); throw; }
    }

    private static IXLWorksheet? PickSheet(XLWorkbook book)
    {
        if (book.TryGetWorksheet("Измерения", out var ws) && ws.RowsUsed().Any()) return ws;
        return book.Worksheets.FirstOrDefault(w => w.RowsUsed().Any());
    }

    private static IXLRow? HeaderRow(IXLWorksheet ws) => ws.RowsUsed().FirstOrDefault();

    private static bool IsVertical(IXLWorksheet ws)
    {
        var h = HeaderRow(ws);
        return h != null && (h.Cell(3).GetString() ?? "").Trim().Equals("Тонометр", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDiary(IXLWorksheet ws)
    {
        return ws.RowsUsed().Take(4).Any(r =>
            (r.Cell(1).GetString() ?? "").Trim().Equals("дата", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ImportCtx
    {
        public HashSet<string> Existing = new();
        public Dictionary<string, Tonometer> Tonos = new();
        public int Imported, Skipped;
    }

    private ImportCtx NewCtx() => new()
    {
        Existing = _meas.GetRange(new DateTime(2000, 1, 1), DateTime.Now.AddDays(1))
            .Select(m => DedupHelper.MeasurementKey(m.TakenAt, m.TonometerName, m.Systolic, m.Diastolic, m.Pulse)).ToHashSet(),
        Tonos = _meas.GetTonometers().ToDictionary(t => t.Name.ToLowerInvariant(), t => t),
    };

    private bool AddMeasurement(ImportCtx ctx, string tname, DateTime taken, int s, int d, int p, string comment)
    {
        var (ok1, _) = Services.ValidationService.ValidatePressure(s, d);
        var (ok2, _) = Services.ValidationService.ValidatePulse(p);
        if (!ok1 || !ok2) { ctx.Skipped++; return false; }
        tname = (tname ?? "").Trim();
        if (string.IsNullOrEmpty(tname)) { ctx.Skipped++; return false; }
        if (!ctx.Existing.Add(DedupHelper.MeasurementKey(taken, tname, s, d, p))) { ctx.Skipped++; return false; }
        if (!ctx.Tonos.TryGetValue(tname.ToLowerInvariant(), out var t))
        {
            if (ctx.Tonos.Count >= 10) { ctx.Skipped++; return false; } // лимит §15
            var (okt, _) = Services.ValidationService.ValidateTonometer(tname);
            if (!okt) { ctx.Skipped++; return false; }
            t = new Tonometer { Name = tname };
            _meas.SaveTonometer(t);
            ctx.Tonos[tname.ToLowerInvariant()] = t;
        }
        _meas.Upsert(new Measurement
        {
            TonometerId = t.Id, TonometerName = t.Name, TakenAt = taken,
            Systolic = s, Diastolic = d, Pulse = p, Comment = comment ?? "",
            CreatedAt = taken,
        });
        ctx.Imported++;
        return true;
    }

    private (int Imported, int Skipped) ImportVertical(IXLWorksheet ws)
    {
        var ctx = NewCtx();
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            try
            {
                if (!TryParseDateTime(row.Cell(1), row.Cell(2), out var taken)) { ctx.Skipped++; continue; }
                var tname = (row.Cell(3).GetString() ?? "").Trim();
                if (!TryParseInt(row.Cell(4), out int s) || !TryParseInt(row.Cell(5), out int d) || !TryParseInt(row.Cell(6), out int p)) { ctx.Skipped++; continue; }
                AddMeasurement(ctx, tname, taken, s, d, p, row.Cell(8).GetString() ?? "");
            }
            catch (Exception ex) { _log.Write("Import", ex.Message); ctx.Skipped++; }
        }
        return (ctx.Imported, ctx.Skipped);
    }

    // Блочный дневник: A=дата, B=время (общее для обоих), CDE=первый тонометр,
    // F пропускается, GHI=второй тонометр. Блоки привязаны к ПОРЯДКУ ДОБАВЛЕНИЯ
    // тонометров в программе (имена из файла игнорируются).
    // Время не распознано → 09:00, следующее без времени → 10:00 и т.д. (по дню).
    private (int Imported, int Skipped) ImportDiary(IXLWorksheet ws)
    {
        var rows = ws.RowsUsed().ToList();
        int h = rows.FindIndex(r => (r.Cell(1).GetString() ?? "").Trim().Equals("дата", StringComparison.OrdinalIgnoreCase));
        if (h < 0) return (0, 0);
        var data = rows.Skip(h + 1).ToList();
        if (data.Count == 0) return (0, 0);

        bool use1 = data.Any(r => Txt(r.Cell(3)) != "" || Txt(r.Cell(4)) != "" || Txt(r.Cell(5)) != "");
        bool use2 = data.Any(r => Txt(r.Cell(7)) != "" || Txt(r.Cell(8)) != "" || Txt(r.Cell(9)) != "");
        int need = (use1 ? 1 : 0) + (use2 ? 1 : 0);
        var tonos = _meas.GetTonometersByCreation();
        while (tonos.Count < need && tonos.Count < 10)
        {
            var t = new Tonometer { Name = $"Тонометр {tonos.Count + 1}" };
            _meas.SaveTonometer(t);
            tonos.Add(t);
        }
        string? n1 = use1 && tonos.Count > 0 ? tonos[0].Name : null;
        string? n2 = use2 && tonos.Count > 1 ? tonos[1].Name : null;

        var ctx = NewCtx();

        // Стартовый контекст дат: первая явная полная дата.
        int? ctxY = null, ctxM = null;
        foreach (var r in data)
        {
            if (IsDateCell(r.Cell(1))) { var dd = r.Cell(1).GetDateTime(); ctxY = dd.Year; ctxM = dd.Month; break; }
        }
        int? lastDay = null;
        DateTime? lastDate = null;
        var timeless = new Dictionary<DateOnly, int>(); // счётчик безвременных по дню

        foreach (var r in data)
        {
            try
            {
                string A = Txt(r.Cell(1)), B = Txt(r.Cell(2));
                bool hasVals = new[] { 3, 4, 5, 7, 8, 9 }.Any(c => Txt(r.Cell(c)) != "");
                if (A == "" && B == "" && !hasVals) continue; // пустая строка
                if (IsHeaderJunk(r)) continue; // строка-подпись полей («верхнее»…) — молча

                DateTime? date = ResolveDiaryDate(r.Cell(1), A, ref ctxY, ref ctxM, ref lastDay, lastDate);
                if (date == null)
                {
                    // дата в колонке времени?
                    if (IsDateCell(r.Cell(2)) && r.Cell(2).GetDateTime() is var bd && bd.Year is >= 2020 and <= 2027)
                    { date = bd.Date; ctxY = bd.Year; ctxM = bd.Month; lastDay = bd.Day; }
                    else if (lastDate != null) date = lastDate;
                    else { ctx.Skipped++; continue; }
                }
                lastDate = date;

                TimeOnly? time = null;
                if (IsDateCell(r.Cell(2)))
                {
                    var bt = r.Cell(2).GetDateTime();
                    if (bt.Year < 2000) time = TimeOnly.FromDateTime(bt); // время Excel
                }
                else if (TryTimeText(B, out var tt)) time = tt;
                if (time == null)
                {
                    // Время не определить: 09:00, следующее 10:00 и т.д. (по дню).
                    var day = DateOnly.FromDateTime(date.Value);
                    timeless.TryGetValue(day, out int k);
                    time = new TimeOnly((9 + k) % 24, 0);
                    timeless[day] = k + 1;
                }

                DateTime taken = date.Value.Date + time.Value.ToTimeSpan();
                if (n1 != null)
                {
                    if (TryVal(r.Cell(3), out int s1) && TryVal(r.Cell(4), out int d1) && TryVal(r.Cell(5), out int p1))
                        AddMeasurement(ctx, n1, taken, s1, d1, p1, "");
                    else if (Txt(r.Cell(3)) != "" || Txt(r.Cell(4)) != "" || Txt(r.Cell(5)) != "") ctx.Skipped++;
                }
                if (n2 != null)
                {
                    if (TryVal(r.Cell(7), out int s2) && TryVal(r.Cell(8), out int d2) && TryVal(r.Cell(9), out int p2))
                        AddMeasurement(ctx, n2, taken, s2, d2, p2, "");
                    else if (Txt(r.Cell(7)) != "" || Txt(r.Cell(8)) != "" || Txt(r.Cell(9)) != "") ctx.Skipped++;
                }
            }
            catch (Exception ex) { _log.Write("Import", ex.Message); ctx.Skipped++; }
        }
        return (ctx.Imported, ctx.Skipped);
    }

    // Строка из названий полей, а не данных.
    private static bool IsHeaderJunk(IXLRow r)
    {
        try
        {
            bool hasNum = new[] { 3, 4, 5, 7, 8, 9 }.Any(c =>
            {
                try { return r.Cell(c).DataType == XLDataType.Number; }
                catch { return false; }
            });
            if (hasNum) return false;
            string[] words = ["время", "верхнее", "нижнее", "пульс", "дата", "тонометр", "самочувствие", "комментарий"];
            return Enumerable.Range(1, 9).Any(c => words.Any(w => Txt(r.Cell(c)).ToLowerInvariant().Contains(w)));
        }
        catch { return false; }
    }

    private static string Txt(IXLCell c)
    {
        try { return (c.GetString() ?? "").Trim(); }
        catch { return ""; }
    }

    private static bool IsDateCell(IXLCell c)
    {
        try { return c.DataType == XLDataType.DateTime; }
        catch { return false; }
    }

    private static DateTime? ResolveDiaryDate(IXLCell cell, string text, ref int? ctxY, ref int? ctxM, ref int? lastDay, DateTime? lastDate)
    {
        if (IsDateCell(cell))
        {
            var full = cell.GetDateTime().Date;
            if (full.Year is < 2020 or > 2027) return null;
            ctxY = full.Year; ctxM = full.Month; lastDay = full.Day;
            return full;
        }
        if (IsWholeNumber(cell, out double nd) && nd >= 1 && nd <= 31)
        {
            if (ctxY == null || ctxM == null) return null;
            int d = (int)nd, y = ctxY.Value, m = ctxM.Value;
            if (lastDay != null && d < lastDay) { m++; if (m > 12) { m = 1; y++; } }
            try { ctxY = y; ctxM = m; lastDay = d; return new DateTime(y, m, d); }
            catch { return null; }
        }
        if (text != "")
        {
            var dm = System.Text.RegularExpressions.Regex.Match(text, @"^(\d{1,2})\D+(\d{1,2})$");
            if (dm.Success && int.TryParse(dm.Groups[1].Value, out int dd2) && int.TryParse(dm.Groups[2].Value, out int mm2)
                && ctxY != null && mm2 is >= 1 and <= 12 && dd2 is >= 1 and <= 31)
            {
                try { ctxM = mm2; lastDay = dd2; return new DateTime(ctxY.Value, mm2, dd2); }
                catch { return null; }
            }
            string[] f = ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "dd/MM/yyyy"];
            if (DateTime.TryParseExact(text, f, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var pd) && pd.Year is >= 2020 and <= 2027)
            {
                ctxY = pd.Year; ctxM = pd.Month; lastDay = pd.Day;
                return pd.Date;
            }
            return lastDate; // мусор — продолжение дня
        }
        return lastDate;
    }

    private static bool IsWholeNumber(IXLCell c, out double v)
    {
        v = 0;
        try
        {
            if (c.DataType == XLDataType.Number) { v = c.GetDouble(); return v == Math.Truncate(v); }
            return false;
        }
        catch { return false; }
    }

    private static bool TryTimeText(string s, out TimeOnly t)
    {
        t = default;
        var m = System.Text.RegularExpressions.Regex.Match(s ?? "", @"(\d{1,2})\D+(\d{2})");
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups[1].Value, out int h) || !int.TryParse(m.Groups[2].Value, out int mm)) return false;
        if (h > 23 || mm > 59) return false;
        t = new TimeOnly(h, mm);
        return true;
    }

    private static bool TryVal(IXLCell c, out int v)
    {
        v = 0;
        try
        {
            if (c.DataType == XLDataType.Number)
            {
                double d = c.GetDouble();
                if (d != Math.Truncate(d)) return false;
                v = (int)d;
                return true;
            }
            return TryParseInt(c, out v);
        }
        catch { return false; }
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
    public sealed record CompareResult(int Matched, List<string> OnlyInFile, List<string> OnlyInDb, int InvalidRows, int FileTotal, int DbTotal);

    // Сверка базы с эталонным Excel: те же правила разбора (через временную БД),
    // ничего не пишет. Списки урезаны до 30 для показа.
    public CompareResult CompareWithFile(string file)
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"mc_cmp_{Guid.NewGuid():N}.db");
        try
        {
            var tmpDb = new Data.Db(tmp);
            var tmpSvc = new ExcelService(new MeasurementRepository(tmpDb), new IntakeRepository(tmpDb),
                new WellBeingRepository(tmpDb), _log);
            var (imp, skip) = tmpSvc.Import(file);
            _ = imp;
            var fileRows = new MeasurementRepository(tmpDb)
                .GetRange(new DateTime(2000, 1, 1), DateTime.Now.AddDays(2));
            var dbRows = _meas.GetRange(new DateTime(2000, 1, 1), DateTime.Now.AddDays(2));
            static string Key(Models.Measurement m) =>
                DedupHelper.MeasurementKey(m.TakenAt, m.TonometerName, m.Systolic, m.Diastolic, m.Pulse);
            static string Desc(Models.Measurement m) =>
                $"{m.TakenAt:dd.MM.yyyy HH:mm} | {m.TonometerName} | {m.Systolic}/{m.Diastolic} п.{m.Pulse}";
            var dbKeys = dbRows.ToDictionary(Key, m => m);
            var fileKeys = fileRows.ToDictionary(Key, m => m);
            var onlyFile = fileKeys.Keys.Where(k => !dbKeys.ContainsKey(k))
                .Select(k => Desc(fileKeys[k])).OrderBy(s => s).ToList();
            var onlyDb = dbKeys.Keys.Where(k => !fileKeys.ContainsKey(k))
                .Select(k => Desc(dbKeys[k])).OrderBy(s => s).ToList();
            int matched = fileKeys.Keys.Count(k => dbKeys.ContainsKey(k));
            const int cap = 30;
            return new CompareResult(matched,
                onlyFile.Take(cap).ToList(), onlyDb.Take(cap).ToList(), skip,
                fileRows.Count, dbRows.Count);
        }
        finally
        {
            try { File.Delete(tmp); File.Delete(tmp + "-wal"); File.Delete(tmp + "-shm"); } catch { }
        }
    }

    // Шаблон блочного дневника: слева Тонометр 1, справа Тонометр 2.
    // Проверки Excel не дадут ввести мусор; импорт перепроверит всё ещё раз.
    public void CreateTemplate(string file)
    {
        using var book = new XLWorkbook();
        var ws = book.Worksheets.Add("Измерения");
        ws.Cell(1, 1).Value = "Тонометр 1";
        ws.Cell(1, 6).Value = "Тонометр 2";
        string[] h = ["дата", "время", "верхнее", "нижнее", "пульс", "время", "верхнее", "нижнее", "пульс"];
        for (int i = 0; i < h.Length; i++) ws.Cell(2, i + 1).Value = h[i];
        ws.Cell(3, 1).Value = "01.10.2026"; ws.Cell(3, 2).Value = "08:00";
        ws.Cell(3, 3).Value = 120; ws.Cell(3, 4).Value = 80; ws.Cell(3, 5).Value = 70;
        ws.Cell(3, 6).Value = "08:05";
        ws.Cell(3, 7).Value = 118; ws.Cell(3, 8).Value = 78; ws.Cell(3, 9).Value = 72;
        ws.Cell(4, 1).Value = "01.10.2026"; ws.Cell(4, 2).Value = "20:00";
        ws.Cell(4, 3).Value = 122; ws.Cell(4, 4).Value = 81; ws.Cell(4, 5).Value = 69;
        try
        {
            // Дата — датой 2020+, значения — целыми в диапазонах
            ws.Range("A3:A1000").SetDataValidation().Date
                .Between(new DateTime(2020, 1, 1), DateTime.Today.AddYears(1));
            foreach (var col in new[] { "C", "G" })
                ws.Range($"{col}3:{col}1000").SetDataValidation().WholeNumber.Between(0, 300);
            foreach (var col in new[] { "D", "H" })
                ws.Range($"{col}3:{col}1000").SetDataValidation().WholeNumber.Between(0, 200);
            foreach (var col in new[] { "E", "I" })
                ws.Range($"{col}3:{col}1000").SetDataValidation().WholeNumber.Between(0, 250);
        }
        catch (Exception ex) { _log.Write("Template", ex.Message); }
        var how = book.Worksheets.Add("Как заполнять");
        how.Cell(1, 1).Value = "Удалите строки-примеры и впишите свои замеры. " +
            "Слева — первый тонометр, справа — второй (имена — в первой строке). " +
            "Дата: ДД.ММ.ГГГГ, время: ЧЧ:ММ (общее для обоих). Пустой блок — пропуск. " +
            "Красные ячейки — ошибка ввода. Импорт: Excel/PDF — Импорт.";
        book.SaveAs(file);
    }
}

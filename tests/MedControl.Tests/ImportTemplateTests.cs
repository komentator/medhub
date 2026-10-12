using MedControl.Data;
using MedControl.Logging;
using MedControl.Repositories;

namespace MedControl.Tests;

public class ImportTemplateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"mc_tpl_{Guid.NewGuid():N}");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact] public void Template_Import_Roundtrip()
    {
        Directory.CreateDirectory(_dir);
        var db = new Db(Path.Combine(_dir, "t.db"));
        var excel = new MedControl.ImportExport.ExcelService(
            new MeasurementRepository(db), new IntakeRepository(db),
            new WellBeingRepository(db), new MedicationRepository(db), new LogService(db));
        var file = Path.Combine(_dir, "tpl.xlsx");
        excel.CreateTemplate(file);
        Assert.True(File.Exists(file));
        var (n1, s1) = excel.Import(file);
        Assert.Equal(3, n1);   // примеры из шаблона: 2 + 1
        Assert.Equal(0, s1);
        var (n2, s2) = excel.Import(file);
        Assert.Equal(0, n2);   // повтор — дубликаты пропущены
        Assert.Equal(3, s2);
        Assert.Equal(3, new MeasurementRepository(db)
            .GetRange(new DateTime(2020, 1, 1), new DateTime(2030, 1, 1)).Count);
        // Тонометры из шапки созданы
        Assert.Equal(2, new MeasurementRepository(db).GetTonometers().Count);
    }

    [Fact] public void Diary_Messy_Rows_Skipped()
    {
        Directory.CreateDirectory(_dir);
        var db = new Db(Path.Combine(_dir, "d.db"));
        var excel = new MedControl.ImportExport.ExcelService(
            new MeasurementRepository(db), new IntakeRepository(db),
            new WellBeingRepository(db), new MedicationRepository(db), new LogService(db));
        var file = Path.Combine(_dir, "messy.xlsx");
        using (var book = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = book.Worksheets.Add("Лист1");
            ws.Cell(1, 1).Value = "Тонометр 1 (A&D UA-777)";
            ws.Cell(1, 6).Value = "Тонометр 2 (Little Doctor)";
            foreach (var (c, v) in new[] { (1, "дата"), (2, "время"), (3, "верхнее"), (4, "нижнее"), (5, "пульс"), (6, "время"), (7, "верхнее"), (8, "нижнее"), (9, "пульс") })
                ws.Cell(2, c).Value = v;
            ws.Cell(3, 1).Value = "05.10.2026"; ws.Cell(3, 2).Value = "08:00";
            ws.Cell(3, 3).Value = 120; ws.Cell(3, 4).Value = 80; ws.Cell(3, 5).Value = 70;
            ws.Cell(3, 7).Value = 118; ws.Cell(3, 8).Value = 78; ws.Cell(3, 9).Value = 72;
            ws.Cell(4, 2).Value = "26--25"; // битое время
            ws.Cell(4, 3).Value = 120; ws.Cell(4, 4).Value = 80; ws.Cell(4, 5).Value = 70;
            ws.Cell(5, 1).Value = "06.10.2026"; ws.Cell(5, 2).Value = "09:00";
            ws.Cell(5, 3).Value = "0-130"; // битое значение
            ws.Cell(5, 4).Value = 80; ws.Cell(5, 5).Value = 70;
            ws.Cell(5, 7).Value = 400; ws.Cell(5, 8).Value = 80; ws.Cell(5, 9).Value = 70; // вне диапазона
            book.SaveAs(file);
        }
        var (n, s) = excel.Import(file);
        Assert.Equal(3, n);   // строка 3 (оба) + строка 4 (время по умолчанию 09:00)
        Assert.Equal(2, s);   // битые значения + вне диапазона
        var rows = new MeasurementRepository(db).GetRange(new DateTime(2026, 10, 4), new DateTime(2026, 10, 6));
        Assert.Contains(rows, m => m.TakenAt == new DateTime(2026, 10, 5, 9, 0, 0));
    }

    [Fact] public void Compare_Finds_Diffs_Both_Ways()
    {
        Directory.CreateDirectory(_dir);
        var db = new Db(Path.Combine(_dir, "c.db"));
        var meas = new MeasurementRepository(db);
        var excel = new MedControl.ImportExport.ExcelService(
            meas, new IntakeRepository(db),
            new WellBeingRepository(db), new MedicationRepository(db), new LogService(db));
        var t = new MedControl.Models.Tonometer { Name = "T" };
        meas.SaveTonometer(t);
        meas.Upsert(new MedControl.Models.Measurement
        {
            TonometerId = t.Id, TonometerName = t.Name, TakenAt = new DateTime(2026, 10, 1, 8, 0, 0),
            Systolic = 120, Diastolic = 80, Pulse = 70, CreatedAt = new DateTime(2026, 10, 1, 8, 0, 0),
        });
        meas.Upsert(new MedControl.Models.Measurement
        {
            TonometerId = t.Id, TonometerName = t.Name, TakenAt = new DateTime(2026, 10, 2, 8, 0, 0),
            Systolic = 121, Diastolic = 81, Pulse = 71, CreatedAt = new DateTime(2026, 10, 2, 8, 0, 0),
        });
        var file = Path.Combine(_dir, "cmp.xlsx");
        using (var book = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = book.Worksheets.Add("Измерения");
            foreach (var (c, v) in new[] { (1, "Дата"), (2, "Время"), (3, "Тонометр"), (4, "Верхнее"), (5, "Нижнее"), (6, "Пульс") })
                ws.Cell(1, c).Value = v;
            ws.Cell(2, 1).Value = "01.10.2026"; ws.Cell(2, 2).Value = "08:00"; ws.Cell(2, 3).Value = "T";
            ws.Cell(2, 4).Value = 120; ws.Cell(2, 5).Value = 80; ws.Cell(2, 6).Value = 70; // совпадение
            ws.Cell(3, 1).Value = "03.10.2026"; ws.Cell(3, 2).Value = "08:00"; ws.Cell(3, 3).Value = "T";
            ws.Cell(3, 4).Value = 125; ws.Cell(3, 5).Value = 82; ws.Cell(3, 6).Value = 72; // только в файле
            book.SaveAs(file);
        }
        var r = excel.CompareWithFile(file);
        Assert.Equal(1, r.Matched);
        Assert.Single(r.OnlyInFile);
        Assert.Single(r.OnlyInDb); // замер 02.10 только в базе
        Assert.Equal(2, r.FileTotal);
        Assert.Equal(2, r.DbTotal);
    }

    [Fact] public void Meds_Import_Creates_And_Updates()
    {
        Directory.CreateDirectory(_dir);
        var db = new Db(Path.Combine(_dir, "m.db"));
        var meas = new MeasurementRepository(db);
        meas.SaveTonometer(new MedControl.Models.Tonometer { Name = "T" });
        var meds = new MedicationRepository(db);
        meds.Save(new MedControl.Models.Medication { Name = "Старое", Mode = MedControl.Models.ScheduleMode.Daily });
        var excel = new MedControl.ImportExport.ExcelService(
            meas, new IntakeRepository(db),
            new WellBeingRepository(db), new MedicationRepository(db), new LogService(db));
        var file = Path.Combine(_dir, "meds.xlsx");
        using (var book = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = book.Worksheets.Add("Лекарства");
            foreach (var (c, v) in new[] { (1, "Название"), (2, "Назначение"), (6, "ИнструкцияТекст") })
                ws.Cell(1, c).Value = v;
            ws.Cell(2, 1).Value = "Новое";
            ws.Cell(2, 2).Value = "От головы";
            ws.Cell(2, 6).Value = "Пить воду.";
            ws.Cell(3, 1).Value = "Старое";
            ws.Cell(3, 2).Value = "Обновлено";
            ws.Cell(4, 1).Value = "";
            book.SaveAs(file);
        }
        var (md, ms) = excel.ImportMedications(
            new ClosedXML.Excel.XLWorkbook(file), Path.GetDirectoryName(file)!);
        Assert.Equal(2, md);
        Assert.Equal(0, ms);
        var all = meds.GetAll();
        Assert.Contains(all, m => m.Name == "Новое" && m.Description == "От головы" && m.InstructionText == "Пить воду.");
        Assert.Contains(all, m => m.Name == "Старое" && m.Description == "Обновлено");
    }

    [Fact] public void ParseTsv_Splits_Rows()
    {
        var rows = MedControl.ImportExport.ExcelService.ParseTsv("a\tb\r\n1\t2\t3\r\n\r\nx\t");
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "a", "b" }, rows[0]);
        Assert.Equal(new[] { "1", "2", "3" }, rows[1]);
        Assert.Equal(new[] { "x" }, rows[2]);
    }

    [Fact] public void ChartImage_Renders_Png()
    {
        var meas = new List<MedControl.Models.Measurement>
        {
            new() { TonometerName = "T", TakenAt = new DateTime(2026, 10, 1, 8, 0, 0), Systolic = 120, Diastolic = 80, Pulse = 70 },
            new() { TonometerName = "T", TakenAt = new DateTime(2026, 10, 2, 8, 0, 0), Systolic = 125, Diastolic = 82, Pulse = 72 },
        };
        var png = MedControl.Reports.ChartImage.Pressure(meas, 400, 200);
        Assert.True(png.Length > 100);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, png.Take(4).ToArray()); // PNG-сигнатура
        var wb = MedControl.Reports.ChartImage.WellBeing(new List<MedControl.Models.WellBeingRating>
        {
            new() { Score = 7, CreatedAt = new DateTime(2026, 10, 1, 8, 0, 0) },
        }, 400, 200);
        Assert.True(wb.Length > 100);
    }

    [Fact] public void Diary_Binds_By_Creation_Order_And_Defaults_Time()
    {
        Directory.CreateDirectory(_dir);
        var db = new Db(Path.Combine(_dir, "o.db"));
        var meas = new MeasurementRepository(db);
        meas.SaveTonometer(new MedControl.Models.Tonometer { Name = "ZZZ-first" });
        meas.SaveTonometer(new MedControl.Models.Tonometer { Name = "AAA-second" });
        var excel = new MedControl.ImportExport.ExcelService(
            meas, new IntakeRepository(db),
            new WellBeingRepository(db), new MedicationRepository(db), new LogService(db));
        var file = Path.Combine(_dir, "order.xlsx");
        using (var book = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = book.Worksheets.Add("Лист1");
            ws.Cell(1, 1).Value = "дата";
            foreach (var (c, v) in new[] { (2, "время"), (3, "верхнее"), (4, "нижнее"), (5, "пульс"), (7, "верхнее"), (8, "нижнее"), (9, "пульс") })
                ws.Cell(2, c).Value = v;
            ws.Cell(1, 1).Value = "дата";
            ws.Cell(3, 1).Value = "07.10.2026"; // без времени
            ws.Cell(3, 3).Value = 120; ws.Cell(3, 4).Value = 80; ws.Cell(3, 5).Value = 70;
            ws.Cell(3, 7).Value = 118; ws.Cell(3, 8).Value = 78; ws.Cell(3, 9).Value = 72;
            ws.Cell(4, 1).Value = "07.10.2026"; // без времени
            ws.Cell(4, 3).Value = 122; ws.Cell(4, 4).Value = 81; ws.Cell(4, 5).Value = 71;
            book.SaveAs(file);
        }
        var (n, s) = excel.Import(file);
        Assert.Equal(3, n);
        Assert.Equal(0, s);
        var rows = meas.GetRange(new DateTime(2026, 10, 7), new DateTime(2026, 10, 8)).ToList();
        var nine = rows.Where(m => m.TakenAt == new DateTime(2026, 10, 7, 9, 0, 0)).ToList();
        var ten = rows.Where(m => m.TakenAt == new DateTime(2026, 10, 7, 10, 0, 0)).ToList();
        // Первый блок — первый добавленный (ZZZ-first), второй — второй, хоть по алфавиту наоборот
        Assert.Equal(2, nine.Count);
        Assert.Contains(nine, m => m.TonometerName == "ZZZ-first" && m.Systolic == 120);
        Assert.Contains(nine, m => m.TonometerName == "AAA-second" && m.Systolic == 118);
        // Время по умолчанию: 09:00, затем 10:00
        Assert.Single(ten);
        Assert.Equal("ZZZ-first", ten[0].TonometerName);
    }
}

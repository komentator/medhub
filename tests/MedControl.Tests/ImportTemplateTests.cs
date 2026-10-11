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
            new WellBeingRepository(db), new LogService(db));
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
            new WellBeingRepository(db), new LogService(db));
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
        Assert.Equal(2, n);   // только первая строка, оба тонометра
        Assert.True(s >= 3);  // битое время + битые значения + вне диапазона
    }
}

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
        Assert.Equal(2, n1);   // два примера из шаблона
        Assert.Equal(0, s1);
        var (n2, s2) = excel.Import(file);
        Assert.Equal(0, n2);   // повтор — дубликаты пропущены
        Assert.Equal(2, s2);
        Assert.Equal(2, new MeasurementRepository(db)
            .GetRange(new DateTime(2020, 1, 1), new DateTime(2030, 1, 1)).Count);
    }
}

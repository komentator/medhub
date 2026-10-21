using MedControl.Data;
using MedControl.Logging;
using MedControl.Models;
using MedControl.Reports;
using MedControl.Repositories;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace MedControl.Tests;

public sealed class WordReportTests : IDisposable
{
    private readonly string _dir;

    public WordReportTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mc_docx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    private (WordReportService Rep, Db Db) MakeRepo()
    {
        var db = new Db(Path.Combine(_dir, "w.db"));
        var meas = new MeasurementRepository(db);
        meas.SaveTonometer(new Tonometer { Name = "Дом" });
        meas.Upsert(new Measurement
        {
            TonometerName = "Дом", Systolic = 138, Diastolic = 65, Pulse = 77,
            TakenAt = new DateTime(2026, 10, 1, 8, 0, 0)
        });
        meas.Upsert(new Measurement
        {
            TonometerName = "Дом", Systolic = 120, Diastolic = 80, Pulse = 70,
            TakenAt = new DateTime(2026, 10, 2, 9, 0, 0), Comment = "после прогулки"
        });
        var wb = new WellBeingRepository(db);
        wb.Upsert(new WellBeingRating { Score = 7, Comment = "нормально", CreatedAt = new DateTime(2026, 10, 2, 10, 0, 0) });
        var ints = new IntakeRepository(db);
        ints.Upsert(new MedicationIntake
        {
            MedicationName = "Лозартан", Dosage = "50 мг", ScheduledAt = new DateTime(2026, 10, 2, 8, 0, 0),
            ActualAt = new DateTime(2026, 10, 2, 8, 10, 0), Status = IntakeStatus.Taken
        });
        return (new WordReportService(meas, ints, wb, new LogService(db)), db);
    }

    [Theory]
    [InlineData("measurements")]
    [InlineData("intakes")]
    [InlineData("joint")]
    [InlineData("charts")]
    public void Docx_Builds_Valid_File(string kind)
    {
        var (rep, _) = MakeRepo();
        string path = rep.BuildDocx(new DateTime(2026, 9, 1), new DateTime(2026, 11, 1), kind, true);
        try
        {
            Assert.True(File.Exists(path));
            Assert.EndsWith(".docx", path);

            using var doc = WordprocessingDocument.Open(path, false);
            var main = doc.MainDocumentPart;
            Assert.NotNull(main);
            Assert.NotNull(main!.Document);

            // Схема OOXML: Word не откроет файл при нарушении порядка элементов.
            var validator = new OpenXmlValidator();
            var errors = validator.Validate(doc).ToList();
            Assert.True(errors.Count == 0,
                string.Join(" | ", errors.Take(5).Select(e => $"{e.Description} @ {e.Path?.XPath}")));

            // В документе есть текст с шапкой отчёта
            var body = main.Document.Body!;
            var text = string.Concat(body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text));
            Assert.Contains("MedControl", text);
            Assert.Contains("Период", text);

            // Таблицы есть в отчётах с данными (для «Графиков» их нет)
            if (kind != "charts")
                Assert.NotEmpty(body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Table>().ToList());
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Docx_Charts_Embed_Images()
    {
        var (rep, _) = MakeRepo();
        string path = rep.BuildDocx(new DateTime(2026, 9, 1), new DateTime(2026, 11, 1), "charts", true);
        try
        {
            using var doc = WordprocessingDocument.Open(path, false);
            var main = doc.MainDocumentPart!;
            Assert.Equal(2, main.ImageParts.Count());          // давление + самочувствие
            var drawings = main.Document.Body!.Descendants<DocumentFormat.OpenXml.Wordprocessing.Drawing>().ToList();
            Assert.Equal(2, drawings.Count);                   // обе картинки вставлены в текст
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Docx_Joint_Has_Sections_And_Data()
    {
        var (rep, _) = MakeRepo();
        string path = rep.BuildDocx(new DateTime(2026, 9, 1), new DateTime(2026, 11, 1), "joint", true);
        try
        {
            using var doc = WordprocessingDocument.Open(path, false);
            var body = doc.MainDocumentPart!.Document.Body!;
            var text = string.Concat(body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text));
            Assert.Contains("Измерения давления", text);
            Assert.Contains("Приём лекарств", text);
            Assert.Contains("Самочувствие", text);
            Assert.Contains("Лозартан", text);
            Assert.Contains("138", text);

            // Две секции: таблицы — книжная, графики — альбомная.
            var sizes = body.Descendants<DocumentFormat.OpenXml.Wordprocessing.PageSize>().ToList();
            Assert.Equal(2, sizes.Count);
            Assert.Equal("portrait", sizes[0].Orient?.InnerText);
            Assert.Equal("landscape", sizes[1].Orient?.InnerText);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Docx_Charts_Only_Landscape()
    {
        var (rep, _) = MakeRepo();
        string path = rep.BuildDocx(new DateTime(2026, 9, 1), new DateTime(2026, 11, 1), "charts", true);
        try
        {
            using var doc = WordprocessingDocument.Open(path, false);
            var body = doc.MainDocumentPart!.Document.Body!;
            var sizes = body.Descendants<DocumentFormat.OpenXml.Wordprocessing.PageSize>().ToList();
            Assert.Single(sizes);
            Assert.Equal("landscape", sizes[0].Orient?.InnerText);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Docx_ChartPoints_Splits_Into_Parts()
    {
        var (rep, _) = MakeRepo();
        // 2 измерения по 1 точке → 2 графика давления + 1 график самочувствия.
        string path = rep.BuildDocx(new DateTime(2026, 9, 1), new DateTime(2026, 11, 1), "charts", true, 1);
        try
        {
            using var doc = WordprocessingDocument.Open(path, false);
            var validator = new OpenXmlValidator();
            Assert.Empty(validator.Validate(doc).ToList());
            Assert.Equal(3, doc.MainDocumentPart!.ImageParts.Count());

            var text = string.Concat(doc.MainDocumentPart!.Document.Body!
                .Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text));
            Assert.Contains("часть 1 из 3", text);
            Assert.Contains("часть 3 из 3", text);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Chunks_Splits_By_Limit()
    {
        // 200 точек по 60 → 4 части (60+60+60+20)
        var list = Enumerable.Range(0, 200).Select(i => i).ToList();
        var parts = MedControl.Reports.WordReportService.Chunks(list, 60);
        Assert.Equal(4, parts.Count);
        Assert.Equal(60, parts[0].Count);
        Assert.Equal(60, parts[1].Count);
        Assert.Equal(60, parts[2].Count);
        Assert.Equal(20, parts[3].Count);
        // хронология сохраняется
        Assert.Equal(0, parts[0][0]);
        Assert.Equal(199, parts[3][^1]);
    }

    [Fact]
    public void Chunks_Fewer_Than_Limit_Keeps_All()
    {
        var list = Enumerable.Range(0, 25).ToList();
        var parts = MedControl.Reports.WordReportService.Chunks(list, 60);
        Assert.Single(parts);
        Assert.Equal(25, parts[0].Count);
    }

    [Fact]
    public void Chunks_Zero_Means_All_In_One()
    {
        var list = Enumerable.Range(0, 150).ToList();
        var parts = MedControl.Reports.WordReportService.Chunks(list, 0);
        Assert.Single(parts);
        Assert.Equal(150, parts[0].Count);
    }

    [Fact]
    public void Chart_Respects_MaxPoints()
    {
        var meas = new List<MedControl.Models.Measurement>();
        for (int i = 0; i < 12; i++)
        {
            meas.Add(new MedControl.Models.Measurement
            {
                TonometerName = "T",
                Systolic = 115 + i,
                Diastolic = 75 + (i % 5),
                Pulse = 70,
                TakenAt = new DateTime(2026, 1, 1).AddHours(i * 6)
            });
        }
        var all = MedControl.Reports.ChartImage.Pressure(meas);
        var few = MedControl.Reports.ChartImage.Pressure(meas, maxPoints: 3);
        Assert.False(all.SequenceEqual(few));   // график с 3 точками отличается от графика со всеми

        var wb = new List<MedControl.Models.WellBeingRating>();
        for (int i = 0; i < 12; i++)
            wb.Add(new MedControl.Models.WellBeingRating { Score = 3 + i % 5, CreatedAt = new DateTime(2026, 1, 1).AddHours(i * 6) });
        var wbAll = MedControl.Reports.ChartImage.WellBeing(wb);
        var wbFew = MedControl.Reports.ChartImage.WellBeing(wb, maxPoints: 3);
        Assert.False(wbAll.SequenceEqual(wbFew));
    }
}

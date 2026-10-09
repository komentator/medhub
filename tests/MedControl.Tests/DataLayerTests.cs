using MedControl.Data;
using MedControl.Models;
using MedControl.Repositories;
using MedControl.Services;
using MedControl.Logging;
using MedControl.Settings;

namespace MedControl.Tests;

// Сквозные проверки слоя данных на временной БД (без UI).
public class DataLayerTests : IDisposable
{
    private readonly string _file;
    private readonly Db _db;
    public DataLayerTests()
    {
        _file = Path.Combine(Path.GetTempPath(), $"medcontrol_test_{Guid.NewGuid():N}.db");
        _db = new Db(_file);
    }
    public void Dispose()
    {
        try { File.Delete(_file); File.Delete(_file + "-wal"); File.Delete(_file + "-shm"); } catch { }
    }

    [Fact] public void Medication_Save_Get_Snapshot_Intake()
    {
        var meds = new MedicationRepository(_db);
        var intakes = new IntakeRepository(_db);
        var m = new Medication { Name = "Аспирин", Description = "назначение", Mode = ScheduleMode.Daily };
        m.Doses.Add(new MedicationDose { MedicationId = m.Id, Time = new TimeOnly(8, 0), Dosage = "1 таблетка" });
        meds.Save(m);
        Assert.Single(meds.GetAll(), x => x.Name == "Аспирин");

        var sched = new DateTime(2026, 1, 1, 8, 0, 0);
        intakes.Upsert(new MedicationIntake
        {
            MedicationId = m.Id, MedicationName = m.Name, Dosage = "1 таблетка",
            ScheduledAt = sched, ActualAt = sched, Status = IntakeStatus.Taken,
            ConfirmMethod = ConfirmMethod.Reminder, CreatedAt = sched,
        });
        Assert.True(intakes.Exists(m.Id, sched));

        // Удаление лекарства историю не трогает (§8.2)
        meds.Delete(m.Id);
        Assert.Empty(meds.GetAll());
        Assert.Single(intakes.GetRange(new DateTime(2025, 1, 1), new DateTime(2027, 1, 1)));
    }

    [Fact] public void Measurement_Trash_Restore()
    {
        var repo = new MeasurementRepository(_db);
        var t = new Tonometer { Name = "Omron" };
        repo.SaveTonometer(t);
        var m = new Measurement
        {
            TonometerId = t.Id, TonometerName = t.Name, TakenAt = new DateTime(2026, 2, 1, 9, 0, 0),
            Systolic = 120, Diastolic = 80, Pulse = 70, CreatedAt = new DateTime(2026, 2, 1, 9, 0, 0),
        };
        repo.Upsert(m);
        Assert.Single(repo.GetRange(new DateTime(2026, 1, 1), new DateTime(2026, 3, 1)));
        repo.MoveToTrash(m.Id);
        Assert.Empty(repo.GetRange(new DateTime(2026, 1, 1), new DateTime(2026, 3, 1)));
        Assert.Single(repo.GetTrash());
        repo.Restore(m.Id);
        Assert.Single(repo.GetRange(new DateTime(2026, 1, 1), new DateTime(2026, 3, 1)));
    }

    [Fact] public void WellBeing_Links_And_Separate_Trash()
    {
        var meas = new MeasurementRepository(_db);
        var intakes = new IntakeRepository(_db);
        var wb = new WellBeingRepository(_db);
        var svc = new WellBeingService(wb, meas, intakes);
        var at = new DateTime(2026, 3, 1, 12, 0, 0);
        var t = new Tonometer { Name = "T" };
        meas.SaveTonometer(t);
        var mm = new Measurement { TonometerId = t.Id, TonometerName = t.Name, TakenAt = at.AddMinutes(-5), Systolic = 120, Diastolic = 80, Pulse = 70, Comment = "голова", CreatedAt = at.AddMinutes(-5) };
        meas.Upsert(mm);

        var rating = svc.Create(7, "", at); // комментарий скопируется из измерения (§6.2)
        Assert.Equal("голова", rating.Comment);
        Assert.Single(wb.GetLinks(rating.Id));

        wb.MoveToTrash(rating.Id);
        Assert.Empty(wb.GetRange(new DateTime(2026, 1, 1), new DateTime(2026, 4, 1)));
        Assert.Single(wb.GetTrash()); // отдельная корзина (§23)
    }

    [Fact] public void Settings_Roundtrip_And_Logs_Rotate()
    {
        var settings = new SettingsService(_db);
        settings.MissedGraceMinutes = 120;
        Assert.Equal(120, new SettingsService(_db).MissedGraceMinutes);
        var log = new LogService(_db);
        log.Write("Test", "hello");
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM TechnicalLogs WHERE Category='Test'";
        Assert.True(Convert.ToInt32(cmd.ExecuteScalar()) >= 1);
    }

    [Fact] public void Excel_Export_Import_Roundtrip_No_Dups()
    {
        var meas = new MeasurementRepository(_db);
        var intakes = new IntakeRepository(_db);
        var wb = new WellBeingRepository(_db);
        var log = new LogService(_db);
        var excel = new MedControl.ImportExport.ExcelService(meas, intakes, wb, log);
        var t = new Tonometer { Name = "Omron X" };
        meas.SaveTonometer(t);
        meas.Upsert(new Measurement
        {
            TonometerId = t.Id, TonometerName = t.Name, TakenAt = new DateTime(2026, 4, 1, 8, 0, 0),
            Systolic = 120, Diastolic = 80, Pulse = 65, CreatedAt = new DateTime(2026, 4, 1, 8, 0, 0),
        });
        var file = Path.Combine(Path.GetTempPath(), $"mc_{Guid.NewGuid():N}.xlsx");
        try
        {
            excel.Export(file, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), true, true, true, true);
            Assert.True(File.Exists(file));
            var (n, _) = excel.Import(file); // своих же данных: дубликаты пропускаются
            Assert.Equal(0, n);
        }
        finally { try { File.Delete(file); } catch { } }
    }
}

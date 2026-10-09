using MedControl.Audio;
using MedControl.Data;
using MedControl.Logging;
using MedControl.Models;
using MedControl.Repositories;
using MedControl.Scheduling;
using MedControl.Services;
using MedControl.Settings;

namespace MedControl.Tests;

// §11: восстановление громкости после «Принял», различие своих/чужых уведомлений,
// отложенные группы не теряются и не блокируют восстановление.
public class VolumeNotificationTests
{
    [Theory]
    [InlineData(10f, 10f, true)]    // уведомление о нашей собственной установке — игнорировать
    [InlineData(10.4f, 10f, true)]  // погрешность округления драйвера
    [InlineData(35f, 10f, false)]   // пользователь выставил вручную — не игнорировать
    [InlineData(35f, -1f, false)]   // нет активного снижения — любое изменение чужое
    public void Own_Notification_Ignored(float notified, float target, bool ignore)
        => Assert.Equal(ignore, VolumeService.ShouldIgnoreNotification(notified, target));
}

public class SnoozeVolumeTests : IDisposable
{
    private readonly string _file;
    private readonly Db _db;
    public SnoozeVolumeTests()
    {
        _file = Path.Combine(Path.GetTempPath(), $"medcontrol_vol_{Guid.NewGuid():N}.db");
        _db = new Db(_file);
    }
    public void Dispose()
    {
        try { File.Delete(_file); File.Delete(_file + "-wal"); File.Delete(_file + "-shm"); } catch { }
    }

    private ReminderService MakeService()
    {
        var log = new LogService(_db);
        return new ReminderService(
            new MedicationRepository(_db), new IntakeRepository(_db),
            new SettingsService(_db), log, new VolumeService(log), new SoundService(log));
    }

    private static PlannedDose Dose(Guid med, DateTime at)
        => new(med, Guid.NewGuid(), "M", "", "1 таблетка", at);

    [Fact] public void Confirm_One_Group_Keeps_Other_Snooze()
    {
        var svc = MakeService();
        var g1 = new List<PlannedDose> { Dose(Guid.NewGuid(), new DateTime(2026, 5, 1, 8, 0, 0)) };
        var g2 = new List<PlannedDose> { Dose(Guid.NewGuid(), new DateTime(2026, 5, 1, 8, 5, 0)) };
        svc.SnoozeGroup(g1);
        svc.SnoozeGroup(g2);
        Assert.Equal(2, svc.SnoozedCount);
        svc.Confirm(g1[0], new DateTime(2026, 5, 1, 8, 1, 0), ConfirmMethod.Reminder, late: false);
        Assert.Equal(1, svc.SnoozedCount); // чужая отложенная группа не потеряна
    }

    [Fact] public void Recorded_Snooze_Cleaned_On_GetPending()
    {
        var svc = MakeService();
        var med = Guid.NewGuid();
        var at = new DateTime(2026, 5, 1, 8, 0, 0);
        svc.SnoozeGroup(new List<PlannedDose> { Dose(med, at) });
        Assert.Equal(1, svc.SnoozedCount);
        // Доза получила запись в истории (подтверждена/пропущена) — отложенность снята.
        new IntakeRepository(_db).Upsert(new MedicationIntake
        {
            MedicationId = med, MedicationName = "M", Dosage = "1",
            ScheduledAt = at, ActualAt = at, Status = IntakeStatus.Taken,
            ConfirmMethod = ConfirmMethod.Reminder, CreatedAt = at,
        });
        svc.GetPending(new DateTime(2026, 5, 1, 12, 0, 0));
        Assert.Equal(0, svc.SnoozedCount);
    }

    [Fact] public void Snooze_Fires_Only_After_FireAt()
    {
        var svc = MakeService();
        var now = DateTime.Now;
        var dose = Dose(Guid.NewGuid(), now.AddMinutes(-5));
        svc.SnoozeGroup(new List<PlannedDose> { dose }, fireAt: now.AddMinutes(15));
        Assert.Empty(svc.GetActive(now)); // рано — окно не всплывает
        Assert.Single(svc.GetActive(now.AddMinutes(16))); // пора — активна
    }

    [Fact] public void NextSnoozeFire_Earliest()
    {
        var svc = MakeService();
        var now = DateTime.Now;
        svc.SnoozeGroup(new List<PlannedDose> { Dose(Guid.NewGuid(), now.AddMinutes(-30)) }, now.AddMinutes(15));
        svc.SnoozeGroup(new List<PlannedDose> { Dose(Guid.NewGuid(), now.AddMinutes(-20)) }, now.AddMinutes(5));
        Assert.Equal(now.AddMinutes(5), svc.GetNextSnoozeFire(now));
    }

    [Fact] public void Confirm_Writes_Shown_And_Snooze_Times()
    {
        var svc = MakeService();
        var med = Guid.NewGuid();
        var sched = new DateTime(2026, 5, 2, 8, 0, 0);
        var dose = Dose(med, sched);
        var shown = new DateTime(2026, 5, 2, 8, 0, 30);
        svc.MarkShown(new List<PlannedDose> { dose }, shown);
        svc.SnoozeGroup(new List<PlannedDose> { dose }, shown.AddMinutes(15));
        svc.Confirm(dose, shown.AddMinutes(16), ConfirmMethod.Reminder, late: false);
        var row = new IntakeRepository(_db).GetRange(new DateTime(2026, 5, 1), new DateTime(2026, 5, 3)).Single();
        Assert.Equal(shown, row.ShownAt);
        Assert.NotNull(row.SnoozedAt);
    }

    [Fact] public void ShownAndSnoozed_Times_Persist()
    {
        var repo = new IntakeRepository(_db);
        var at = new DateTime(2026, 6, 1, 8, 0, 0);
        repo.Upsert(new MedicationIntake
        {
            MedicationId = Guid.NewGuid(), MedicationName = "M", Dosage = "1",
            ScheduledAt = at, ActualAt = at, Status = IntakeStatus.Taken,
            ConfirmMethod = ConfirmMethod.Reminder, CreatedAt = at,
            ShownAt = at.AddMinutes(-2), SnoozedAt = at.AddMinutes(-15),
        });
        var got = repo.GetRange(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)).Single();
        Assert.Equal(at.AddMinutes(-2), got.ShownAt);
        Assert.Equal(at.AddMinutes(-15), got.SnoozedAt);
    }
}

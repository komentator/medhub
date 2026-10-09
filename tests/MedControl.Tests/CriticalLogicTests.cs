using MedControl.Models;
using MedControl.Scheduling;
using MedControl.Services;
using MedControl.Backup;
using MedControl.ImportExport;

namespace MedControl.Tests;

// §47. Тесты критической бизнес-логики.
public class ScheduleTests
{
    private static Medication DailyMed(params string[] times) => new()
    {
        Name = "Test", IsActive = true, Mode = ScheduleMode.Daily,
        Doses = times.Select(t => new MedicationDose { Time = TimeOnly.Parse(t), Dosage = "1" }).ToList()
    };

    [Fact] public void Daily_Generates_Each_Day()
    {
        var m = DailyMed("08:00");
        var occ = ScheduleCalculator.GetOccurrences([m], new DateTime(2026, 1, 1), new DateTime(2026, 1, 4));
        Assert.Equal(3, occ.Count);
    }

    [Fact] public void Weekly_Only_Selected_Days()
    {
        var m = new Medication
        {
            Name = "W", IsActive = true, Mode = ScheduleMode.Weekly, DaysOfWeek = [0, 2], // Пн, Ср
            Doses = [new MedicationDose { Time = new TimeOnly(9, 0), Dosage = "1" }]
        };
        // 2026-01-05 = понедельник
        var occ = ScheduleCalculator.GetOccurrences([m], new DateTime(2026, 1, 5), new DateTime(2026, 1, 12));
        Assert.Equal(2, occ.Count); // Пн 05 и Ср 07
    }

    [Fact] public void Midnight_Crossing()
    {
        var m = DailyMed("00:05", "23:55");
        var occ = ScheduleCalculator.GetOccurrences([m], new DateTime(2026, 1, 1, 0, 0, 0), new DateTime(2026, 1, 2, 0, 0, 0));
        Assert.Equal(2, occ.Count);
    }
}

public class GroupingTests
{
    [Theory]
    [InlineData("10:00", "10:15", true)]   // ровно 15 минут — одна группа
    [InlineData("10:00", "10:14", true)]   // 14:59 эквивалент
    [InlineData("10:00", "10:16", false)]  // 15:01+ — разные группы
    public void Grouping_15min_Boundary(string a, string b, bool sameGroup)
    {
        var list = new List<PlannedDose>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "M", "", "1", DateTime.Today.Add(TimeOnly.Parse(a).ToTimeSpan())),
            new(Guid.NewGuid(), Guid.NewGuid(), "M", "", "1", DateTime.Today.Add(TimeOnly.Parse(b).ToTimeSpan())),
        };
        var g = ScheduleCalculator.GroupBy15Minutes(list);
        Assert.Equal(sameGroup ? 1 : 2, g.Count);
    }

    [Fact] public void Snooze_Adds_15min() =>
        Assert.Equal(new DateTime(2026, 1, 1, 10, 15, 0), ScheduleCalculator.ApplySnooze(new DateTime(2026, 1, 1, 10, 0, 0)));
}

public class MissedTests
{
    [Theory]
    [InlineData(60, 61, true)]
    [InlineData(60, 59, false)]
    public void GracePeriod(int graceMin, int elapsedMin, bool missed)
    {
        var s = new DateTime(2026, 1, 1, 10, 0, 0);
        Assert.Equal(missed, ScheduleCalculator.IsMissed(s, s.AddMinutes(elapsedMin), TimeSpan.FromMinutes(graceMin)));
    }

    [Fact] public void NoDuplicates()
    {
        var medId = Guid.NewGuid();
        var sched = new DateTime(2026, 1, 1, 8, 0, 0);
        var hist = new List<MedicationIntake> { new() { MedicationId = medId, ScheduledAt = sched } };
        Assert.True(ScheduleCalculator.DoseAlreadyRecorded(hist, medId, sched));
        Assert.False(ScheduleCalculator.DoseAlreadyRecorded(hist, medId, sched.AddMinutes(1)));
    }
}

public class ValidationTests
{
    [Theory]
    [InlineData(120, 80, true)]
    [InlineData(0, 0, true)]
    [InlineData(120, 0, false)]
    [InlineData(0, 80, false)]
    [InlineData(80, 120, false)]
    [InlineData(49, 30, false)]
    [InlineData(301, 90, false)]
    public void Pressure(int s, int d, bool ok) => Assert.Equal(ok, ValidationService.ValidatePressure(s, d).Ok);

    [Theory]
    [InlineData(0, true)]
    [InlineData(60, true)]
    [InlineData(19, false)]
    [InlineData(251, false)]
    public void Pulse(int p, bool ok) => Assert.Equal(ok, ValidationService.ValidatePulse(p).Ok);
}

public class StatisticsTests
{
    [Fact] public void Pressure_Avg_Excludes_ZeroZero()
    {
        var ms = new List<Measurement>
        {
            new() { Systolic = 120, Diastolic = 80, Pulse = 70 },
            new() { Systolic = 0, Diastolic = 0, Pulse = 0 },
            new() { Systolic = 140, Diastolic = 90, Pulse = 80 },
        };
        var st = StatisticsService.Pressure(ms);
        Assert.Equal(130, st.AvgSys);
        Assert.Equal(85, st.AvgDia);
        Assert.Equal(75, st.AvgPulse);
        Assert.Equal(3, st.Count);
    }
}

public class WellBeingLinkTests
{
    [Fact] public void Links_Within_15min()
    {
        var at = new DateTime(2026, 1, 1, 12, 0, 0);
        var meas = new List<(Guid, DateTime)> { (Guid.NewGuid(), at.AddMinutes(-15)), (Guid.NewGuid(), at.AddMinutes(15)), (Guid.NewGuid(), at.AddMinutes(16)) };
        var res = WellBeingLinker.FindLinks(at, meas, []);
        Assert.Equal(2, res.Count); // ровно ±15 входит, 16 — нет
    }
}

public class BackupPolicyTests
{
    [Fact] public void Needs_Taken_Last()
    {
        var today = new DateOnly(2026, 1, 1);
        var planned = new List<(DateTime, IntakeStatus)>
        {
            (new DateTime(2026, 1, 1, 8, 0, 0), IntakeStatus.Taken),
            (new DateTime(2026, 1, 1, 20, 0, 0), IntakeStatus.TakenLate),
        };
        Assert.False(BackupPolicy.ShouldCreateDailyBackup(today, planned, [])); // последний TakenLate — нет копии
        planned[1] = (planned[1].Item1, IntakeStatus.Taken);
        Assert.True(BackupPolicy.ShouldCreateDailyBackup(today, planned, []));
        Assert.False(BackupPolicy.ShouldCreateDailyBackup(today, [], [])); // нет планов — нет копии
    }

    [Fact] public void Retention_7()
    {
        var files = Enumerable.Range(1, 9).Select(i => $"f{i}").ToList();
        Assert.Equal(2, BackupPolicy.SelectForDeletion(files).Count);
    }

    [Fact] public void PickLastValid_Skips_Invalid()
    {
        var r = BackupPolicy.PickLastValid(["a", "b", "c"], f => f == "c");
        Assert.Equal("c", r);
        Assert.Null(BackupPolicy.PickLastValid(["a"], _ => false));
    }
}

public class DedupTests
{
    [Fact] public void OnlyNew_Skips_Duplicates()
    {
        var existing = new HashSet<string> { "k1" };
        var res = DedupHelper.OnlyNew(["k1", "k2", "k2"], existing, x => x);
        Assert.Single(res);
        Assert.Equal("k2", res[0]);
    }
}

using MedControl.Data;
using MedControl.Logging;
using MedControl.Models;
using MedControl.Repositories;
using MedControl.Services;
using MedControl.Settings;

namespace MedControl.Tests;

public class TransferTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"mc_transfer_{Guid.NewGuid():N}");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact] public void Pack_Roundtrip_With_Photos_And_Leader()
    {
        var aDb = Path.Combine(_dir, "a", "medcontrol.db");
        var aPh = Path.Combine(_dir, "a", "Photos");
        var bDb = Path.Combine(_dir, "b", "medcontrol.db");
        var bPh = Path.Combine(_dir, "b", "Photos");
        Directory.CreateDirectory(aPh);

        var dbA = new Db(aDb);
        var setA = new SettingsService(dbA);
        var svcA = new TransferService(setA, new LogService(dbA), aDb, aPh);
        var medsA = new MedicationRepository(dbA);
        var intakesA = new IntakeRepository(dbA);

        var m = new Medication { Name = "Тест", Mode = ScheduleMode.Daily };
        m.Doses.Add(new MedicationDose { MedicationId = m.Id, Time = new TimeOnly(9, 0), Dosage = "1" });
        medsA.Save(m);
        var sched = new DateTime(2026, 7, 1, 9, 0, 0);
        intakesA.Upsert(new MedicationIntake
        {
            MedicationId = m.Id, MedicationName = m.Name, Dosage = "1",
            ScheduledAt = sched, ActualAt = sched, Status = IntakeStatus.Taken,
            ConfirmMethod = ConfirmMethod.Reminder, CreatedAt = sched,
        });
        File.WriteAllBytes(Path.Combine(aPh, "pack.jpg"), [1, 2, 3, 4]);
        var idA = setA.DeviceId;

        var pack = Path.Combine(_dir, "move.mcpack");
        var (ok1, _) = svcA.CreatePack("192.168.1.10", pack);
        Assert.True(ok1);
        Assert.True(File.Exists(pack));

        var dbB = new Db(bDb);
        var setB = new SettingsService(dbB);
        var svcB = new TransferService(setB, new LogService(dbB), bDb, bPh);
        var (ok2, _) = svcB.RestorePack(pack);
        Assert.True(ok2);

        // Ведущий подставлен, участник новый, флаг первой связи взведён
        Assert.Equal("192.168.1.10", setB.PeerAddress);
        Assert.NotEqual(idA, setB.DeviceId);
        Assert.True(setB.PendingInitialSync);

        // Данные и фото на месте
        Assert.Single(new MedicationRepository(dbB).GetAll(), x => x.Name == "Тест");
        Assert.Single(new IntakeRepository(dbB).GetRange(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));
        Assert.True(File.Exists(Path.Combine(bPh, "pack.jpg")));
    }

    [Fact] public void PackBytes_Roundtrip()
    {
        var aDb = Path.Combine(_dir, "d", "medcontrol.db");
        var aPh = Path.Combine(_dir, "d", "Photos");
        var bDb = Path.Combine(_dir, "e", "medcontrol.db");
        var bPh = Path.Combine(_dir, "e", "Photos");
        Directory.CreateDirectory(aPh);
        var dbA = new Db(aDb);
        var svcA = new TransferService(new SettingsService(dbA), new LogService(dbA), aDb, aPh);
        new MedicationRepository(dbA).Save(new Medication { Name = "Сеть", Mode = ScheduleMode.Daily });
        var (ok1, _, bytes) = svcA.CreatePackBytes("192.168.1.20");
        Assert.True(ok1);
        Assert.NotNull(bytes);
        Assert.True(bytes!.Length > 100);
        var dbB = new Db(bDb);
        var setB = new SettingsService(dbB);
        var svcB = new TransferService(setB, new LogService(dbB), bDb, bPh);
        var (ok2, _) = svcB.RestorePackBytes(bytes);
        Assert.True(ok2);
        Assert.Equal("192.168.1.20", setB.PeerAddress);
        Assert.Single(new MedicationRepository(dbB).GetAll(), x => x.Name == "Сеть");
    }

    [Fact] public void Restore_BadFile_Fails()
    {
        var db = Path.Combine(_dir, "c", "medcontrol.db");
        var svc = new TransferService(new SettingsService(new Db(db)), new LogService(new Db(db)), db, Path.Combine(_dir, "c", "Photos"));
        var bad = Path.Combine(_dir, "bad.mcpack");
        File.WriteAllText(bad, "not a zip");
        var (ok, _) = svc.RestorePack(bad);
        Assert.False(ok);
    }
}

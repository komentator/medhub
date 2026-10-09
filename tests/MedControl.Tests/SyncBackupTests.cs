using MedControl.Backup;
using MedControl.Sync;

namespace MedControl.Tests;

public class SyncMergeTests
{
    [Theory]
    [InlineData("2026-01-02T10:00:00", null, "2026-01-01T10:00:00", null, true)]   // входящая новее
    [InlineData("2026-01-01T10:00:00", null, "2026-01-02T10:00:00", null, false)]  // локальная новее
    [InlineData(null, "2026-01-03T10:00:00", "2026-01-02T10:00:00", null, true)]   // удаление новее правки
    [InlineData("2026-01-02T10:00:00", null, "2026-01-02T10:00:00", null, false)]  // равны — не трогаем
    public void IncomingWins_Rule(string? iu, string? id, string? lu, string? ld, bool win)
        => Assert.Equal(win, SyncMerge.IncomingWins(iu, id, lu, ld));

    [Fact] public void ChangedSince_Filter()
    {
        var since = new DateTime(2026, 1, 2);
        Assert.True(SyncMerge.ChangedSince("2026-01-03T00:00:00", null, since));
        Assert.True(SyncMerge.ChangedSince(null, "2026-01-03T00:00:00", since));
        Assert.False(SyncMerge.ChangedSince("2026-01-01T00:00:00", null, since));
        Assert.False(SyncMerge.ChangedSince(null, null, since));
    }

    [Fact] public void PickWinner_Newest()
    {
        var win = SyncMerge.PickWinner([
            ("a", "2026-01-01T00:00:00"),
            ("b", "2026-01-03T00:00:00"),
            ("c", "2026-01-02T00:00:00"),
        ]);
        Assert.Equal("b", win);
    }

    [Fact] public void Natural_Keys_Stable()
    {
        Assert.Equal(SyncMerge.IntakeKey("m", "t"), SyncMerge.IntakeKey("m", "t"));
        Assert.NotEqual(SyncMerge.IntakeKey("m1", "t"), SyncMerge.IntakeKey("m2", "t"));
        Assert.Equal(SyncMerge.MeasKey("t", "x", 120, 80, 70), SyncMerge.MeasKey("t", "x", 120, 80, 70));
    }
}

public class ManualBackupTests
{
    [Fact] public void Manual_Naming_And_Detection()
    {
        var name = BackupPolicy.ManualFileName(new DateTime(2026, 1, 1, 8, 0, 0));
        Assert.StartsWith("MedControl_manual_", name);
        Assert.True(BackupPolicy.IsManual(name));
        Assert.False(BackupPolicy.IsManual(BackupPolicy.FileName(new DateTime(2026, 1, 1))));
    }

    [Fact] public void Rotation_Ignores_Manual()
    {
        // Ротация получает только авто-копии: ручные никогда не удаляются автоматически.
        var auto = Enumerable.Range(1, 9).Select(i => $"MedControl_2026-01-0{i}_08-00-00.db").ToList();
        Assert.Equal(2, BackupPolicy.SelectForDeletion(auto).Count);
    }
}

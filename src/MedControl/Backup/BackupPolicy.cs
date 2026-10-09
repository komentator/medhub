using System.IO;
using MedControl.Models;

namespace MedControl.Backup;

// §27–28. Чистая политика backup — покрыта тестами.
public static class BackupPolicy
{
    public const int MaxAutoBackups = 7;
    public static string FileName(DateTime now) => $"MedControl_{now:yyyy-MM-dd_HH-mm-ss}.db";
    public static string ManualFileName(DateTime now) => $"MedControl_manual_{now:yyyy-MM-dd_HH-mm-ss}.db";
    public static bool IsManual(string fileName) => Path.GetFileName(fileName).Contains("_manual_");

    // Backup создаётся после того, как последний запланированный приём
    // текущего дня получил статус именно «Принял» (§28).
    public static bool ShouldCreateDailyBackup(
        DateOnly today,
        IReadOnlyList<(DateTime ScheduledAt, IntakeStatus Status)> todaysPlanned,
        IReadOnlyList<DateTime> existingBackupDates)
    {
        if (todaysPlanned.Count == 0) return false; // нет планов — нет копии
        var last = todaysPlanned.MaxBy(x => x.ScheduledAt);
        if (last.Status != IntakeStatus.Taken) return false;
        // Одна актуальная копия текущего состояния: если сегодня уже есть копия — не дублируем
        if (existingBackupDates.Any(d => DateOnly.FromDateTime(d) == today)) return false;
        return true;
    }

    public static List<string> SelectForDeletion(IEnumerable<string> filesOrderedOldestFirst)
    {
        var list = filesOrderedOldestFirst.ToList();
        if (list.Count <= MaxAutoBackups) return new List<string>();
        return list.Take(list.Count - MaxAutoBackups).ToList();
    }

    // Выбор последнего валидного backup (§30, §47).
    public static string? PickLastValid(IList<string> newestFirst, Func<string, bool> isValid)
    {
        foreach (var f in newestFirst)
        {
            bool ok;
            try { ok = isValid(f); } catch { ok = false; }
            if (ok) return f;
        }
        return null;
    }
}

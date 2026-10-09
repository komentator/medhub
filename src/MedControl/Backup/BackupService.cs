using System.IO;
using MedControl.Data;
using MedControl.Models;
using MedControl.Logging;
using MedControl.Settings;

namespace MedControl.Backup;

// §27–31. Обычные копии без шифрования, max 7, смена папки с переносом.
public sealed class BackupService
{
    private readonly SettingsService _settings;
    private readonly LogService _log;
    public BackupService(SettingsService settings, LogService log) { _settings = settings; _log = log; }

    public string Dir => string.IsNullOrWhiteSpace(_settings.BackupDir) ? Paths.BackupDir : _settings.BackupDir;

    public void EnsureDir() => Directory.CreateDirectory(Dir);

    public List<string> ListAuto() =>
        Directory.Exists(Dir) ? Directory.GetFiles(Dir, "MedControl_*.db").Where(f => !BackupPolicy.IsManual(f)).OrderBy(f => f).ToList() : new List<string>();

    // Ручные копии: хранятся постоянно, удаляются только вручную.
    public List<string> ListManual() =>
        Directory.Exists(Dir) ? Directory.GetFiles(Dir, "MedControl_manual_*.db").OrderByDescending(f => f).ToList() : new List<string>();

    public (bool Ok, string Msg) CreateManual()
    {
        try
        {
            EnsureDir();
            var dest = Path.Combine(Dir, BackupPolicy.ManualFileName(DateTime.Now));
            using var src = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Paths.DbFile}");
            src.Open();
            using var cmd = src.CreateCommand();
            cmd.CommandText = $"VACUUM INTO '{dest.Replace("'", "''")}'";
            cmd.ExecuteNonQuery();
            _settings.LastBackupAt = DateTime.Now;
            _log.Write("Backup", "Ручная копия: " + dest);
            return (true, "Копия сохранена:\n" + dest);
        }
        catch (Exception ex) { _log.Write("Backup", "CreateManual: " + ex.Message); return (false, ex.Message); }
    }

    public (bool Ok, string Msg) DeleteManual(string file)
    {
        try
        {
            var full = Path.GetFullPath(file);
            if (!full.StartsWith(Path.GetFullPath(Dir))) return (false, "Файл вне папки копий.");
            if (!BackupPolicy.IsManual(full)) return (false, "Автоматические копии удаляются автоматически.");
            File.Delete(full);
            return (true, "Копия удалена.");
        }
        catch (Exception ex) { _log.Write("Backup", "DeleteManual: " + ex.Message); return (false, ex.Message); }
    }

    public (bool Ok, string Msg) RestoreManual(string file)
    {
        var full = Path.GetFullPath(file);
        if (!BackupPolicy.IsManual(full) && !full.StartsWith(Path.GetFullPath(Dir))) return (false, "Файл вне папки копий.");
        return RestoreFrom(full);
    }

    // Вызывать после каждого подтверждения «Принял»: проверяет §28 и создаёт копию.
    public bool TryDailyBackup(IReadOnlyList<(DateTime ScheduledAt, IntakeStatus Status)> todaysPlanned)
    {
        try
        {
            EnsureDir();
            var today = DateOnly.FromDateTime(DateTime.Now);
            var existingDates = ListAuto().Select(f =>
            {
                try { return File.GetCreationTime(f); } catch { return DateTime.MinValue; }
            }).ToList();
            if (!BackupPolicy.ShouldCreateDailyBackup(today, todaysPlanned, existingDates)) return false;
            // SQLite backup: VACUUM INTO (атомарно) либо копия при закрытых соединениях.
            var dest = Path.Combine(Dir, BackupPolicy.FileName(DateTime.Now));
            using (var src = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Paths.DbFile}"))
            {
                src.Open();
                using var cmd = src.CreateCommand();
                cmd.CommandText = $"VACUUM INTO '{dest.Replace("'", "''")}'";
                cmd.ExecuteNonQuery();
            }
            // Ротация: оставить 7 последних
            var all = ListAuto();
            foreach (var old in BackupPolicy.SelectForDeletion(all))
                try { File.Delete(old); } catch (Exception ex) { _log.Write("Backup", "DeleteOld: " + ex.Message); }
            _settings.LastBackupAt = DateTime.Now;
            return true;
        }
        catch (Exception ex) { _log.Write("Backup", "TryDailyBackup: " + ex.Message); return false; }
    }

    public (bool Ok, string Msg) RestoreFrom(string backupFile)
    {
        try
        {
            var check = Db.QuickCheck(backupFile);
            if (check.Trim() != "ok") return (false, "Копия повреждена: " + check);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Copy(backupFile, Paths.DbFile, overwrite: true);
            return (true, "База восстановлена из копии.");
        }
        catch (Exception ex) { _log.Write("Restore", ex.Message); return (false, ex.Message); }
    }

    public (bool Ok, string Msg, string? Used) AutoRecover(string reason)
    {
        var newest = ListAuto().OrderByDescending(f => f).ToList();
        var valid = BackupPolicy.PickLastValid(newest, f => Db.QuickCheck(f).Trim() == "ok");
        if (valid == null) return (false, "Нет валидных резервных копий. " + reason, null);
        var (ok, msg) = RestoreFrom(valid);
        return (ok, msg + " Причина: " + reason, valid);
    }

    public void ChangeDirectory(string newDir)
    {
        var old = Dir;
        Directory.CreateDirectory(newDir);
        foreach (var f in ListAuto().Concat(ListManual()))
        {
            try
            {
                var dest = Path.Combine(newDir, Path.GetFileName(f));
                File.Copy(f, dest, overwrite: false);
                // не удаляем старую, если копия не удалась (§31)
                try { File.Delete(f); } catch { }
            }
            catch (Exception ex) { _log.Write("Backup", "Move: " + ex.Message); }
        }
        _settings.BackupDir = newDir;
    }
}

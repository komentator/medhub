using System.IO;
using System.IO.Compression;
using System.Text.Json;
using MedControl.Data;
using MedControl.Logging;
using MedControl.Settings;

namespace MedControl.Services;

// Перенос базы на другой ПК одним файлом .mcpack (zip):
// БД + фото + meta.json (IP ведущего компьютера, имя, время, версия).
// На новом ПК мастер первого запуска восстанавливает пакет, берёт IP ведущего
// и устанавливает с ним связь (первая синхронизация).
public sealed record TransferMeta(string LeaderIp, string LeaderName, string CreatedUtc, string App, int DbVersion);

public sealed class TransferService
{
    private readonly SettingsService _settings;
    private readonly LogService _log;
    private readonly string _dbFile;
    private readonly string _photosDir;

    public TransferService(SettingsService settings, LogService log, string? dbFile = null, string? photosDir = null)
    {
        _settings = settings;
        _log = log;
        _dbFile = dbFile ?? Paths.DbFile;
        _photosDir = photosDir ?? Paths.PhotosDir;
    }

    public (bool Ok, string Msg) CreatePack(string leaderIp, string destFile)
    {
        try
        {
            leaderIp = (leaderIp ?? "").Trim();
            if (string.IsNullOrEmpty(leaderIp)) return (false, "Укажите IP-адрес ведущего компьютера.");
            var tmp = Path.Combine(Path.GetTempPath(), $"mc_pack_{Guid.NewGuid():N}.db");
            try
            {
                using (var src = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbFile}"))
                {
                    src.Open();
                    using var cmd = src.CreateCommand();
                    cmd.CommandText = $"VACUUM INTO '{tmp.Replace("'", "''")}'";
                    cmd.ExecuteNonQuery();
                }
                if (Db.QuickCheck(tmp).Trim() != "ok") return (false, "База не прошла проверку, пакет не создан.");
                var meta = new TransferMeta(leaderIp, _settings.DeviceName, DateTime.UtcNow.ToString("O"), UpdateCheck.Short(UpdateCheck.Current()), 3);
                if (File.Exists(destFile)) File.Delete(destFile);
                using var zip = ZipFile.Open(destFile, ZipArchiveMode.Create);
                zip.CreateEntryFromFile(tmp, "data/medcontrol.db", CompressionLevel.Optimal);
                var metaBytes = JsonSerializer.SerializeToUtf8Bytes(meta);
                using (var ms = new MemoryStream(metaBytes))
                using (var es = zip.CreateEntry("meta.json").Open())
                    ms.CopyTo(es);
                if (Directory.Exists(_photosDir))
                {
                    foreach (var f in Directory.GetFiles(_photosDir))
                        zip.CreateEntryFromFile(f, "photos/" + Path.GetFileName(f), CompressionLevel.Optimal);
                }
                _log.Write("Transfer", $"Пакет создан: {destFile}, ведущий {leaderIp}");
                return (true, "Пакет готов:\n" + destFile);
            }
            finally { try { File.Delete(tmp); } catch { } }
        }
        catch (Exception ex) { _log.Write("Transfer", "CreatePack: " + ex.Message); return (false, ex.Message); }
    }

    // Пакет в память (для отправки по сети).
    public (bool Ok, string Msg, byte[]? Bytes) CreatePackBytes(string leaderIp)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mc_net_{Guid.NewGuid():N}.mcpack");
        try
        {
            var (ok, msg) = CreatePack(leaderIp, tmp);
            if (!ok) return (false, msg, null);
            return (true, msg, File.ReadAllBytes(tmp));
        }
        catch (Exception ex) { _log.Write("Transfer", "CreatePackBytes: " + ex.Message); return (false, ex.Message, null); }
        finally { try { File.Delete(tmp); } catch { } }
    }

    public (bool Ok, string Msg) RestorePackBytes(byte[] bytes)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mc_net_{Guid.NewGuid():N}.mcpack");
        try
        {
            File.WriteAllBytes(tmp, bytes);
            return RestorePack(tmp);
        }
        catch (Exception ex) { _log.Write("Transfer", "RestorePackBytes: " + ex.Message); return (false, ex.Message); }
        finally { try { File.Delete(tmp); } catch { } }
    }

    public (bool Ok, string Msg) RestorePack(string packFile)
    {
        try
        {
            using var zip = ZipFile.OpenRead(packFile);
            var dbEntry = zip.GetEntry("data/medcontrol.db");
            var metaEntry = zip.GetEntry("meta.json");
            if (dbEntry == null || metaEntry == null) return (false, "Это не пакет MedControl (.mcpack).");
            TransferMeta? meta;
            using (var ms = new MemoryStream())
            using (var es = metaEntry.Open())
            {
                es.CopyTo(ms);
                meta = JsonSerializer.Deserialize<TransferMeta>(ms.ToArray());
            }
            if (meta == null || string.IsNullOrWhiteSpace(meta.LeaderIp))
                return (false, "В пакете нет данных о ведущем компьютере.");

            var tmpDb = Path.Combine(Path.GetTempPath(), $"mc_restore_{Guid.NewGuid():N}.db");
            try
            {
                using (var fs = File.Create(tmpDb))
                using (var es = dbEntry.Open())
                    es.CopyTo(fs);
                if (Db.QuickCheck(tmpDb).Trim() != "ok") return (false, "База в пакете повреждена.");
                Directory.CreateDirectory(Path.GetDirectoryName(_dbFile)!);
                // Пул держит открытые хендлы файла — иначе копия/чтение увидят старую БД.
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                File.Copy(tmpDb, _dbFile, overwrite: true);
            }
            finally { try { File.Delete(tmpDb); } catch { } }

            // Фото: заменяем содержимое хранилища
            try
            {
                Directory.CreateDirectory(_photosDir);
                foreach (var f in Directory.GetFiles(_photosDir)) File.Delete(f);
                foreach (var e in zip.Entries)
                {
                    if (!e.FullName.StartsWith("photos/") || e.FullName.EndsWith("/")) continue;
                    var dest = Path.Combine(_photosDir, Path.GetFileName(e.FullName));
                    using var fs = File.Create(dest);
                    using var es = e.Open();
                    es.CopyTo(fs);
                }
            }
            catch (Exception ex) { _log.Write("Transfer", "Photos: " + ex.Message); }

            // Этот ПК — новый участник: свой ID/имя, адрес ведущего, флаг первой синхронизации.
            _settings.Set("DeviceId", Guid.NewGuid().ToString());
            _settings.Set("DeviceName", Environment.MachineName);
            _settings.PeerAddress = meta.LeaderIp;
            _settings.Set("PendingInitialSync", true.ToString());
            _log.Write("Transfer", $"Пакет принят от {meta.LeaderName} ({meta.LeaderIp})");
            return (true, $"База восстановлена.\nВедущий: {meta.LeaderName} ({meta.LeaderIp})\nСвязь будет установлена автоматически.");
        }
        catch (Exception ex) { _log.Write("Transfer", "RestorePack: " + ex.Message); return (false, ex.Message); }
    }
}

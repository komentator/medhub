using System.IO;
using MedControl.Data;

namespace MedControl.Settings;

// Настройки в AppSettings (Key/Value). Последние настройки печати запоминаем (§26).
public sealed class SettingsService
{
    private readonly Db _db;
    private readonly Dictionary<string, string> _cache = new();
    public SettingsService(Db db) { _db = db; Load(); }

    private void Load()
    {
        try
        {
            using var c = _db.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT Key,Value FROM AppSettings";
            using var r = cmd.ExecuteReader();
            while (r.Read()) _cache[r.GetString(0)] = r.GetString(1);
        }
        catch { }
    }

    public string Get(string key, string def = "") => _cache.TryGetValue(key, out var v) ? v : def;
    public int GetInt(string key, int def) => int.TryParse(Get(key), out var v) ? v : def;
    public bool GetBool(string key, bool def) => bool.TryParse(Get(key), out var v) ? v : def;

    public void Set(string key, string value)
    {
        _cache[key] = value;
        try
        {
            using var c = _db.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO AppSettings(Key,Value) VALUES($k,$v) ON CONFLICT(Key) DO UPDATE SET Value=$v";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    // Типизированные свойства
    public int MissedGraceMinutes { get => GetInt("MissedGraceMinutes", 60); set => Set("MissedGraceMinutes", value.ToString()); }
    public int VolumeDuckPercent { get => GetInt("VolumeDuckPercent", 10); set => Set("VolumeDuckPercent", value.ToString()); }
    public string Theme { get => Get("Theme", "Light"); set => Set("Theme", value); }
    public double UiFontSize { get => double.TryParse(Get("UiFontSize"), out var v) ? v : 14; set => Set("UiFontSize", value.ToString()); }
    public double ReminderFontSize { get => double.TryParse(Get("ReminderFontSize"), out var v) ? v : 18; set => Set("ReminderFontSize", value.ToString()); }
    public bool Autostart { get => GetBool("Autostart", false); set => Set("Autostart", value.ToString()); }
    public string BackupDir { get => Get("BackupDir", Data.Paths.BackupDir); set => Set("BackupDir", value); }
    public DateTime? LastBackupAt { get => DateTime.TryParse(Get("LastBackupAt"), out var v) ? v : null; set => Set("LastBackupAt", value?.ToString("O") ?? ""); }
    public bool WizardDone { get => GetBool("WizardDone", false); set => Set("WizardDone", value.ToString()); }
    public string DeviceId
    {
        get
        {
            var v = Get("DeviceId", "");
            if (string.IsNullOrEmpty(v)) { v = Guid.NewGuid().ToString(); Set("DeviceId", v); }
            return v;
        }
    }
    public string DeviceName { get => Get("DeviceName", Environment.MachineName); set => Set("DeviceName", value); }
    public bool SyncServerEnabled { get => GetBool("SyncServerEnabled", false); set => Set("SyncServerEnabled", value.ToString()); }
    public int SyncPort { get => GetInt("SyncPort", 47890); set => Set("SyncPort", value.ToString()); }
    public string SyncKey { get => Get("SyncKey", ""); set => Set("SyncKey", value); }
    public string PeerAddress { get => Get("PeerAddress", ""); set => Set("PeerAddress", value); }
    public bool PendingInitialSync { get => GetBool("PendingInitialSync", false); set => Set("PendingInitialSync", value.ToString()); }
    public string MeasurementInput { get => Get("MeasurementInput", "New"); set => Set("MeasurementInput", value); }
    public string ExcelComparePath { get => Get("ExcelComparePath", ""); set => Set("ExcelComparePath", value); }
    public string LastFileDir { get => Get("LastFileDir", ""); set => Set("LastFileDir", value); }
}

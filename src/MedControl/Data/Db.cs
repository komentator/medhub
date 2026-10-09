using System.IO;
using Microsoft.Data.Sqlite;

namespace MedControl.Data;

public sealed class Db : IDisposable
{
    private readonly string _connStr;
    public Db()
    {
        Paths.EnsureAll();
        _connStr = new SqliteConnectionStringBuilder
        {
            DataSource = Paths.DbFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        Initialize();
    }
    // Для тестов: произвольный файл
    public Db(string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        _connStr = new SqliteConnectionStringBuilder { DataSource = filePath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
        Initialize();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connStr);
        c.Open();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();
        }
        return c;
    }

    private void Initialize()
    {
        using var c = Open();
        int v;
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "PRAGMA user_version;";
            v = Convert.ToInt32(cmd.ExecuteScalar());
        }
        if (v < 1) MigrateToV1(c);
        if (v < 2) MigrateToV2(c);
        if (v < 3) MigrateToV3(c);
    }

    private static void MigrateToV1(SqliteConnection c)
    {
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Medications(
 Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Description TEXT NOT NULL DEFAULT '',
 IsActive INTEGER NOT NULL DEFAULT 1, Mode INTEGER NOT NULL DEFAULT 0,
 DaysOfWeek TEXT NOT NULL DEFAULT '', StartDate TEXT NULL, EndDate TEXT NULL);
CREATE TABLE IF NOT EXISTS MedicationDoses(
 Id TEXT PRIMARY KEY, MedicationId TEXT NOT NULL REFERENCES Medications(Id) ON DELETE CASCADE,
 Time TEXT NOT NULL, Dosage TEXT NOT NULL DEFAULT '');
CREATE TABLE IF NOT EXISTS MedicationIntakes(
 Id TEXT PRIMARY KEY, MedicationId TEXT NOT NULL, MedicationName TEXT NOT NULL,
 MedicationDescription TEXT NOT NULL DEFAULT '', Dosage TEXT NOT NULL DEFAULT '',
 ScheduledAt TEXT NOT NULL, ActualAt TEXT NULL, Status INTEGER NOT NULL,
 ConfirmMethod INTEGER NOT NULL, Comment TEXT NOT NULL DEFAULT '',
 IsDeleted INTEGER NOT NULL DEFAULT 0, CreatedAt TEXT NOT NULL,
 DeletedAt TEXT NULL, RestoredAt TEXT NULL);
CREATE INDEX IF NOT EXISTS IX_Intakes_Sched ON MedicationIntakes(MedicationId, ScheduledAt);
CREATE TABLE IF NOT EXISTS Tonometers(
 Id TEXT PRIMARY KEY, Name TEXT NOT NULL, IsDeleted INTEGER NOT NULL DEFAULT 0);
CREATE TABLE IF NOT EXISTS Measurements(
 Id TEXT PRIMARY KEY, TonometerId TEXT NOT NULL, TonometerName TEXT NOT NULL,
 TakenAt TEXT NOT NULL, Systolic INTEGER NOT NULL, Diastolic INTEGER NOT NULL,
 Pulse INTEGER NOT NULL, Comment TEXT NOT NULL DEFAULT '', CreatedAt TEXT NOT NULL,
 ModifiedAt TEXT NULL, IsDeleted INTEGER NOT NULL DEFAULT 0,
 DeletedAt TEXT NULL, RestoredAt TEXT NULL);
CREATE INDEX IF NOT EXISTS IX_Meas_Taken ON Measurements(TakenAt);
CREATE TABLE IF NOT EXISTS WellBeingRatings(
 Id TEXT PRIMARY KEY, Score INTEGER NOT NULL, Comment TEXT NOT NULL DEFAULT '',
 CreatedAt TEXT NOT NULL, ModifiedAt TEXT NULL, IsDeleted INTEGER NOT NULL DEFAULT 0,
 DeletedAt TEXT NULL, RestoredAt TEXT NULL);
CREATE INDEX IF NOT EXISTS IX_WB_Created ON WellBeingRatings(CreatedAt);
CREATE TABLE IF NOT EXISTS WellBeingLinks(
 WellBeingId TEXT NOT NULL, LinkedId TEXT NOT NULL, LinkedKind TEXT NOT NULL,
 PRIMARY KEY(WellBeingId, LinkedId));
CREATE TABLE IF NOT EXISTS AppSettings(Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS TechnicalLogs(
 Id INTEGER PRIMARY KEY AUTOINCREMENT, CreatedAt TEXT NOT NULL,
 Category TEXT NOT NULL, Message TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS IX_Logs_Created ON TechnicalLogs(CreatedAt);
PRAGMA user_version=1;";
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    // v2: времена показа окна и нажатия «Отложить» для «Подробнее» в приёмах.
    private static void MigrateToV2(SqliteConnection c)
    {
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
ALTER TABLE MedicationIntakes ADD COLUMN ShownAt TEXT NULL;
ALTER TABLE MedicationIntakes ADD COLUMN SnoozedAt TEXT NULL;
PRAGMA user_version=2;";
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    // v3: UpdatedAt для синхронизации, фото/инструкции лекарств, пиры синхронизации.
    private static void MigrateToV3(SqliteConnection c)
    {
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
ALTER TABLE Medications ADD COLUMN UpdatedAt TEXT NULL;
ALTER TABLE Medications ADD COLUMN PhotoPack TEXT NOT NULL DEFAULT '';
ALTER TABLE Medications ADD COLUMN PhotoPill TEXT NOT NULL DEFAULT '';
ALTER TABLE Medications ADD COLUMN PhotoInstruction TEXT NOT NULL DEFAULT '';
ALTER TABLE Medications ADD COLUMN InstructionText TEXT NOT NULL DEFAULT '';
ALTER TABLE MedicationDoses ADD COLUMN UpdatedAt TEXT NULL;
ALTER TABLE MedicationIntakes ADD COLUMN UpdatedAt TEXT NULL;
ALTER TABLE Tonometers ADD COLUMN UpdatedAt TEXT NULL;
ALTER TABLE Measurements ADD COLUMN UpdatedAt TEXT NULL;
ALTER TABLE WellBeingRatings ADD COLUMN UpdatedAt TEXT NULL;
UPDATE Measurements SET UpdatedAt=CreatedAt WHERE UpdatedAt IS NULL;
UPDATE MedicationIntakes SET UpdatedAt=CreatedAt WHERE UpdatedAt IS NULL;
UPDATE WellBeingRatings SET UpdatedAt=CreatedAt WHERE UpdatedAt IS NULL;
CREATE TABLE IF NOT EXISTS SyncPeers(
 DeviceId TEXT PRIMARY KEY, Name TEXT NOT NULL DEFAULT '',
 Address TEXT NOT NULL DEFAULT '', LastSyncAt TEXT NOT NULL DEFAULT '');
PRAGMA user_version=3;";
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public void Dispose() { }

    // Быстрая проверка целостности по требованию (§3): quick_check, не full integrity_check на каждый запуск.
    public static string QuickCheck(string file)
    {
        try
        {
            using var c = new SqliteConnection($"Data Source={file};Mode=ReadOnly");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA quick_check;";
            var r = Convert.ToString(cmd.ExecuteScalar());
            return r ?? "unknown";
        }
        catch (Exception ex) { return "error: " + ex.Message; }
    }

    public static bool IsCorruptionMessage(Exception ex)
    {
        var m = ex.Message.ToLowerInvariant();
        return m.Contains("corrupt") || m.Contains("malformed") || m.Contains("not a database");
    }
}

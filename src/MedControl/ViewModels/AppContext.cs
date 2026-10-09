using MedControl.Data;
using MedControl.Repositories;
using MedControl.Services;
using MedControl.Audio;
using MedControl.Backup;
using MedControl.ImportExport;
using MedControl.Reports;
using MedControl.Logging;
using MedControl.Settings;
using MedControl.Scheduling;
using MedControl.Sync;

namespace MedControl.ViewModels;

// Простая композиция зависимостей (без DI-контейнера — меньше зависимостей, проще поддержка).
public sealed class AppServices
{
    public Db Db { get; } = new();
    public LogService Log { get; }
    public SettingsService Settings { get; }
    public MedicationRepository Meds { get; }
    public IntakeRepository Intakes { get; }
    public MeasurementRepository Meas { get; }
    public WellBeingRepository Wb { get; }
    public VolumeService Volume { get; }
    public SoundService Sound { get; }
    public ReminderService Reminders { get; }
    public ReminderScheduler Scheduler { get; }
    public WellBeingService WellBeing { get; }
    public BackupService Backups { get; }
    public ExcelService Excel { get; }
    public ReportService Reports { get; }
    public SyncPeerRepository Peers { get; }
    public SyncService Sync { get; }
    public PhotoService Photos { get; }
    public TransferService Transfer { get; }
    public event Action? Synced;

    public AppServices()
    {
        Log = new LogService(Db);
        Settings = new SettingsService(Db);
        Meds = new MedicationRepository(Db);
        Intakes = new IntakeRepository(Db);
        Meas = new MeasurementRepository(Db);
        Wb = new WellBeingRepository(Db);
        Volume = new VolumeService(Log);
        Sound = new SoundService(Log);
        Reminders = new ReminderService(Meds, Intakes, Settings, Log, Volume, Sound);
        Scheduler = new ReminderScheduler(Reminders, Log);
        WellBeing = new WellBeingService(Wb, Meas, Intakes);
        Backups = new BackupService(Settings, Log);
        Excel = new ExcelService(Meas, Intakes, Wb, Log);
        Reports = new ReportService(Meas, Intakes, Wb, Log);
        Peers = new SyncPeerRepository(Db);
        Sync = new SyncService(Meds, Intakes, Meas, Wb, Peers, Settings, Log);
        Sync.Synced += () => Synced?.Invoke();
        Photos = new PhotoService(Log);
        Transfer = new TransferService(Settings, Log);
        Sync.Transfer = Transfer;
        Log.Write("App", "Запуск MedControl");
    }
}

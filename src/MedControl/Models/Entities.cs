namespace MedControl.Models;

public enum ScheduleMode { Daily, Weekly }
public enum IntakeStatus { Taken, TakenLate, Missed, Unplanned }
public enum ConfirmMethod { Reminder, Manual, GroupAuto, Unplanned, TakenLate }

public sealed class Medication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";          // до 100
    public string Description { get; set; } = "";   // без ограничения
    public bool IsActive { get; set; } = true;
    public ScheduleMode Mode { get; set; } = ScheduleMode.Daily;
    // дни недели для Weekly: 0=Пн..6=Вс (ISO). Храним CSV.
    public List<int> DaysOfWeek { get; set; } = new();
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }          // null = постоянно
    public List<MedicationDose> Doses { get; set; } = new();
    public DateTime? UpdatedAt { get; set; }        // для синхронизации (LWW)
    public string PhotoPackPath { get; set; } = "";       // фото упаковки (относ. путь в Photos)
    public string PhotoPillPath { get; set; } = "";       // фото таблетки
    public string PhotoInstructionPath { get; set; } = ""; // фото инструкции
    public string InstructionText { get; set; } = "";      // печатная инструкция
}

public sealed class MedicationDose
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MedicationId { get; set; }
    public TimeOnly Time { get; set; }
    public string Dosage { get; set; } = "";       // до 100, своя на время
    public DateTime? UpdatedAt { get; set; }
}

public sealed class MedicationIntake
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MedicationId { get; set; }
    // Snapshot на момент события (§9)
    public string MedicationName { get; set; } = "";
    public string MedicationDescription { get; set; } = "";
    public string Dosage { get; set; } = "";
    public DateTime ScheduledAt { get; set; }      // план; для внепланового = факт
    public DateTime? ActualAt { get; set; }        // факт подтверждения
    public IntakeStatus Status { get; set; }
    public ConfirmMethod ConfirmMethod { get; set; }
    public string Comment { get; set; } = "";
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? DeletedAt { get; set; }
    public DateTime? RestoredAt { get; set; }
    public DateTime? ShownAt { get; set; }    // когда окно напоминания впервые показано
    public DateTime? SnoozedAt { get; set; }  // когда нажали «Отложить» (последний раз)
    public DateTime? UpdatedAt { get; set; }  // для синхронизации
}

public sealed class Tonometer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";         // до 50
    public bool IsDeleted { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class Measurement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TonometerId { get; set; }
    public string TonometerName { get; set; } = ""; // snapshot
    public DateTime TakenAt { get; set; }
    public int Systolic { get; set; }
    public int Diastolic { get; set; }
    public int Pulse { get; set; }
    public string Comment { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? ModifiedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime? RestoredAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class WellBeingRating
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Score { get; set; }                 // 1..10
    public string Comment { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? ModifiedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime? RestoredAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class WellBeingLink
{
    public Guid WellBeingId { get; set; }
    public Guid LinkedId { get; set; }             // Measurement.Id или MedicationIntake.Id
    public string LinkedKind { get; set; } = "";   // "Measurement" | "Intake"
}

public sealed class TechnicalLog
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
}

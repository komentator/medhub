using MedControl.Models;

namespace MedControl.Scheduling;

// Виртуальное плановое событие (не хранится в БД, генерируется на лету).
public sealed record PlannedDose(
    Guid MedicationId,
    Guid DoseId,
    string MedicationName,
    string Description,
    string Dosage,
    DateTime ScheduledAt);

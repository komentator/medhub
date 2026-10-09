using MedControl.Models;
using MedControl.Repositories;
using MedControl.Scheduling;
using MedControl.Settings;
using MedControl.Audio;
using MedControl.Logging;

namespace MedControl.Services;

// Бизнес-логика приёмов: виртуальные события + факты в History (§9–§13).
public sealed class ReminderService
{
    private readonly MedicationRepository _meds;
    private readonly IntakeRepository _intakes;
    private readonly SettingsService _settings;
    private readonly LogService _log;
    public readonly VolumeService Volume;
    public readonly SoundService Sound;

    // Отложенные группы: ключ — ScheduledAt первой дозы группы.
    // FireAt — когда окно должно появиться снова (сейчас + 15 мин, §10.2).
    private sealed record SnoozeEntry(List<PlannedDose> Doses, DateTime FireAt);
    private readonly Dictionary<DateTime, SnoozeEntry> _snoozed = new();
    // Когда окно напоминания впервые показано для дозы (для «Подробнее»).
    private readonly Dictionary<(Guid Med, DateTime Sched), DateTime> _shownAt = new();
    // Когда пользователь нажал «Отложить» для дозы (для «Подробнее»).
    private readonly Dictionary<(Guid Med, DateTime Sched), DateTime> _snoozePressed = new();
    public event Action? Changed;

    public ReminderService(MedicationRepository meds, IntakeRepository intakes, SettingsService settings, LogService log, VolumeService volume, SoundService sound)
    { _meds = meds; _intakes = intakes; _settings = settings; _log = log; Volume = volume; Sound = sound; }

    public TimeSpan Grace => TimeSpan.FromMinutes(_settings.MissedGraceMinutes <= 0 ? 60 : _settings.MissedGraceMinutes);

    // Снижение громкости по настройке пользователя (§11, §35).
    public void DuckVolume() => Volume.Duck(Math.Clamp(_settings.VolumeDuckPercent, 0, 50));

    // Сколько отложенных групп ещё не закрыто (нужно планировщику для Restore).
    public int SnoozedCount => _snoozed.Count;

    public List<Medication> Medications => _meds.GetAll();

    // Все незакрытые виртуальные события на [now-12ч, now+24ч] минус уже записанные.
    public List<PlannedDose> GetPending(DateTime now)
    {
        var meds = _meds.GetAll();
        var occ = ScheduleCalculator.GetOccurrences(meds, now.AddHours(-12), now.AddDays(1).AddMinutes(1));
        var hist = _intakes.GetRange(now.AddHours(-12), now.AddDays(1).AddMinutes(1));
        var res = new List<PlannedDose>();
        foreach (var o in occ)
        {
            if (o.ScheduledAt > now) { res.Add(o); continue; }
            // прошедшее: если уже записано — пропускаем, иначе это кандидат (активное/пропущенное)
            if (ScheduleCalculator.DoseAlreadyRecorded(hist, o.MedicationId, o.ScheduledAt)) continue;
            res.Add(o);
        }
        // Добавляем отложенные повторы; уже записанные (подтверждены/пропущены) — вычищаем
        foreach (var kv in _snoozed.ToList())
        {
            kv.Value.Doses.RemoveAll(d => ScheduleCalculator.DoseAlreadyRecorded(hist, d.MedicationId, d.ScheduledAt));
            if (kv.Value.Doses.Count == 0) { _snoozed.Remove(kv.Key); continue; }
            foreach (var d in kv.Value.Doses)
            {
                if (!res.Any(r => r.MedicationId == d.MedicationId && r.ScheduledAt == d.ScheduledAt))
                    res.Add(d);
            }
        }
        res.Sort((a, b) => a.ScheduledAt.CompareTo(b.ScheduledAt));
        return res;
    }

    // Активные (наступившие, но ещё не пропущенные) на now.
    // Отложенная доза активна только после своего FireAt (иначе окно всплывало бы сразу, а не через 15 мин).
    public List<PlannedDose> GetActive(DateTime now)
        => GetPending(now).Where(d => d.ScheduledAt <= now && !ScheduleCalculator.IsMissed(d.ScheduledAt, now, Grace) && !IsSnoozedNotDue(d, now)).ToList();

    private bool IsSnoozedNotDue(PlannedDose d, DateTime now)
    {
        foreach (var kv in _snoozed)
            if (kv.Value.FireAt > now && kv.Value.Doses.Any(x => x.MedicationId == d.MedicationId && x.ScheduledAt == d.ScheduledAt))
                return true;
        return false;
    }

    // Ближайшее время повтора отложенных (для таймера планировщика).
    public DateTime? GetNextSnoozeFire(DateTime now)
    {
        DateTime? best = null;
        foreach (var kv in _snoozed)
            if (kv.Value.FireAt > now && (best == null || kv.Value.FireAt < best))
                best = kv.Value.FireAt;
        return best;
    }

    // Окно напоминания показано: фиксируем время первого показа для каждой дозы.
    public void MarkShown(IEnumerable<PlannedDose> doses, DateTime at)
    {
        foreach (var d in doses)
            _shownAt.TryAdd((d.MedicationId, d.ScheduledAt), at);
    }

    // Пересчёт пропущенных: для всех прошедших beyond grace без записи — создать запись Missed.
    public int RecalcMissed(DateTime now)
    {
        int n = 0;
        try
        {
            var meds = _meds.GetAll();
            var from = now.AddDays(-2);
            var occ = ScheduleCalculator.GetOccurrences(meds, from, now);
            var hist = _intakes.GetRange(from, now.AddMinutes(1));
            foreach (var o in occ)
            {
                if (o.ScheduledAt > now) continue;
                if (ScheduleCalculator.DoseAlreadyRecorded(hist, o.MedicationId, o.ScheduledAt)) continue;
                if (IsSnoozedNotDue(o, now)) continue; // отложена — ещё не пропущена
                if (!ScheduleCalculator.IsMissed(o.ScheduledAt, now, Grace)) continue;
                var m = meds.First(x => x.Id == o.MedicationId);
                _intakes.Upsert(new MedicationIntake
                {
                    MedicationId = o.MedicationId,
                    MedicationName = m.Name, MedicationDescription = m.Description, Dosage = o.Dosage,
                    ScheduledAt = o.ScheduledAt, ActualAt = null,
                    Status = IntakeStatus.Missed, ConfirmMethod = ConfirmMethod.Manual,
                    CreatedAt = now,
                });
                n++;
            }
            if (n > 0) Changed?.Invoke();
        }
        catch (Exception ex) { _log.Write("Reminder", "RecalcMissed: " + ex.Message); }
        return n;
    }

    public void Confirm(PlannedDose dose, DateTime actual, ConfirmMethod method, bool late)
    {
        var meds = _meds.GetAll();
        var m = meds.FirstOrDefault(x => x.Id == dose.MedicationId);
        var status = late ? IntakeStatus.TakenLate : dose.ScheduledAt.Date == actual.Date && (actual - dose.ScheduledAt) >= Grace ? IntakeStatus.TakenLate : IntakeStatus.Taken;
        // Внеплановый отдельно
        _intakes.Upsert(new MedicationIntake
        {
            MedicationId = dose.MedicationId,
            MedicationName = m?.Name ?? dose.MedicationName,
            MedicationDescription = m?.Description ?? dose.Description,
            Dosage = dose.Dosage,
            ScheduledAt = dose.ScheduledAt,
            ActualAt = actual,
            Status = method == ConfirmMethod.Unplanned ? IntakeStatus.Unplanned : status,
            ConfirmMethod = method,
            CreatedAt = actual,
            ShownAt = _shownAt.TryGetValue((dose.MedicationId, dose.ScheduledAt), out var sh) ? sh : null,
            SnoozedAt = _snoozePressed.TryGetValue((dose.MedicationId, dose.ScheduledAt), out var sn) ? sn : null,
        });
        _shownAt.Remove((dose.MedicationId, dose.ScheduledAt));
        _snoozePressed.Remove((dose.MedicationId, dose.ScheduledAt));
        // Подтверждение снимает отложенность только своей дозы; чужие группы не трогаем.
        foreach (var kv in _snoozed.ToList())
        {
            kv.Value.Doses.RemoveAll(d => d.MedicationId == dose.MedicationId && d.ScheduledAt == dose.ScheduledAt);
            if (kv.Value.Doses.Count == 0) _snoozed.Remove(kv.Key);
        }
        Changed?.Invoke();
    }

    // Групповое подтверждение одной кнопкой «Принял» (§10.1).
    public void ConfirmGroup(IEnumerable<PlannedDose> group, DateTime actual, ConfirmMethod method = ConfirmMethod.Reminder)
    {
        foreach (var d in group) Confirm(d, actual, method, late: false);
        _snoozed.Remove(group.First().ScheduledAt);
        Changed?.Invoke();
    }

    public void SnoozeGroup(List<PlannedDose> group, DateTime? fireAt = null)
    {
        var now = DateTime.Now;
        var key = group[0].ScheduledAt;
        _snoozed[key] = new SnoozeEntry(group, fireAt ?? now.AddMinutes(15));
        foreach (var d in group)
            _snoozePressed[(d.MedicationId, d.ScheduledAt)] = now;
        Changed?.Invoke();
    }

    public void CancelSnoozedContaining(Guid medicationId, DateTime scheduledAt, DateTime manualTime)
    {
        // §10.3: ручное подтверждение одного из отложенной группы отменяет всю группу,
        // остальные получают «Принял» с тем же фактическим временем.
        foreach (var kv in _snoozed.ToList())
        {
            if (kv.Value.Doses.Any(d => d.MedicationId == medicationId && d.ScheduledAt == scheduledAt))
            {
                foreach (var d in kv.Value.Doses.ToList())
                {
                    if (d.MedicationId == medicationId && d.ScheduledAt == scheduledAt) continue;
                    Confirm(d, manualTime, ConfirmMethod.GroupAuto, late: false);
                }
                _snoozed.Remove(kv.Key);
            }
        }
    }

    public void ConfirmUnplanned(Guid medId, string dosage, DateTime actual)
    {
        var m = _meds.GetAll().FirstOrDefault(x => x.Id == medId);
        _intakes.Upsert(new MedicationIntake
        {
            MedicationId = medId,
            MedicationName = m?.Name ?? "?", MedicationDescription = m?.Description ?? "",
            Dosage = dosage, ScheduledAt = actual, ActualAt = actual,
            Status = IntakeStatus.Unplanned, ConfirmMethod = ConfirmMethod.Unplanned,
            CreatedAt = actual,
        });
        Changed?.Invoke();
    }

    public void MarkTakenLate(Guid intakeId, DateTime actual)
    {
        var all = _intakes.GetRange(DateTime.Now.AddYears(-5), DateTime.Now.AddDays(1), false);
        var it = all.FirstOrDefault(x => x.Id == intakeId);
        if (it == null) return;
        it.Status = IntakeStatus.TakenLate;
        it.ActualAt = actual;
        it.ConfirmMethod = ConfirmMethod.Manual;
        _intakes.Upsert(it);
        // §10.3: ручное подтверждение гасит отложенную группу, остальные — «Принял» тем же временем
        CancelSnoozedContaining(it.MedicationId, it.ScheduledAt, actual);
        Changed?.Invoke();
    }
}

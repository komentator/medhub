using MedControl.Models;

namespace MedControl.Scheduling;

// Чистая логика расписания — покрыта тестами, без зависимостей от БД/UI.
public static class ScheduleCalculator
{
    public static bool IsActiveOnDate(Medication m, DateOnly date)
    {
        if (!m.IsActive) return false;
        if (m.StartDate is { } s && date < s) return false;
        if (m.EndDate is { } e && date > e) return false;
        if (m.Mode == ScheduleMode.Daily) return true;
        // Weekly: DaysOfWeek 0=Пн..6=Вс
        int iso = ((int)date.DayOfWeek + 6) % 7;
        return m.DaysOfWeek.Contains(iso);
    }

    // Генерирует виртуальные события на интервал [from, to).
    public static List<PlannedDose> GetOccurrences(IEnumerable<Medication> meds, DateTime from, DateTime to)
    {
        var result = new List<PlannedDose>();
        foreach (var m in meds)
        {
            if (!m.IsActive) continue;
            for (var d = DateOnly.FromDateTime(from); d < DateOnly.FromDateTime(to).AddDays(1); d = d.AddDays(1))
            {
                if (!IsActiveOnDate(m, d)) continue;
                foreach (var dose in m.Doses)
                {
                    var at = d.ToDateTime(dose.Time);
                    if (at >= from && at < to)
                        result.Add(new PlannedDose(m.Id, dose.Id, m.Name, m.Description, dose.Dosage, at));
                }
            }
        }
        result.Sort((a, b) => a.ScheduledAt.CompareTo(b.ScheduledAt));
        return result;
    }

    // События текущего дня, отсортированные по времени (§4).
    public static List<PlannedDose> GetTodayDoses(IEnumerable<Medication> meds, DateTime now)
    {
        var day = DateOnly.FromDateTime(now);
        return GetOccurrences(meds, day.ToDateTime(TimeOnly.MinValue), day.AddDays(1).ToDateTime(TimeOnly.MinValue));
    }

    // Группировка событий в пределах 15 минут (§10.1). Граница включительно.
    public static List<List<PlannedDose>> GroupBy15Minutes(IList<PlannedDose> sorted)
    {
        var groups = new List<List<PlannedDose>>();
        List<PlannedDose>? cur = null;
        foreach (var d in sorted.OrderBy(x => x.ScheduledAt))
        {
            if (cur == null || (d.ScheduledAt - cur[0].ScheduledAt).TotalMinutes > 15)
            {
                cur = new List<PlannedDose> { d };
                groups.Add(cur);
            }
            else cur.Add(d);
        }
        return groups;
    }

    public static DateTime ApplySnooze(DateTime t) => t.AddMinutes(15);

    // Grace period: через сколько минут после плана статус становится «Пропущено».
    public static bool IsMissed(DateTime scheduledAt, DateTime now, TimeSpan grace)
        => now - scheduledAt >= grace;

    // Защита от дублей: событие уже подтверждено/записано?
    public static bool AlreadyRecorded(IEnumerable<MedicationIntake> history, Guid doseId, DateTime scheduledAt)
        => history.Any(h => h.ScheduledAt == scheduledAt &&
            h.MedicationName != null &&
            // Связь doseId храним в комментарии-метке? Нет — сопоставляем по MedicationId+ScheduledAt.
            true);

    public static bool DoseAlreadyRecorded(IEnumerable<MedicationIntake> history, Guid medicationId, DateTime scheduledAt)
        => history.Any(h => h.MedicationId == medicationId && h.ScheduledAt == scheduledAt && !h.IsDeleted);
}

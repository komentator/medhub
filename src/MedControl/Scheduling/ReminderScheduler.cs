using MedControl.Services;
using MedControl.Logging;

namespace MedControl.Scheduling;

// §41: без частого polling — таймер до ближайшего события.
public sealed class ReminderScheduler : IDisposable
{
    private readonly ReminderService _svc;
    private readonly LogService _log;
    private readonly System.Timers.Timer _timer = new() { AutoReset = false };
    private DateTime _lastSoundAt = DateTime.MinValue;
    public event Action<List<PlannedDose>>? Due;

    public ReminderScheduler(ReminderService svc, LogService log)
    {
        _svc = svc; _log = log;
        _timer.Elapsed += (_, _) => OnTick();
        _svc.Changed += Reschedule;
    }

    public void Start() => Reschedule();
    public void Reschedule()
    {
        try
        {
            _timer.Stop();
            var now = DateTime.Now;
            _svc.RecalcMissed(now);
            var pending = _svc.GetPending(now).Where(d => d.ScheduledAt > now.AddSeconds(-5)).OrderBy(d => d.ScheduledAt).ToList();
            DateTime? nextAt = pending.FirstOrDefault()?.ScheduledAt;
            var snoozeAt = _svc.GetNextSnoozeFire(now);
            if (snoozeAt != null && (nextAt == null || snoozeAt < nextAt)) nextAt = snoozeAt;
            if (nextAt == null)
            {
                // Нет будущих — перепроверить через 60 с (дешёвый fallback, не частый polling).
                _timer.Interval = 60_000;
                _timer.Start();
                return;
            }
            var ms = (nextAt.Value - now).TotalMilliseconds;
            _timer.Interval = Math.Clamp(ms <= 0 ? 100 : ms, 100, 3_600_000);
            _timer.Start();
        }
        catch (Exception ex) { _log.Write("Scheduler", "Reschedule: " + ex.Message); }
    }

    private void OnTick()
    {
        try
        {
            var now = DateTime.Now;
            _svc.RecalcMissed(now);
            var active = _svc.GetActive(now);
            if (active.Count > 0)
            {
                // Повтор звука через 10 мин, если не подтверждено (§11).
                if ((now - _lastSoundAt).TotalMinutes >= 10 || _lastSoundAt == DateTime.MinValue)
                {
                    _lastSoundAt = now;
                    _svc.Sound.Play();
                    _svc.DuckVolume();
                }
                Due?.Invoke(active);
            }
            else if (_svc.SnoozedCount == 0)
            {
                // Нет активных и нет отложенных — громкость должна быть исходной.
                // (Отложенное напоминание громкость не восстанавливает, §11.)
                _svc.Volume.Restore();
            }
        }
        catch (Exception ex) { _log.Write("Scheduler", "OnTick: " + ex.Message); }
        finally { Reschedule(); }
    }

    public void Dispose() { _timer.Dispose(); }
}

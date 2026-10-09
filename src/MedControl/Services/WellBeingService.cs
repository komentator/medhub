using MedControl.Models;
using MedControl.Repositories;

namespace MedControl.Services;

// §6. Создание/редактирование оценок, автолинковка ±15 мин, копирование комментария (§6.2).
public sealed class WellBeingService
{
    private readonly WellBeingRepository _wb;
    private readonly MeasurementRepository _meas;
    private readonly IntakeRepository _intakes;
    public WellBeingService(WellBeingRepository wb, MeasurementRepository meas, IntakeRepository intakes)
    { _wb = wb; _meas = meas; _intakes = intakes; }

    public WellBeingRating Create(int score, string comment, DateTime at)
    {
        var (ok, err) = ValidationService.ValidateWellBeing(score);
        if (!ok) throw new ArgumentException(err);
        var from = at.AddMinutes(-15).AddSeconds(-1);
        var to = at.AddMinutes(15).AddSeconds(1);
        var meas = _meas.GetRange(from, to).Select(m => ((m.Id, m.TakenAt), m.Comment)).ToList();
        var ints = _intakes.GetRange(from, to).Select(i => ((i.Id, i.ScheduledAt), i.Comment)).ToList();
        var links = WellBeingLinker.FindLinks(at, meas.Select(x => x.Item1), ints.Select(x => x.Item1));

        // §6.2: если комментария нет — первый найденный непустой по времени
        if (string.IsNullOrWhiteSpace(comment))
        {
            var ordered = new List<(DateTime At, string C)>();
            ordered.AddRange(meas.Select(x => (x.Item1.TakenAt, x.Comment)));
            ordered.AddRange(ints.Select(x => (x.Item1.ScheduledAt, x.Comment)));
            ordered.Sort((a, b) => a.At.CompareTo(b.At));
            var first = ordered.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.C));
            if (first.C != null) comment = first.C;
        }

        var wb = new WellBeingRating { Score = score, Comment = comment ?? "", CreatedAt = at };
        _wb.Upsert(wb);
        _wb.SetLinks(wb.Id, links.Select(l => (l.Id, l.Kind)));
        return wb;
    }

    public void Edit(Guid id, int score, string comment)
    {
        var all = _wb.GetRange(DateTime.Now.AddYears(-10), DateTime.Now.AddDays(1), false);
        var w = all.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Оценка не найдена.");
        bool changed = w.Score != score || w.Comment != (comment ?? "");
        w.Score = score; w.Comment = comment ?? "";
        if (changed) w.ModifiedAt = DateTime.Now; // только при реальном изменении (§19 по аналогии)
        _wb.Upsert(w);
    }
}

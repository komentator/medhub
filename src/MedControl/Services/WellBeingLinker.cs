namespace MedControl.Services;

// §6.3: связь оценки с записями в пределах ±15 минут.
public static class WellBeingLinker
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    public static List<(Guid Id, string Kind, DateTime At)> FindLinks(
        DateTime ratingAt,
        IEnumerable<(Guid Id, DateTime At)> measurements,
        IEnumerable<(Guid Id, DateTime At)> intakes)
    {
        var res = new List<(Guid Id, string Kind, DateTime At)>();
        foreach (var m in measurements)
            if (Math.Abs((m.At - ratingAt).TotalMinutes) <= 15)
                res.Add((m.Id, "Measurement", m.At));
        foreach (var i in intakes)
            if (Math.Abs((i.At - ratingAt).TotalMinutes) <= 15)
                res.Add((i.Id, "Intake", i.At));
        // §6.2: первый найденный непустой комментарий по времени
        res.Sort((a, b) => a.At.CompareTo(b.At));
        return res;
    }
}

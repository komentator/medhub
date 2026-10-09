namespace MedControl.ImportExport;

// Импорт без дублей (§24, §47).
public static class DedupHelper
{
    public static string MeasurementKey(DateTime takenAt, string tonometer, int sys, int dia, int pulse)
        => $"{takenAt:O}|{tonometer.Trim().ToLowerInvariant()}|{sys}|{dia}|{pulse}";

    public static string IntakeKey(Guid medicationId, DateTime scheduledAt, string dosage)
        => $"{medicationId}|{scheduledAt:O}|{(dosage ?? "").Trim().ToLowerInvariant()}";

    public static string WellBeingKey(DateTime createdAt, int score)
        => $"{createdAt:O}|{score}";

    public static List<T> OnlyNew<T>(IEnumerable<T> incoming, ISet<string> existingKeys, Func<T, string> keyOf)
    {
        var res = new List<T>();
        foreach (var item in incoming)
        {
            var k = keyOf(item);
            if (existingKeys.Add(k)) res.Add(item);
        }
        return res;
    }
}

namespace MedControl.Sync;

// Версионированные DTO (V=1). Имена полей — контракт, в т.ч. для будущего Android.
public sealed record RDose(string Id, string Time, string Dosage, string? Updated);
public sealed record RMed(string Id, string Name, string Desc, int Active, int Mode, string Days,
    string? Start, string? End, string? Updated,
    string PhotoPack, string PhotoPill, string PhotoInstr, string InstrText, List<RDose> Doses);
public sealed record RIntake(string Id, string MedId, string Name, string Desc, string Dosage,
    string Sched, string? Actual, int Status, int Method, string Comment, int Del, string Created,
    string? DeletedAt, string? RestoredAt, string? Shown, string? Snoozed, string? Updated);
public sealed record RTono(string Id, string Name, int Del, string? Updated);
public sealed record RMeas(string Id, string TonoId, string TonoName, string Taken, int Sys, int Dia, int Pulse,
    string Comment, string Created, string? Mod, int Del, string? DeletedAt, string? RestoredAt, string? Updated);
public sealed record RWb(string Id, int Score, string Comment, string Created, string? Mod, int Del,
    string? DeletedAt, string? RestoredAt, string? Updated);
public sealed record RLink(string Wb, string Linked, string Kind);

public sealed record SyncPayload(string V, string Device, string DeviceName, string Time,
    List<RMed> Meds, List<RIntake> Intakes, List<RTono> Tonos, List<RMeas> Meas, List<RWb> Wb, List<RLink> Links)
{
    public static SyncPayload Empty(string device, string name) =>
        new("1", device, name, DateTime.UtcNow.ToString("O"), new(), new(), new(), new(), new(), new());
}

public sealed record PullRequest(string Since);
public sealed record PushResult(bool Ok, string? Error, string Time);

// Чистые правила слияния (last-writer-wins) — покрыты тестами.
public static class SyncMerge
{
    public static DateTime T(string? iso)
    {
        if (string.IsNullOrEmpty(iso)) return DateTime.MinValue;
        return DateTime.TryParse(iso, out var d) ? d : DateTime.MinValue;
    }

    public static bool ChangedSince(string? updated, string? deletedAt, DateTime since)
        => T(updated) > since || T(deletedAt) > since;

    // Входящая запись побеждает, если её эффективное время (правка/удаление) новее.
    public static bool IncomingWins(string? incUpdated, string? incDeletedAt, string? locUpdated, string? locDeletedAt)
        => Max(T(incUpdated), T(incDeletedAt)) > Max(T(locUpdated), T(locDeletedAt));

    private static DateTime Max(DateTime a, DateTime b) => a >= b ? a : b;

    // Естественные ключи для подавления дублей одной и той же дозы/замера на двух ПК.
    public static string IntakeKey(string medId, string sched) => medId + "|" + sched;
    public static string MeasKey(string taken, string tono, int s, int d, int p) => $"{taken}|{tono}|{s}|{d}|{p}";
    public static string WbKey(string created, int score) => $"{created}|{score}";

    // Из группы дублей оставить ID с новейшим UpdatedAt.
    public static string PickWinner(IEnumerable<(string Id, string? Updated)> rows)
        => rows.OrderByDescending(r => T(r.Updated)).First().Id;
}

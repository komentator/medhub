using MedControl.Models;

namespace MedControl.Services;

public sealed record PressureStats(
    double? AvgSys, double? AvgDia, double? AvgPulse,
    int? MinSys, int? MaxSys, int? MinDia, int? MaxDia,
    int? MinPulse, int? MaxPulse, int Count);

public sealed record WellBeingStats(double? Avg, int? Min, int? Max, int Count);

public static class StatisticsService
{
    public static PressureStats Pressure(IEnumerable<Measurement> items)
    {
        var list = items.Where(m => !m.IsDeleted).ToList();
        if (list.Count == 0) return new PressureStats(null, null, null, null, null, null, null, null, null, 0);
        // 0/0 исключаем из средних по давлению, но считаем в количестве
        var sys = list.Where(m => !(m.Systolic == 0 && m.Diastolic == 0)).ToList();
        double? avgS = sys.Count > 0 ? sys.Average(m => (double)m.Systolic) : null;
        double? avgD = sys.Count > 0 ? sys.Average(m => (double)m.Diastolic) : null;
        var pulses = list.Where(m => m.Pulse != 0).ToList();
        double? avgP = pulses.Count > 0 ? pulses.Average(m => (double)m.Pulse) : null;
        return new PressureStats(
            avgS, avgD, avgP,
            sys.Count > 0 ? sys.Min(m => m.Systolic) : null,
            sys.Count > 0 ? sys.Max(m => m.Systolic) : null,
            sys.Count > 0 ? sys.Min(m => m.Diastolic) : null,
            sys.Count > 0 ? sys.Max(m => m.Diastolic) : null,
            pulses.Count > 0 ? pulses.Min(m => m.Pulse) : null,
            pulses.Count > 0 ? pulses.Max(m => m.Pulse) : null,
            list.Count);
    }

    public static WellBeingStats WellBeing(IEnumerable<WellBeingRating> items)
    {
        var list = items.Where(w => !w.IsDeleted).ToList();
        if (list.Count == 0) return new WellBeingStats(null, null, null, 0);
        return new WellBeingStats(list.Average(w => (double)w.Score), list.Min(w => w.Score), list.Max(w => w.Score), list.Count);
    }
}

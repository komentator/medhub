using System.IO;
using MedControl.Models;
using MedControl.Scheduling;
using MedControl.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace MedControl.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppServices _ctx;
    public MainViewModel(AppServices ctx)
    {
        _ctx = ctx;
        PeriodDays = 30;
        Refresh();
        _ctx.Reminders.Changed += Refresh;
    }

    private int _periodDays;
    public int PeriodDays { get => _periodDays; set { Set(ref _periodDays, value); Refresh(); } }

    private List<PlannedDose> _today = new();
    public List<PlannedDose> Today { get => _today; set => Set(ref _today, value); }

    private string _todayText = "";
    public string TodayText { get => _todayText; set => Set(ref _todayText, value); }

    private string _stats = "";
    public string Stats { get => _stats; set => Set(ref _stats, value); }

    public ISeries[] SysSeries { get; set; } = [];
    public ISeries[] DiaSeries { get; set; } = [];
    public ISeries[] PulseSeries { get; set; } = [];
    public ISeries[] WbSeries { get; set; } = [];

    // Цвета графиков (текст тултипа красится в тот же цвет).
    public SKColor SysColor { get; } = new(255, 107, 107);   // верхнее — красный
    public SKColor DiaColor { get; } = new(255, 169, 77);    // нижнее — оранжевый
    public SKColor PulseColor { get; } = new(105, 219, 124); // пульс — зелёный
    public SKColor WbColor { get; } = new(232, 130, 246);    // самочувствие — сиреневый

    private static LineSeries<double> Colored(double[] values, string name, int geo, SKColor color) =>
        new()
        {
            Values = values,
            Name = name,
            GeometrySize = geo,
            Stroke = new SolidColorPaint(color) { StrokeThickness = 2 },
            Fill = new SolidColorPaint(color.WithAlpha(35)),
            GeometryStroke = new SolidColorPaint(color) { StrokeThickness = 2 },
            GeometryFill = new SolidColorPaint(color),
        };

    // Яркий цвет тонометра (один на всех графиках).
    private static readonly SKColor[] TonoPalette =
    [
        new(224, 49, 49), new(25, 113, 194), new(47, 158, 68), new(240, 140, 0),
        new(156, 54, 181), new(12, 133, 153), new(214, 51, 108), new(121, 85, 72),
        new(90, 90, 200), new(80, 160, 40),
    ];

    // Серии по тонометрам: ось X — distinct-моменты времени.
    // Замеры в одно и то же время стоят на одной вертикали, каждый со своим значением.
    // Линии различаются цветом + толщиной + размером точек.
    private static ISeries[] PerTono(List<Measurement> pool, Func<Measurement, double> pick,
        Dictionary<Guid, List<int>> wb)
    {
        var times = pool.Select(m => m.TakenAt).Distinct().OrderBy(t => t).ToList();
        var tonos = pool.Select(m => m.TonometerName).Distinct().OrderBy(n => n).ToList();
        var res = new List<ISeries>();
        for (int i = 0; i < tonos.Count; i++)
        {
            var color = TonoPalette[i % TonoPalette.Length];
            float thick = 2 + (i % 3); // 2, 3, 4
            var tids = new List<int>(); // глобальные индексы своих точек (для тултипа)
            var pts = new List<ObservablePoint>();
            for (int k = 0; k < times.Count; k++)
            {
                var own = pool.Where(m => m.TakenAt == times[k] && m.TonometerName == tonos[i]).ToList();
                if (own.Count == 0) continue;
                tids.Add(k);
                pts.Add(new ObservablePoint(k, own.Average(pick)));
            }
            var s = new LineSeries<ObservablePoint>
            {
                Values = pts.ToArray(),
                Name = tonos[i],
                GeometrySize = 6 + (i % 3) * 2, // 6, 8, 10
                Stroke = new SolidColorPaint(color) { StrokeThickness = thick },
                Fill = null,
                GeometryStroke = new SolidColorPaint(color) { StrokeThickness = thick },
                GeometryFill = new SolidColorPaint(color),
            };
            s.XToolTipLabelFormatter = point => TimeDate(times, GlobalIdx(tids, (int)point.Index));
            s.YToolTipLabelFormatter = point => TimeTip(times, pool, wb, GlobalIdx(tids, (int)point.Index));
            res.Add(s);
        }
        return res.ToArray();
    }

    // Снизу только целые номера (шаг MinStep=1, без принудительного шага —
    // иначе метки слипаются; MinLimit=0 убирает «-1» слева).
    // ВАЖНО: у каждого графика СВОЙ экземпляр оси — общий ломает отрисовку.
    public static Axis[] NewXAxes() => [new Axis
    {
        LabelsPaint = new SolidColorPaint(SKColors.Gray),
        TextSize = 12,
        MinStep = 1,
        MinLimit = 0,
    }];

    public void Refresh()
    {
        var now = DateTime.Now;
        var meds = _ctx.Meds.GetAll();
        var occ = ScheduleCalculator.GetTodayDoses(meds, now);
        var hist = _ctx.Intakes.GetRange(now.Date, now.Date.AddDays(1));
        var pending = occ.Where(o => !hist.Any(h => h.MedicationId == o.MedicationId && h.ScheduledAt == o.ScheduledAt)).ToList();
        Today = pending;
        TodayText = pending.Count == 0 ? "Активных напоминаний нет"
            : string.Join(Environment.NewLine, pending.Select(p => $"{p.ScheduledAt:HH:mm} — {p.MedicationName} ({p.Dosage})"));

        var from = PeriodDays >= 3650 ? new DateTime(2000, 1, 1) : now.AddDays(-PeriodDays);
        var meas = _ctx.Meas.GetRange(from, now.AddDays(1)).OrderBy(m => m.TakenAt).ToList();
        var wbs = _ctx.Wb.GetRange(from, now.AddDays(1)).OrderBy(w => w.CreatedAt).ToList();
        var st = StatisticsService.Pressure(meas);
        var wst = StatisticsService.WellBeing(wbs);
        Stats = $"Давление: ср. {st.AvgSys:0}/{st.AvgDia:0}, пульс {st.AvgPulse:0}, мин {st.MinSys}/{st.MinDia}/{st.MinPulse}, макс {st.MaxSys}/{st.MaxDia}/{st.MaxPulse}, N={st.Count}" +
                Environment.NewLine +
                $"Самочувствие: ср. {wst.Avg:0.0}, мин {wst.Min}, макс {wst.Max}, N={wst.Count}";

        var wbByMeas = _ctx.Wb.GetScoresByMeasurement(meas.Select(m => m.Id));
        // КАЖДЫЙ замер — своя точка. Замеры в пределах 5 минут считаются одним
        // временем только для подсказки (там данные всех тонометров сразу).
        var bp = meas.Where(m => !(m.Systolic == 0 && m.Diastolic == 0)).ToList();
        var pu = meas.Where(m => m.Pulse != 0).ToList();

        // Отдельная цветная линия на каждый тонометр, легенда — названия.
        SysSeries = PerTono(bp, m => m.Systolic, wbByMeas);
        DiaSeries = PerTono(bp, m => m.Diastolic, wbByMeas);
        PulseSeries = PerTono(pu, m => m.Pulse, wbByMeas);
        var wbList = wbs.ToList();
        var wbs2 = Colored(wbList.Select(w => (double)w.Score).ToArray(), "Самочувствие", 9, WbColor);
        wbs2.XToolTipLabelFormatter = point => WbDate(wbList, (int)point.Index);
        wbs2.YToolTipLabelFormatter = point => WbTip(wbList, (int)point.Index);
        WbSeries = [wbs2];
        Raise(nameof(SysSeries)); Raise(nameof(DiaSeries)); Raise(nameof(PulseSeries)); Raise(nameof(WbSeries));
    }

    private static int GlobalIdx(List<int> gidx, int local) => local < 0 || local >= gidx.Count ? -1 : gidx[local];

    // Тултип — одна строка без переносов (Skia не рисует \n) и только кириллическим шрифтом (см. TooltipTextPaint).
    // На одной вертикали — данные всех тонометров этого момента времени.
    private static string TimeDate(List<DateTime> times, int index)
        => index < 0 || index >= times.Count ? "" : times[index].ToString("dd.MM.yyyy HH:mm");

    private static string TimeTip(List<DateTime> times, List<Measurement> pool, Dictionary<Guid, List<int>> wb, int index)
    {
        if (index < 0 || index >= times.Count) return "";
        var at = times[index];
        var items = pool.Where(m => m.TakenAt == at).ToList();
        var parts = new List<string> { at.ToString("dd.MM HH:mm") };
        foreach (var g in items.GroupBy(m => m.TonometerName).OrderBy(g => g.Key))
        {
            string vals = $"{g.Average(m => (double)m.Systolic):0}/{g.Average(m => (double)m.Diastolic):0}";
            var pu = g.Where(m => m.Pulse != 0).ToList();
            if (pu.Count > 0) vals += $" п.{g.Average(m => (double)m.Pulse):0}";
            parts.Add($"{g.Key} {vals}");
        }
        var scores = items.Where(m => wb.ContainsKey(m.Id)).SelectMany(m => wb[m.Id]).Distinct().ToList();
        if (scores.Count > 0) parts.Add("сам. " + string.Join(",", scores));
        var com = items.Select(m => m.Comment).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
        if (com != null) parts.Add(Short(com, 40));
        return string.Join(" • ", parts);
    }

    private static string WbDate(List<WellBeingRating> pts, int index)
        => index < 0 || index >= pts.Count ? "" : pts[index].CreatedAt.ToString("dd.MM.yyyy HH:mm");

    private static string WbTip(List<WellBeingRating> pts, int index)
    {
        if (index < 0 || index >= pts.Count) return "";
        var w = pts[index];
        var c = string.IsNullOrWhiteSpace(w.Comment) ? "" : " • " + Short(w.Comment, 50);
        return $"самочувствие {w.Score}/10{c}";
    }

    private static string Short(string s, int max)
    {
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }
}

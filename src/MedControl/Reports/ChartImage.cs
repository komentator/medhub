using SkiaSharp;
using MedControl.Models;

namespace MedControl.Reports;

// Растровые графики для PDF-отчётов (рисуются данными, не зависят от окна).
public static class ChartImage
{
    private static readonly SKColor[] Palette =
    [
        SKColors.Red, SKColors.Blue, SKColors.Green, SKColors.Orange,
        SKColors.Purple, SKColors.Teal, SKColors.DeepPink, SKColors.Brown,
    ];

    private static SKTypeface Face()
    {
        try { return SKTypeface.FromFamilyName("Segoe UI"); }
        catch { return SKTypeface.Default; }
    }

    public static byte[] Pressure(List<Measurement> meas, int w = 1100, int h = 420, int maxPoints = 0)
    {
        var pts = meas.OrderBy(m => m.TakenAt).ToList();
        if (maxPoints > 0 && pts.Count > maxPoints) pts = pts.Skip(pts.Count - maxPoints).ToList();
        var tonos = pts.Select(m => m.TonometerName).Distinct().OrderBy(n => n).ToList();
        using var bmp = new SKBitmap(w, h);
        using var cv = new SKCanvas(bmp);
        cv.Clear(SKColors.White);
        using var font = Face();
        using var title = new SKPaint { Color = SKColors.Black, TextSize = 26, Typeface = font, IsAntialias = true };
        cv.DrawText("Давление и пульс", 20, 34, title);
        if (pts.Count == 0)
        {
            using var t = new SKPaint { Color = SKColors.Gray, TextSize = 22, Typeface = font };
            cv.DrawText("Нет данных", 20, 80, t);
            return Encode(bmp);
        }
        const int L = 70, R = 20, T = 90, B = 60;
        var vals = pts.Where(m => !(m.Systolic == 0 && m.Diastolic == 0))
            .SelectMany(m => new[] { (double)m.Systolic, (double)m.Diastolic })
            .Concat(pts.Where(m => m.Pulse != 0).Select(m => (double)m.Pulse)).ToList();
        double min = vals.Count == 0 ? 0 : vals.Min();
        double max = vals.Count == 0 ? 100 : vals.Max();
        if (min >= max) { min -= 5; max += 5; }
        min = Math.Floor((min - 5) / 10) * 10;
        max = Math.Ceiling((max + 5) / 10) * 10;
        var t0 = pts.First().TakenAt;
        var t1 = pts.Last().TakenAt;
        if (t1 <= t0) t1 = t0.AddHours(1);
        float X(DateTime t) => (float)(L + (w - L - R) * (t - t0).TotalSeconds / (t1 - t0).TotalSeconds);
        float Y(double v) => (float)(T + (h - T - B) * (1 - (v - min) / (max - min)));
        using var grid = new SKPaint { Color = new SKColor(220, 220, 220), StrokeWidth = 1 };
        using var axis = new SKPaint { Color = SKColors.Gray, TextSize = 18, Typeface = font };
        for (int i = 0; i <= 5; i++)
        {
            double v = min + (max - min) * i / 5;
            cv.DrawLine(L, Y(v), w - R, Y(v), grid);
            cv.DrawText($"{v:0}", 8, Y(v) + 6, axis);
        }
        for (int i = 0; i < 8; i++)
        {
            var t = t0 + (t1 - t0) * i / 7;
            cv.DrawText(t.ToString("dd.MM"), X(t) - 30, h - 20, axis);
        }
        for (int i = 0; i < tonos.Count; i++)
        {
            var color = Palette[i % Palette.Length];
            using var line = new SKPaint { Color = color, StrokeWidth = 3, IsAntialias = true };
            using var dot = new SKPaint { Color = color, IsAntialias = true };
            var own = pts.Where(m => m.TonometerName == tonos[i]).ToList();
            DrawLine(cv, own.Where(m => !(m.Systolic == 0 && m.Diastolic == 0)).Select(m => new SKPoint(X(m.TakenAt), Y(m.Systolic))).ToList(), line, dot);
            DrawLine(cv, own.Where(m => !(m.Systolic == 0 && m.Diastolic == 0)).Select(m => new SKPoint(X(m.TakenAt), Y(m.Diastolic))).ToList(), line, dot);
            using var pline = new SKPaint { Color = color.WithAlpha(150), StrokeWidth = 2, IsAntialias = true, PathEffect = SKPathEffect.CreateDash([8, 6], 0) };
            DrawLine(cv, own.Where(m => m.Pulse != 0).Select(m => new SKPoint(X(m.TakenAt), Y(m.Pulse))).ToList(), pline, dot);
        }
        using var leg = new SKPaint { Color = SKColors.Black, TextSize = 20, Typeface = font };
        float lx = L;
        foreach (var t in tonos.Take(6))
        {
            var c = Palette[tonos.IndexOf(t) % Palette.Length];
            using var p = new SKPaint { Color = c };
            cv.DrawRect(lx, 48, 26, 18, p);
            cv.DrawText(t, lx + 32, 64, leg);
            lx += 32 + leg.MeasureText(t) + 24;
        }
        return Encode(bmp);
    }

    public static byte[] WellBeing(List<WellBeingRating> list, int w = 1100, int h = 300, int maxPoints = 0)
    {
        var pts = list.OrderBy(x => x.CreatedAt).ToList();
        if (maxPoints > 0 && pts.Count > maxPoints) pts = pts.Skip(pts.Count - maxPoints).ToList();
        using var bmp = new SKBitmap(w, h);
        using var cv = new SKCanvas(bmp);
        cv.Clear(SKColors.White);
        using var font = Face();
        using var title = new SKPaint { Color = SKColors.Black, TextSize = 26, Typeface = font, IsAntialias = true };
        cv.DrawText("Самочувствие", 20, 34, title);
        if (pts.Count == 0)
        {
            using var t = new SKPaint { Color = SKColors.Gray, TextSize = 22, Typeface = font };
            cv.DrawText("Нет данных", 20, 80, t);
            return Encode(bmp);
        }
        const int L = 70, R = 20, T = 70, B = 60;
        var t0 = pts.First().CreatedAt;
        var t1 = pts.Last().CreatedAt;
        if (t1 <= t0) t1 = t0.AddHours(1);
        float X(DateTime t) => (float)(L + (w - L - R) * (t - t0).TotalSeconds / (t1 - t0).TotalSeconds);
        float Y(double v) => (float)(T + (h - T - B) * (1 - (v - 1) / 9.0));
        using var grid = new SKPaint { Color = new SKColor(220, 220, 220), StrokeWidth = 1 };
        using var axis = new SKPaint { Color = SKColors.Gray, TextSize = 18, Typeface = font };
        for (int v = 1; v <= 10; v += 3)
        {
            cv.DrawLine(L, Y(v), w - R, Y(v), grid);
            cv.DrawText($"{v}", 30, Y(v) + 6, axis);
        }
        using var line = new SKPaint { Color = SKColors.Purple, StrokeWidth = 3, IsAntialias = true };
        using var dot = new SKPaint { Color = SKColors.Purple, IsAntialias = true };
        DrawLine(cv, pts.Select(x => new SKPoint(X(x.CreatedAt), Y(x.Score))).ToList(), line, dot);
        return Encode(bmp);
    }

    private static void DrawLine(SKCanvas cv, List<SKPoint> pts, SKPaint line, SKPaint dot)
    {
        for (int i = 1; i < pts.Count; i++) cv.DrawLine(pts[i - 1], pts[i], line);
        foreach (var p in pts) cv.DrawCircle(p, 6, dot);
    }

    private static byte[] Encode(SKBitmap bmp)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }
}

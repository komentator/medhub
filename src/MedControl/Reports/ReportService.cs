using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using MedControl.Data;
using MedControl.Models;
using MedControl.Repositories;
using MedControl.Logging;

namespace MedControl.Reports;

// §25–26. Печать результатов + сохранение PDF без диалога в Reports\medkontrol_*.pdf, A4.
public sealed class ReportService
{
    private readonly MeasurementRepository _meas;
    private readonly IntakeRepository _intakes;
    private readonly WellBeingRepository _wb;
    private readonly LogService _log;
    public ReportService(MeasurementRepository meas, IntakeRepository intakes, WellBeingRepository wb, LogService log)
    { _meas = meas; _intakes = intakes; _wb = wb; _log = log; }

    private static void AddChartPage(PdfDocument doc, PdfPage tpl, XImage img)
    {
        var pg = doc.AddPage();
        pg.Size = tpl.Size;
        pg.Orientation = tpl.Orientation;
        var g = XGraphics.FromPdfPage(pg);
        double maxW = pg.Width.Point - 80;
        double h = maxW * img.PixelHeight / Math.Max(1, img.PixelWidth);
        double maxH = pg.Height.Point - 80;
        if (h > maxH)
        {
            maxW *= maxH / h;
            h = maxH;
        }
        g.DrawImage(img, 40, 40, maxW, h);
    }

    public string BuildPdf(DateTime from, DateTime to, bool landscape, int fontSize, string kind, bool charts = true)
    {
        try
        {
            Directory.CreateDirectory(Paths.ReportDir);
            var doc = new PdfDocument();
            doc.Info.Title = "MedControl отчёт";
            var page = doc.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            page.Orientation = landscape ? PdfSharp.PageOrientation.Landscape : PdfSharp.PageOrientation.Portrait;
            var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Arial", fontSize <= 0 ? 11 : fontSize);
            var small = new XFont("Arial", 9);
            var header = new XFont("Arial", (fontSize <= 0 ? 11 : fontSize) + 2, PdfSharp.Drawing.XFontStyleEx.Bold);
            double y = 40;
            void Line(string text, XFont f, int step = 14)
            {
                gfx.DrawString(text, f, XBrushes.Black, XUnit.FromPoint(40), XUnit.FromPoint(y));
                y += step;
            }
            Line($"MedControl — {kind}. Сформировано: {DateTime.Now:yyyy-MM-dd HH:mm}. Период: {from:yyyy-MM-dd} — {to:yyyy-MM-dd}", small);
            y += 10;
            if (kind is "measurements" or "joint")
            {
                Line("Измерения давления", header, 20);
                var rows = _meas.GetRange(from, to).OrderBy(m => m.TonometerName).ThenBy(m => m.TakenAt).Take(80).ToList();
                var scores = _wb.GetScoresByMeasurement(rows.Select(r => r.Id));
                string lastTono = "";
                foreach (var m in rows)
                {
                    if (y > page.Height.Point - 60) break;
                    if (m.TonometerName != lastTono)
                    {
                        lastTono = m.TonometerName;
                        y += 6;
                        Line($"Тонометр: {lastTono}", font, 18);
                    }
                    var sc = scores.TryGetValue(m.Id, out var s) ? string.Join(",", s) : "—";
                    Line($"{m.TakenAt:dd.MM.yyyy}  {m.TakenAt:HH:mm}  верхнее {m.Systolic}  нижнее {m.Diastolic}  пульс {m.Pulse}  самочувствие: {sc}", small);
                    if (!string.IsNullOrWhiteSpace(m.Comment))
                        Line($"Комментарий: {m.Comment}", small);
                }
                y += 10;
            }
            if (kind is "intakes" or "joint")
            {
                Line("Приём лекарств", header, 20);
                var rows = _intakes.GetRange(from, to).OrderBy(i => i.ScheduledAt).Take(80).ToList();
                foreach (var g in rows.GroupBy(i => i.ScheduledAt))
                {
                    if (y > page.Height.Point - 60) break;
                    Line($"{g.Key:dd.MM.yyyy HH:mm} — " + string.Join("; ", g.Select(i => $"{i.MedicationName} ({i.Dosage})")), font, 18);
                    foreach (var i in g)
                    {
                        if (y > page.Height.Point - 60) break;
                        string status = i.Status switch
                        {
                            IntakeStatus.Taken => "Принял",
                            IntakeStatus.TakenLate => "Принял позже",
                            IntakeStatus.Missed => "Пропущено",
                            _ => "Внеплановый",
                        };
                        Line($"Факт: {(i.ActualAt?.ToString("dd.MM HH:mm") ?? "—")}  Статус: {status}" +
                            (string.IsNullOrWhiteSpace(i.Comment) ? "" : $"  Комментарий: {i.Comment}"), small);
                    }
                }
                y += 10;
            }
            // Самочувствие отдельным разделом (§26)
            Line("Самочувствие:", header, 20);
            var wbs = _wb.GetRange(from, to).Take(40).ToList();
            foreach (var w in wbs)
            {
                if (y > page.Height.Point - 60) break;
                Line($"{w.CreatedAt:yyyy-MM-dd HH:mm} — {w.Score}/10" + (string.IsNullOrWhiteSpace(w.Comment) ? "" : $" ({w.Comment})"), small);
            }
            if (charts)
            {
                var streams = new List<MemoryStream>();
                try
                {
                    XImage Img(byte[] b)
                    {
                        var ms = new MemoryStream(b);
                        streams.Add(ms);
                        return XImage.FromStream(ms);
                    }
                    if (kind is "measurements" or "joint")
                    {
                        var allMeas = _meas.GetRange(from, to).OrderBy(m => m.TakenAt).ToList();
                        AddChartPage(doc, page, Img(ChartImage.Pressure(allMeas)));
                    }
                    AddChartPage(doc, page, Img(ChartImage.WellBeing(_wb.GetRange(from, to).ToList())));
                }
                catch (Exception ex) { _log.Write("PDF", "Charts: " + ex.Message); }
                finally { foreach (var s in streams) try { s.Dispose(); } catch { } }
            }
            var path = Path.Combine(Paths.ReportDir, $"medkontrol_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.pdf");
            doc.Save(path);
            return path;
        }
        catch (Exception ex) { _log.Write("PDF", ex.Message); throw; }
    }
}

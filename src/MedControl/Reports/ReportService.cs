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

    // Один альбомный лист: шапка + график(и). Если передан только один — он занимает весь лист.
    private static void AddCombinedChartsPage(PdfDocument doc, string header, XImage? pressure, XImage? wb, int fontSize)
    {
        var pg = doc.AddPage();
        pg.Size = PdfSharp.PageSize.A4;
        pg.Orientation = PdfSharp.PageOrientation.Landscape;
        var g = XGraphics.FromPdfPage(pg);
        var hf = new XFont("Arial", fontSize <= 0 ? 11 : fontSize);
        g.DrawString(header, hf, XBrushes.Black, XUnit.FromPoint(40), XUnit.FromPoint(36));
        double w = pg.Width.Point - 80;
        double y = 56;
        if (pressure != null && wb != null)
        {
            double half = (pg.Height.Point - y - 30) / 2;
            DrawFit(g, pressure, 40, y, w, half - 6);
            DrawFit(g, wb, 40, y + half, w, half - 6);
        }
        else
        {
            var only = pressure ?? wb;
            if (only != null) DrawFit(g, only, 40, y, w, pg.Height.Point - y - 30);
        }
    }

    private static void DrawFit(XGraphics g, XImage img, double x, double y, double maxW, double maxH)
    {
        double h = maxW * img.PixelHeight / Math.Max(1, img.PixelWidth);
        double w = maxW;
        if (h > maxH)
        {
            w *= maxH / h;
            h = maxH;
        }
        g.DrawImage(img, x, y, w, h);
    }

    public string BuildPdf(DateTime from, DateTime to, bool landscape, int fontSize, string kind, bool charts = true, int chartPoints = 0)
    {
        // Потоки картинок живут до конца Save (PdfSharp читает их лениво).
        var streams = new List<MemoryStream>();
        try
        {
            Directory.CreateDirectory(Paths.ReportDir);
            var doc = new PdfDocument();
            doc.Info.Title = "MedControl отчёт";
            bool tables = kind != "charts";
            PdfPage? page = null;
            XGraphics? gfx = null;
            if (tables)
            {
                page = doc.AddPage();
                page.Size = PdfSharp.PageSize.A4;
                page.Orientation = landscape ? PdfSharp.PageOrientation.Landscape : PdfSharp.PageOrientation.Portrait;
                gfx = XGraphics.FromPdfPage(page);
            }
            var font = new XFont("Arial", fontSize <= 0 ? 11 : fontSize);
            var small = new XFont("Arial", 9);
            var header = new XFont("Arial", (fontSize <= 0 ? 11 : fontSize) + 2, PdfSharp.Drawing.XFontStyleEx.Bold);
            double y = 40;
            void Line(string text, XFont f, int step = 14)
            {
                gfx!.DrawString(text, f, XBrushes.Black, XUnit.FromPoint(40), XUnit.FromPoint(y));
                y += step;
            }
            string kindName = kind switch
            {
                "measurements" => "Измерения",
                "intakes" => "Приём лекарств",
                "joint" => "Совместный отчёт",
                "charts" => "Графики",
                _ => kind,
            };
            if (tables)
            {
                Line($"MedControl — {kindName}. Сформировано: {DateTime.Now:yyyy-MM-dd HH:mm}. Период: {from:yyyy-MM-dd} — {to:yyyy-MM-dd}", small);
                y += 10;
            }
            if (tables && kind is "measurements" or "joint")
            {
                Line("Измерения давления", header, 20);
                var rows = _meas.GetRange(from, to).OrderBy(m => m.TonometerName).ThenBy(m => m.TakenAt).Take(80).ToList();
                var scores = _wb.GetScoresByMeasurement(rows.Select(r => r.Id));
                string lastTono = "";
                foreach (var m in rows)
                {
                    if (y > page!.Height.Point - 60) break;
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
            if (tables && kind is "intakes" or "joint")
            {
                Line("Приём лекарств", header, 20);
                var rows = _intakes.GetRange(from, to).OrderBy(i => i.ScheduledAt).Take(80).ToList();
                foreach (var g in rows.GroupBy(i => i.ScheduledAt))
                {
                    if (y > page!.Height.Point - 60) break;
                    Line($"{g.Key:dd.MM.yyyy HH:mm} — " + string.Join("; ", g.Select(i => $"{i.MedicationName} ({i.Dosage})")), font, 18);
                    foreach (var i in g)
                    {
                        if (y > page!.Height.Point - 60) break;
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
            List<WellBeingRating> wbs = new();
            if (tables)
            {
                Line("Самочувствие:", header, 20);
                wbs = _wb.GetRange(from, to).Take(40).ToList();
                foreach (var w in wbs)
                {
                    if (y > page!.Height.Point - 60) break;
                    Line($"{w.CreatedAt:yyyy-MM-dd HH:mm} — {w.Score}/10" + (string.IsNullOrWhiteSpace(w.Comment) ? "" : $" ({w.Comment})"), small);
                }
            }
            if (kind == "charts")
            {
                // Только графики: альбомные листы, по одному на часть данных.
                try
                {
                    XImage Img(byte[] b)
                    {
                        // Поток обязан быть расширяемым (иначе GetBuffer бросает исключение).
                        var ms = new MemoryStream();
                        ms.Write(b, 0, b.Length);
                        ms.Position = 0;
                        streams.Add(ms);
                        return XImage.FromStream(ms);
                    }
                    var allMeas = _meas.GetRange(from, to).OrderBy(m => m.TakenAt).ToList();
                    var allWb = _wb.GetRange(from, to).OrderBy(w => w.CreatedAt).ToList();
                    var measParts = WordReportService.Chunks(allMeas, chartPoints);
                    var wbParts = WordReportService.Chunks(allWb, chartPoints);

                    string head = $"MedControl — Графики. Сформировано: {DateTime.Now:yyyy-MM-dd HH:mm}. Период: {from:yyyy-MM-dd} — {to:yyyy-MM-dd}";
                    int total = measParts.Count + wbParts.Count;
                    int n = 0;
                    foreach (var part in measParts)
                    {
                        n++;
                        AddCombinedChartsPage(doc, $"{head}. Давление и пульс — часть {n} из {total}",
                            Img(ChartImage.Pressure(part)), null!, fontSize);
                    }
                    foreach (var part in wbParts)
                    {
                        n++;
                        AddCombinedChartsPage(doc, $"{head}. Самочувствие — часть {n} из {total}",
                            null!, Img(ChartImage.WellBeing(part)), fontSize);
                    }
                }
                catch (Exception ex) { _log.Write("PDF", "Charts: " + ex.Message); }
            }
            else if (charts)
            {
                try
                {
                    XImage Img(byte[] b)
                    {
                        var ms = new MemoryStream();
                        ms.Write(b, 0, b.Length);
                        ms.Position = 0;
                        streams.Add(ms);
                        return XImage.FromStream(ms);
                    }
                    if (kind is "measurements" or "joint")
                    {
                        var allMeas = _meas.GetRange(from, to).OrderBy(m => m.TakenAt).ToList();
                        foreach (var part in WordReportService.Chunks(allMeas, chartPoints))
                            AddChartPage(doc, page!, Img(ChartImage.Pressure(part)));
                    }
                    foreach (var part in WordReportService.Chunks(_wb.GetRange(from, to).OrderBy(w => w.CreatedAt).ToList(), chartPoints))
                        AddChartPage(doc, page!, Img(ChartImage.WellBeing(part)));
                }
                catch (Exception ex) { _log.Write("PDF", "Charts: " + ex.Message); }
            }
            var path = Path.Combine(Paths.ReportDir, $"medkontrol_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.pdf");
            doc.Save(path);
            return path;
        }
        catch (Exception ex) { _log.Write("PDF", ex.Message); throw; }
        finally { foreach (var s in streams) try { s.Dispose(); } catch { } }
    }
}

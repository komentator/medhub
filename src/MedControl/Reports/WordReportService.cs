using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MedControl.Data;
using MedControl.Models;
using MedControl.Repositories;
using MedControl.Logging;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace MedControl.Reports;

// Word (.docx) отчёт: таблицы (книжная A4) + графики (альбомная A4).
public sealed class WordReportService
{
    private readonly MeasurementRepository _meas;
    private readonly IntakeRepository _intakes;
    private readonly WellBeingRepository _wb;
    private readonly LogService _log;

    public WordReportService(MeasurementRepository meas, IntakeRepository intakes, WellBeingRepository wb, LogService log)
    {
        _meas = meas; _intakes = intakes; _wb = wb; _log = log;
    }

    // A4, twips (1 twip = 1/20 pt)
    private const int PageWP = 11906;          // книжная: ширина
    private const int PageHP = 16838;          // книжная: высота
    private const int PageWL = 16838;          // альбомная: ширина
    private const int PageHL = 11906;          // альбомная: высота
    private const int MarginX = 1134;
    private const int MarginY = 850;
    private const int UsableWP = PageWP - 2 * MarginX;   // 9638 — таблицы
    private const int UsableWL = PageWL - 2 * MarginX;   // 14570 — графики
    private const long EmuPerTwip = 635;                // 12700 EMU / 20 twips

    /// <summary>Сколько точек помещается на один график. Если в отчёте их больше — он разбивается на несколько графиков.</summary>
    public const int ChartPointsPerChart = 60;

    public string BuildDocx(DateTime from, DateTime to, string kind, bool charts = true, int chartPoints = ChartPointsPerChart)
    {
        Directory.CreateDirectory(Paths.ReportDir);
        // Имя с миллисекундами: несколько отчётов в одну секунду не должны затирать друг друга.
        var path = UniquePath(Path.Combine(Paths.ReportDir, $"medkontrol_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.docx"));
        try
        {
            using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
            var main = doc.AddMainDocumentPart();
            main.Document = new Document();
            var body = new Body();
            main.Document.Append(body);
            AddStyles(main);

            uint picId = 1;
            bool chartsOnly = kind == "charts";
            bool wantCharts = chartsOnly || charts;

            // ---------- помощники ----------
            static string Sz(double pt) => ((int)Math.Round(pt * 2)).ToString();

            RunProperties Props(double size, bool bold)
            {
                // Порядок элементов w:rPr важен для Word: rFonts → b → sz → szCs
                return new RunProperties(
                    new RunFonts { Ascii = "Arial", HighAnsi = "Arial", ComplexScript = "Arial" },
                    new Bold { Val = OnOffValue.FromBoolean(bold) },
                    new FontSize { Val = Sz(size) },
                    new FontSizeComplexScript { Val = Sz(size) });
            }

            void Para(string text, double size = 10, bool bold = false, JustificationValues? just = null, int after = 60)
            {
                var pPr = new ParagraphProperties(
                    new SpacingBetweenLines { After = after.ToString(), Line = "240", LineRule = LineSpacingRuleValues.Auto });
                if (just.HasValue) pPr.Append(new Justification { Val = just.Value });
                body.Append(new Paragraph(pPr,
                    new Run(Props(size, bold),
                        new Text(text ?? "") { Space = SpaceProcessingModeValues.Preserve })));
            }

            void Title(string text) => Para(text, 16, true, JustificationValues.Center, 40);
            void Subtitle(string text) => Para(text, 10, false, JustificationValues.Center, 40);
            void Heading(string text) => Para(text, 13, true, null, 80);

            void PageBreak() => body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })));

            // Граница секции: книжная — таблицы, альбомная — графики.
            void EndPortraitSection()
            {
                body.Append(new Paragraph(new ParagraphProperties(
                    new SectionProperties(
                        new PageSize { Width = PageWP, Height = PageHP, Orient = PageOrientationValues.Portrait },
                        new PageMargin { Top = MarginY, Bottom = MarginY, Left = (uint)MarginX, Right = (uint)MarginX, Header = 425, Footer = 425 }))));
            }

            TableCell Cell(string text, int width, bool bold, bool header = false)
            {
                // Порядок в w:tcPr: tcW → shd → vAlign
                var tcPr = new TableCellProperties(
                    new TableCellWidth { Width = width.ToString(), Type = TableWidthUnitValues.Dxa });
                if (header) tcPr.Append(new Shading { Val = ShadingPatternValues.Clear, Fill = "E8EEF7" });
                tcPr.Append(new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });
                var tc = new TableCell(tcPr);
                tc.Append(new Paragraph(
                    new ParagraphProperties(new SpacingBetweenLines { After = "20", Before = "20" }),
                    new Run(Props(9, bold),
                        new Text(text ?? "") { Space = SpaceProcessingModeValues.Preserve })));
                return tc;
            }

            // share: доли ширины в процентах (сумма 100)
            void Table(string[] headers, List<string[]> rows, int[] share)
            {
                int usable = chartsOnly ? UsableWL : UsableWP;
                var widths = new int[share.Length];
                int used = 0;
                for (int i = 0; i < share.Length; i++)
                {
                    widths[i] = usable * share[i] / 100;
                    used += widths[i];
                }
                widths[^1] += usable - used;   // остаток в последний столбец

                var t = new Table();
                t.Append(new TableProperties(
                    new TableWidth { Width = usable.ToString(), Type = TableWidthUnitValues.Dxa },
                    new TableBorders(
                        new TopBorder { Val = BorderValues.Single, Size = 4 },
                        new LeftBorder { Val = BorderValues.Single, Size = 4 },
                        new BottomBorder { Val = BorderValues.Single, Size = 4 },
                        new RightBorder { Val = BorderValues.Single, Size = 4 },
                        new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                        new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 }),
                    new TableLayout { Type = TableLayoutValues.Fixed }));
                var grid = new TableGrid();
                foreach (var w in widths) grid.Append(new GridColumn { Width = w.ToString() });
                t.Append(grid);

                var hr = new TableRow(new TableRowProperties(new TableHeader()));
                for (int i = 0; i < headers.Length; i++) hr.Append(Cell(headers[i], widths[i], true, true));
                t.Append(hr);

                foreach (var r in rows)
                {
                    var tr = new TableRow();
                    for (int i = 0; i < headers.Length; i++)
                        tr.Append(Cell(i < r.Length ? r[i] : "", widths[i], false));
                    t.Append(tr);
                }
                body.Append(t);
                body.Append(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "0" })));
            }

            // Моноширинный текстовый блок: каждый замер — с новой строки, колонки выровнены.
            void MonoLines(List<string> lines)
            {
                foreach (var line in lines)
                {
                    body.Append(new Paragraph(
                        new ParagraphProperties(
                            new SpacingBetweenLines { After = "0", Line = "200", LineRule = LineSpacingRuleValues.Auto }),
                        new Run(
                            new RunProperties(
                                new RunFonts { Ascii = "Courier New", HighAnsi = "Courier New", ComplexScript = "Courier New" },
                                new FontSize { Val = "18" },
                                new FontSizeComplexScript { Val = "18" }),
                            new Text(line) { Space = SpaceProcessingModeValues.Preserve })));
                }
                body.Append(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "60" })));
            }

            static string Fit(string s, int width)
            {
                s = (s ?? "").Replace('\r', ' ').Replace('\n', ' ');
                if (s.Length > width) s = s.Substring(0, Math.Max(0, width - 1)) + "…";
                return s.PadRight(width);
            }

            void Picture(byte[] png, string? caption)
            {
                if (png == null || png.Length < 24) return;
                var part = main.AddImagePart(ImagePartType.Png);
                using (var s = part.GetStream(FileMode.Create, FileAccess.Write)) s.Write(png, 0, png.Length);
                string relId = main.GetIdOfPart(part);

                int pw = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
                int ph = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
                if (pw <= 0 || ph <= 0) return;
                long cx = (long)UsableWL * EmuPerTwip;
                long cy = cx * ph / pw;

                var drawing = new Drawing(
                    new DW.Inline(
                        new DW.Extent { Cx = cx, Cy = cy },
                        new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                        new DW.DocProperties { Id = picId++, Name = "График" },
                        new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                        new A.Graphic(
                            new A.GraphicData(
                                new PIC.Picture(
                                    new PIC.NonVisualPictureProperties(
                                        new PIC.NonVisualDrawingProperties { Id = 0U, Name = "chart.png" },
                                        new PIC.NonVisualPictureDrawingProperties()),
                                    new PIC.BlipFill(
                                        new A.Blip { Embed = relId },
                                        new A.Stretch(new A.FillRectangle())),
                                    new PIC.ShapeProperties(
                                        new A.Transform2D(
                                            new A.Offset { X = 0L, Y = 0L },
                                            new A.Extents { Cx = cx, Cy = cy }),
                                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
                    {
                        DistanceFromTop = 0U,
                        DistanceFromBottom = 0U,
                        DistanceFromLeft = 0U,
                        DistanceFromRight = 0U
                    });

                body.Append(new Paragraph(
                    new ParagraphProperties(
                        new SpacingBetweenLines { After = "60" },
                        new Justification { Val = JustificationValues.Center }),
                    new Run(drawing)));
                if (!string.IsNullOrWhiteSpace(caption))
                    Para(caption, 9, false, JustificationValues.Center, 120);
            }

            // ---------- данные ----------
            var meas = _meas.GetRange(from, to).OrderBy(m => m.TakenAt).ToList();
            var wbs = _wb.GetRange(from, to).OrderBy(w => w.CreatedAt).ToList();
            var intakes = _intakes.GetRange(from, to).OrderBy(i => i.ScheduledAt).ToList();

            // ---------- шапка ----------
            Title($"MedControl — {KindName(kind)}");
            Subtitle($"Сформировано: {DateTime.Now:yyyy-MM-dd HH:mm}");
            Subtitle($"Период: {from:yyyy-MM-dd} — {to:yyyy-MM-dd}");
            body.Append(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "120" })));

            if (kind is "measurements" or "joint")
            {
                Heading("Измерения давления");
                if (meas.Count == 0)
                {
                    Para("За выбранный период измерений нет.", 10, false, null, 120);
                }
                else
                {
                    var scores = _wb.GetScoresByMeasurement(meas.Select(r => r.Id));
                    // Текстом: каждое измерение — с новой строки.
                    var lines = new List<string>
                    {
                        "Дата       Время   Давление  Пульс  Тонометр     Самочувствие  Комментарий"
                    };
                    foreach (var m in meas)
                    {
                        string sc = scores.TryGetValue(m.Id, out var s) ? string.Join(",", s) : "—";
                        lines.Add(
                            $"{Fit(m.TakenAt.ToString("dd.MM.yyyy"), 10)} {Fit(m.TakenAt.ToString("HH:mm"), 6)} " +
                            $"{Fit($"{m.Systolic}/{m.Diastolic}", 9)} {Fit(m.Pulse > 0 ? m.Pulse.ToString() : "—", 6)} " +
                            $"{Fit(m.TonometerName, 13)} {Fit(sc, 14)} {Fit(m.Comment, 40)}");
                    }
                    MonoLines(lines);

                    var valid = meas.Where(m => !(m.Systolic == 0 && m.Diastolic == 0)).ToList();
                    if (valid.Count > 0)
                    {
                        Para(
                            $"Всего измерений: {meas.Count}. Среднее давление: {valid.Average(m => m.Systolic):0}/{valid.Average(m => m.Diastolic):0}." +
                            (valid.Any(m => m.Pulse > 0) ? $" Средний пульс: {valid.Where(m => m.Pulse > 0).Average(m => m.Pulse):0}." : "") +
                            $" Мин: {valid.Min(m => m.Systolic)}/{valid.Min(m => m.Diastolic)}. Макс: {valid.Max(m => m.Systolic)}/{valid.Max(m => m.Diastolic)}.",
                            10, true, null, 160);
                    }
                }
            }

            if (kind is "intakes" or "joint")
            {
                Heading("Приём лекарств");
                if (intakes.Count == 0)
                {
                    Para("За выбранный период приёмов нет.", 10, false, null, 120);
                }
                else
                {
                    var rows = new List<string[]>();
                    foreach (var i in intakes)
                    {
                        rows.Add(new[]
                        {
                            i.ScheduledAt.ToString("dd.MM.yyyy"),
                            i.ScheduledAt.ToString("HH:mm"),
                            i.MedicationName ?? "",
                            i.Dosage ?? "",
                            StatusName(i.Status),
                            i.ActualAt?.ToString("dd.MM.yyyy HH:mm") ?? "—",
                            string.IsNullOrWhiteSpace(i.Comment) ? "" : i.Comment!,
                        });
                    }
                    Table(
                        new[] { "Дата", "Время", "Лекарство", "Дозировка", "Статус", "Факт", "Комментарий" },
                        rows,
                        new[] { 12, 9, 22, 13, 14, 17, 13 });

                    int taken = intakes.Count(i => i.Status == IntakeStatus.Taken || i.Status == IntakeStatus.TakenLate);
                    int missed = intakes.Count(i => i.Status == IntakeStatus.Missed);
                    Para($"Всего приёмов: {intakes.Count}. Принято: {taken}. Пропущено: {missed}.", 10, true, null, 160);
                }
            }

            if (kind is "measurements" or "joint" or "intakes")
            {
                Heading("Самочувствие");
                if (wbs.Count == 0)
                {
                    Para("За выбранный период оценок самочувствия нет.", 10, false, null, 120);
                }
                else
                {
                    var rows = new List<string[]>();
                    foreach (var w in wbs)
                    {
                        rows.Add(new[]
                        {
                            w.CreatedAt.ToString("dd.MM.yyyy"),
                            w.CreatedAt.ToString("HH:mm"),
                            w.Score.ToString(),
                            string.IsNullOrWhiteSpace(w.Comment) ? "" : w.Comment!,
                        });
                    }
                    Table(
                        new[] { "Дата", "Время", "Оценка", "Комментарий" },
                        rows,
                        new[] { 12, 9, 8, 71 });
                    if (wbs.Any(w => w.Score > 0))
                        Para($"Средняя оценка самочувствия: {wbs.Where(w => w.Score > 0).Average(w => w.Score):0.0} из 10.", 10, true, null, 160);
                }
            }

            // ---------- графики (альбомная секция) ----------
            if (wantCharts)
            {
                if (!chartsOnly) EndPortraitSection();   // таблицы закончились — дальше альбомная
                Heading("Графики");

                // Точек больше, чем помещается на один график, — разбиваем на части по chartPoints.
                var measParts = Chunks(meas, chartPoints);
                var wbParts = Chunks(wbs, chartPoints);
                int total = measParts.Count + wbParts.Count;
                int n = 0;

                foreach (var part in measParts)
                {
                    if (n > 0) PageBreak();
                    n++;
                    string range = part.Count > 0
                        ? $"{part[0].TakenAt:dd.MM.yyyy} — {part[^1].TakenAt:dd.MM.yyyy}"
                        : "";
                    string cap = PartCaption("Давление и пульс", n, total, range, part.Count);
                    Subtitle(cap);
                    Picture(ChartImage.Pressure(part), null);
                }

                foreach (var part in wbParts)
                {
                    PageBreak();
                    n++;
                    string range = part.Count > 0
                        ? $"{part[0].CreatedAt:dd.MM.yyyy} — {part[^1].CreatedAt:dd.MM.yyyy}"
                        : "";
                    string cap = PartCaption("Самочувствие", n, total, range, part.Count);
                    Subtitle(cap);
                    Picture(ChartImage.WellBeing(part), null);
                }
            }

            // ---------- параметры последней секции ----------
            if (chartsOnly)
            {
                body.Append(new SectionProperties(
                    new PageSize { Width = PageWL, Height = PageHL, Orient = PageOrientationValues.Landscape },
                    new PageMargin { Top = MarginY, Bottom = MarginY, Left = (uint)MarginX, Right = (uint)MarginX, Header = 425, Footer = 425 }));
            }
            else if (wantCharts)
            {
                body.Append(new SectionProperties(
                    new PageSize { Width = PageWL, Height = PageHL, Orient = PageOrientationValues.Landscape },
                    new PageMargin { Top = MarginY, Bottom = MarginY, Left = (uint)MarginX, Right = (uint)MarginX, Header = 425, Footer = 425 }));
            }
            else
            {
                body.Append(new SectionProperties(
                    new PageSize { Width = PageWP, Height = PageHP, Orient = PageOrientationValues.Portrait },
                    new PageMargin { Top = MarginY, Bottom = MarginY, Left = (uint)MarginX, Right = (uint)MarginX, Header = 425, Footer = 425 }));
            }

            main.Document.Save();
            return path;
        }
        catch (Exception ex)
        {
            _log.Write("DOCX", ex.Message);
            throw;
        }
    }

    /// <summary>Режет список на части по size точек (size = 0 — одна часть со всем).</summary>
    public static List<List<T>> Chunks<T>(List<T> list, int size)
    {
        var res = new List<List<T>>();
        if (list.Count == 0) { res.Add(list); return res; }
        if (size <= 0) { res.Add(list); return res; }
        for (int i = 0; i < list.Count; i += size)
            res.Add(list.GetRange(i, Math.Min(size, list.Count - i)));
        return res;
    }

    private static string PartCaption(string title, int index, int total, string range, int count)
    {
        string part = total > 1 ? $" — часть {index} из {total}" : "";
        string dates = string.IsNullOrWhiteSpace(range) ? "" : $" ({range})";
        return $"{title}{part}{dates} — {count} точек";
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        for (int i = 2; i < 100; i++)
        {
            var alt = Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}_{i}.docx");
            if (!File.Exists(alt)) return alt;
        }
        return path;
    }

    private static void AddStyles(MainDocumentPart main)
    {
        var part = main.AddNewPart<StyleDefinitionsPart>();
        part.Styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = "Arial", HighAnsi = "Arial", ComplexScript = "Arial" },
                    new FontSize { Val = "20" },
                    new FontSizeComplexScript { Val = "20" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                    new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }))),
            new Style(
                new StyleName { Val = "Normal" },
                new StyleParagraphProperties(
                    new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }),
                new StyleRunProperties(
                    new RunFonts { Ascii = "Arial", HighAnsi = "Arial", ComplexScript = "Arial" },
                    new FontSize { Val = "20" },
                    new FontSizeComplexScript { Val = "20" }))
            { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true });
        part.Styles.Save();
    }

    private static string KindName(string k) => k switch
    {
        "measurements" => "Измерения давления",
        "intakes" => "Приём лекарств",
        "joint" => "Совместный отчёт",
        "charts" => "Графики",
        _ => k,
    };

    private static string StatusName(IntakeStatus s) => s switch
    {
        IntakeStatus.Taken => "Принял",
        IntakeStatus.TakenLate => "Принял позже",
        IntakeStatus.Missed => "Пропущено",
        _ => "Внеплановый",
    };
}

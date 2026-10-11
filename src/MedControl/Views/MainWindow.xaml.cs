using System.IO;
using System.Windows;
using Microsoft.Win32;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using SelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using OpenFolderDialog = Microsoft.Win32.OpenFolderDialog;
using Application = System.Windows.Application;
using MedControl.Models;
using MedControl.ViewModels;
using MedControl.Services;

namespace MedControl.Views;

public partial class MainWindow : Window
{
    private readonly AppServices _ctx;
    private readonly MainViewModel _vm;
    public bool AllowClose { get; set; }
    public MainWindow(AppServices ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        Title = $"MedControl {UpdateCheck.Short(UpdateCheck.Current())}";
        _vm = new MainViewModel(ctx);
        VolumeSlider.ValueChanged += (_, _) => VolumeLabel.Text = $"{(int)VolumeSlider.Value}%";
        // Тултипы графиков кириллическим шрифтом (иначе Skia рисует «квадратики»),
        // текст — в цвет линии своего графика.
        var tips = new[]
        {
            (SysChart, _vm.SysColor), (DiaChart, _vm.DiaColor),
            (PulseChart, _vm.PulseColor), (WbChart, _vm.WbColor),
        };
        foreach (var (ch, color) in tips)
        {
            ch.TooltipTextPaint = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(color) { FontFamily = "Segoe UI" };
            ch.TooltipTextSize = 14;
            ch.LegendPosition = LiveChartsCore.Measure.LegendPosition.Top;
            ch.LegendTextPaint = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColors.DimGray) { FontFamily = "Segoe UI" };
            ch.LegendTextSize = 13;
            // Колесико мыши над графиком — растянуть/сузить время (число видимых замеров),
            // перетаскивание — сдвиг по времени.
            ch.ZoomMode = LiveChartsCore.Measure.ZoomAndPanMode.X;
            ch.ZoomingSpeed = 0.8;
        }
        LoadSettings();
        RefreshAll();
        Closing += (_, e) =>
        {
            if (AllowClose) return; // реальный выход (трей «Выход», удаление данных)
            // §37: закрытие окна — в трей, не завершать
            e.Cancel = true;
            Hide();
        };
    }

    public void GoSettings() { Tabs.SelectedIndex = 9; Show(); Activate(); }

    private void LoadSettings()
    {
        GraceBox.SelectedIndex = _ctx.Settings.MissedGraceMinutes switch { 15 => 0, 30 => 1, 60 => 2, 120 => 3, 180 => 4, 360 => 5, _ => 2 };
        VolumeSlider.Value = _ctx.Settings.VolumeDuckPercent;
        AutostartBox.IsChecked = AutostartService.IsEnabled();
        UiFontBox.Text = _ctx.Settings.UiFontSize.ToString("0");
        ReminderFontBox.Text = _ctx.Settings.ReminderFontSize.ToString("0");
        FontSize = _ctx.Settings.UiFontSize;
        foreach (var it in MeasureInputBox.Items)
            if (it is ComboBoxItem ci && ci.Tag?.ToString() == _ctx.Settings.MeasurementInput)
            { MeasureInputBox.SelectedItem = it; break; }
        ExcelCompareBox.Text = _ctx.Settings.ExcelComparePath;
        DeviceNameBox.Text = _ctx.Settings.DeviceName;
        SyncServerBox.IsChecked = _ctx.Settings.SyncServerEnabled;
        SyncPortBox.Text = _ctx.Settings.SyncPort.ToString();
        SyncKeyBox.Password = _ctx.Settings.SyncKey;
        PeerIpBox.Text = _ctx.Settings.PeerAddress;
        DeviceIdBox.Text = $"ID: {_ctx.Settings.DeviceId}\nIP этого ПК: {string.Join(", ", MedControl.Sync.SyncService.GetLocalIps())}";
    }

    public void RefreshAll()
    {
        _vm.Refresh();
        TodayBox.Text = _vm.TodayText;
        StatsBox.Text = _vm.Stats;
        SysChart.Series = _vm.SysSeries;
        DiaChart.Series = _vm.DiaSeries;
        PulseChart.Series = _vm.PulseSeries;
        WbChart.Series = _vm.WbSeries;
        SysChart.XAxes = MainViewModel.NewXAxes();
        DiaChart.XAxes = MainViewModel.NewXAxes();
        PulseChart.XAxes = MainViewModel.NewXAxes();
        WbChart.XAxes = MainViewModel.NewXAxes();
        OnRefreshMeas(null, null);
        OnRefreshIntakes(null, null);
        OnRefreshWb(null, null);
        OnRefreshTrash(null, null);
        RefreshTonos();
        RefreshMeds();
        OnRefreshInstructions(null, null);
        RefreshManualBackups();
        BackupInfo.Text = $"Последний backup: {_ctx.Settings.LastBackupAt?.ToString("yyyy-MM-dd HH:mm") ?? "—"}. Авто: {_ctx.Backups.ListAuto().Count}, ручных: {_ctx.Backups.ListManual().Count}. Папка: {_ctx.Backups.Dir}";
    }

    // ---------- Главная ----------
    private void OnPeriodChanged(object s, SelectionChangedEventArgs e)
    {
        // Событие может прийти во время InitializeComponent — тогда контролы/VM ещё не готовы.
        if (_vm == null || PeriodBox == null) return;
        if (PeriodBox.SelectedItem is ComboBoxItem ci && int.TryParse(ci.Tag?.ToString(), out int d))
        { _vm.PeriodDays = d; RefreshAll(); }
    }
    private void OnGotoMeasurements(object s, RoutedEventArgs e) => Tabs.SelectedIndex = 1;

    private void OnNewMeasurement(object s, RoutedEventArgs e)
    {
        var tonos = _ctx.Meas.GetTonometers();
        if (tonos.Count == 0)
        {
            if (MessageBox.Show("Нет тонометров. Перейти в настройки и добавить?", "MedControl", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                Tabs.SelectedIndex = 9;
            return;
        }
        if (_ctx.Settings.MeasurementInput == "Old")
        {
            if (MeasurementForm.Show(_ctx, this, null, historic: false)) RefreshAll();
        }
        else
        {
            if (MeasureMultiForm.Show(_ctx, this)) RefreshAll();
        }
    }
    private void OnNewHistoricMeasurement(object s, RoutedEventArgs e)
    {
        if (MeasurementForm.Show(_ctx, this, null, historic: true)) RefreshAll();
    }
    private void OnEditMeasurement(object s, RoutedEventArgs e)
    {
        if (MeasGrid.SelectedItem is MeasRow r)
        {
            var m = _ctx.Meas.GetRange(DateTime.Now.AddYears(-10), DateTime.Now.AddDays(1)).FirstOrDefault(x => x.Id == r.Id);
            if (m == null) return;
            if (MeasurementForm.Show(_ctx, this, m, historic: true)) RefreshAll();
        }
    }
    private void OnMeasDetails(object? s, EventArgs e)
    {
        if (MeasGrid.SelectedItem is MeasRow r)
        {
            var m = _ctx.Meas.GetRange(DateTime.Now.AddYears(-10), DateTime.Now.AddDays(1)).FirstOrDefault(x => x.Id == r.Id);
            if (m == null) return;
            MeasurementDetails.Show(_ctx, this, m);
        }
    }
    private void OnDeleteMeasurement(object s, RoutedEventArgs e)
    {
        if (MeasGrid.SelectedItem is MeasRow r &&
            MessageBox.Show("Переместить измерение в корзину?", "MedControl", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        {
            _ctx.Meas.MoveToTrash(r.Id);
            _ctx.Wb.DeleteLinksOf(r.Id);
            RefreshAll();
        }
    }

    // ---------- Измерения ----------
    private sealed record MeasRow(Guid Id, string Date, string Time, string Tonometer, int Sys, int Dia, int Pulse, string Wb, string Comment);
    private void OnRefreshMeas(object? s, RoutedEventArgs? e)
    {
        if (_ctx == null || MeasGrid == null) return;
        var q = (MeasSearch?.Text ?? "").Trim().ToLowerInvariant();
        var rows = _ctx.Meas.GetRange(DateTime.Now.AddYears(-10), DateTime.Now.AddDays(1)).OrderByDescending(m => m.TakenAt).ToList();
        if (!string.IsNullOrEmpty(q)) rows = rows.Where(m => m.TonometerName.ToLowerInvariant().Contains(q)).ToList();
        var scores = _ctx.Wb.GetScoresByMeasurement(rows.Select(r => r.Id));
        MeasGrid.ItemsSource = rows.Select(m => new MeasRow(m.Id, m.TakenAt.ToString("yyyy-MM-dd"), m.TakenAt.ToString("HH:mm"),
            m.TonometerName, m.Systolic, m.Diastolic, m.Pulse,
            scores.TryGetValue(m.Id, out var sc) ? string.Join(",", sc) : "", m.Comment)).ToList();
    }

    // ---------- Лекарства ----------
    private sealed record MedRow(Guid Id, string Name, string Active, string Mode, string Doses, string Photo, string Instr);
    private void RefreshMeds()
    {
        MedsGrid.ItemsSource = _ctx.Meds.GetAll().Select(m => new MedRow(m.Id, m.Name, m.IsActive ? "Да" : "Нет",
            m.Mode == ScheduleMode.Daily ? "Каждый день" : "По дням",
            string.Join("; ", m.Doses.Select(d => $"{d.Time:HH:mm} {d.Dosage}")),
            (!string.IsNullOrEmpty(m.PhotoPackPath) || !string.IsNullOrEmpty(m.PhotoPillPath)) ? "Да" : "—",
            (!string.IsNullOrEmpty(m.PhotoInstructionPath) || !string.IsNullOrEmpty(m.InstructionText)) ? "Да" : "—")).ToList();
    }
    private void OnAddMed(object s, RoutedEventArgs e) { if (MedicationForm.Show(_ctx, this, null)) { RefreshMeds(); RefreshAll(); } }
    private void OnEditMed(object s, RoutedEventArgs e)
    {
        if (MedsGrid.SelectedItem is MedRow r)
        {
            var m = _ctx.Meds.GetAll().FirstOrDefault(x => x.Id == r.Id);
            if (m != null && MedicationForm.Show(_ctx, this, m)) { RefreshMeds(); RefreshAll(); }
        }
    }
    private void OnBulkDoses(object s, RoutedEventArgs e) { if (BulkDoseWizard.Run(_ctx, this)) { RefreshMeds(); RefreshAll(); } }
    private void OnToggleMed(object s, RoutedEventArgs e)
    {
        if (MedsGrid.SelectedItem is MedRow r)
        {
            var m = _ctx.Meds.GetAll().First(x => x.Id == r.Id);
            m.IsActive = !m.IsActive;
            _ctx.Meds.Save(m);
            RefreshMeds(); RefreshAll();
        }
    }
    private void OnDeleteMed(object s, RoutedEventArgs e)
    {
        if (MedsGrid.SelectedItem is MedRow r &&
            MessageBox.Show("Удалить лекарство? История приёмов сохранится (§8.2).", "MedControl", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        { _ctx.Meds.Delete(r.Id); RefreshMeds(); RefreshAll(); }
    }

    // ---------- Приёмы ----------
    // Несколько лекарств в одно время — одно событие (группа).
    private sealed record IntakeRow(List<Guid> Ids, string Scheduled, string Actual, string Summary, string Status, string Method, string Wb);
    private static string IntakeStatusText(IntakeStatus s) => s switch
    {
        IntakeStatus.Taken => "Принял",
        IntakeStatus.TakenLate => "Принял позже",
        IntakeStatus.Missed => "Пропущено",
        _ => "Внеплановый приём",
    };
    private void OnRefreshIntakes(object? s, RoutedEventArgs? e)
    {
        if (_ctx == null || IntakeStatusBox == null || IntakesGrid == null) return;
        var f = IntakeStatusBox.SelectedItem is ComboBoxItem ci ? ci.Content?.ToString() : "Все";
        var q = (IntakeSearch?.Text ?? "").Trim().ToLowerInvariant();
        var rows = _ctx.Intakes.GetRange(DateTime.Now.AddYears(-5), DateTime.Now.AddDays(2)).OrderByDescending(i => i.ScheduledAt).ToList();
        rows = f switch
        {
            "Принял" => rows.Where(i => i.Status == IntakeStatus.Taken).ToList(),
            "Принял позже" => rows.Where(i => i.Status == IntakeStatus.TakenLate).ToList(),
            "Пропущено" => rows.Where(i => i.Status == IntakeStatus.Missed).ToList(),
            "Внеплановый приём" => rows.Where(i => i.Status == IntakeStatus.Unplanned).ToList(),
            _ => rows,
        };
        if (!string.IsNullOrEmpty(q)) rows = rows.Where(i => i.MedicationName.ToLowerInvariant().Contains(q)).ToList();
        var scores = _ctx.Wb.GetScoresByIntake(rows.Select(r => r.Id));
        IntakesGrid.ItemsSource = rows.GroupBy(i => i.ScheduledAt).Select(g =>
        {
            var list = g.ToList();
            var actuals = list.Where(i => i.ActualAt != null).Select(i => i.ActualAt!.Value.ToString("yyyy-MM-dd HH:mm")).Distinct().ToList();
            return new IntakeRow(
                list.Select(i => i.Id).ToList(),
                g.Key.ToString("yyyy-MM-dd HH:mm"),
                actuals.Count == 0 ? "-" : string.Join(" / ", actuals),
                string.Join("; ", list.Select(i => $"{i.MedicationName} ({i.Dosage})")),
                string.Join(" / ", list.Select(i => IntakeStatusText(i.Status)).Distinct()),
                string.Join(" / ", list.Select(i => i.ConfirmMethod.ToString()).Distinct()),
                string.Join(",", list.Where(i => scores.ContainsKey(i.Id)).SelectMany(i => scores[i.Id]).Distinct()));
        }).ToList();
    }
    private List<MedicationIntake> GroupIntakes(IntakeRow r)
    {
        var all = _ctx.Intakes.GetRange(DateTime.Now.AddYears(-5), DateTime.Now.AddDays(2));
        return all.Where(x => r.Ids.Contains(x.Id)).OrderBy(x => x.MedicationName).ToList();
    }
    private void OnTakenLate(object s, RoutedEventArgs e)
    {
        if (IntakesGrid.SelectedItem is IntakeRow r)
        {
            var now = DateTime.Now;
            foreach (var it in GroupIntakes(r)) _ctx.Reminders.MarkTakenLate(it.Id, now);
            TryBackup();
            OnRefreshIntakes(null, null); RefreshAll();
        }
    }
    private void OnIntakeComment(object s, RoutedEventArgs e)
    {
        if (IntakesGrid.SelectedItem is IntakeRow r)
        {
            var group = GroupIntakes(r);
            if (group.Count == 0) return;
            var text = Steps.AskText(this, "Комментарий приёма", "Комментарий (можно пустой — удалит):",
                group[0].Comment, multiline: true, allowEmpty: true);
            if (text == null) return;
            foreach (var it in group) { it.Comment = text; _ctx.Intakes.Upsert(it); }
            OnRefreshIntakes(null, null);
        }
    }
    private void OnIntakeDetails(object? s, EventArgs e)
    {
        if (IntakesGrid.SelectedItem is IntakeRow r)
        {
            var group = GroupIntakes(r);
            if (group.Count == 0) return;
            IntakeDetails.ShowGroup(_ctx, this, group);
            OnRefreshIntakes(null, null);
        }
    }
    private void OnDeleteIntake(object s, RoutedEventArgs e)
    {
        if (IntakesGrid.SelectedItem is IntakeRow r &&
            MessageBox.Show("Переместить приём в корзину?", "MedControl", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        {
            foreach (var it in GroupIntakes(r)) { _ctx.Intakes.MoveToTrash(it.Id); _ctx.Wb.DeleteLinksOf(it.Id); }
            OnRefreshIntakes(null, null);
        }
    }
    private void OnUnplanned(object s, RoutedEventArgs e)
    {
        if (UnplannedWizard.Run(_ctx, this)) { RefreshAll(); }
    }

    // ---------- Самочувствие ----------
    private sealed record WbRow(Guid Id, string Date, int Score, string Comment);
    private void OnNewWellBeing(object s, RoutedEventArgs e)
    {
        var d = new WellBeingDialog(_ctx, "Оцените самочувствие (1 — очень плохо, 10 — отлично)") { Owner = this, Topmost = true };
        if (d.ShowDialog() == true) RefreshAll();
    }
    private void OnRefreshWb(object? s, RoutedEventArgs? e)
    {
        if (_ctx == null || WbGrid == null) return;
        WbGrid.ItemsSource = _ctx.Wb.GetRange(DateTime.Now.AddYears(-10), DateTime.Now.AddDays(1))
            .OrderByDescending(w => w.CreatedAt).Select(w => new WbRow(w.Id, w.CreatedAt.ToString("yyyy-MM-dd HH:mm"), w.Score, w.Comment)).ToList();
    }
    private void OnEditWellBeing(object s, RoutedEventArgs e)
    {
        if (WbGrid.SelectedItem is WbRow r)
        {
            var scoreIdx = Steps.AskChoiceIndex(this, "Оценка", "Шаг 1. Новая оценка (1 — очень плохо, 10 — отлично):",
                ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10"], r.Score - 1);
            if (scoreIdx < 0) return;
            var comment = Steps.AskText(this, "Комментарий", "Шаг 2. Комментарий:",
                r.Comment, multiline: true, allowEmpty: true);
            if (comment == null) return;
            _ctx.WellBeing.Edit(r.Id, scoreIdx + 1, comment);
            OnRefreshWb(null, null);
        }
    }
    private void OnDeleteWellBeing(object s, RoutedEventArgs e)
    {
        if (WbGrid.SelectedItem is WbRow r &&
            MessageBox.Show("Переместить оценку в корзину самочувствия?", "MedControl", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        { _ctx.Wb.MoveToTrash(r.Id); OnRefreshWb(null, null); }
    }

    // ---------- Корзина ----------
    private void OnRefreshTrash(object? s, RoutedEventArgs? e)
    {
        if (_ctx == null || TrashMeasBox == null || TrashIntakeBox == null || TrashWbBox == null) return;
        TrashMeasBox.ItemsSource = _ctx.Meas.GetTrash().Select(m => $"{m.TakenAt:yyyy-MM-dd HH:mm} {m.TonometerName} {m.Systolic}/{m.Diastolic} [{m.Id}]").ToList();
        TrashIntakeBox.ItemsSource = _ctx.Intakes.GetTrash().Select(i => $"{i.ScheduledAt:yyyy-MM-dd HH:mm} {i.MedicationName} [{i.Id}]").ToList();
        TrashWbBox.ItemsSource = _ctx.Wb.GetTrash().Select(w => $"{w.CreatedAt:yyyy-MM-dd HH:mm} {w.Score}/10 [{w.Id}]").ToList();
    }
    private static Guid ParseId(string? s)
    {
        if (s == null) return Guid.Empty;
        var a = s.LastIndexOf('['); var b = s.LastIndexOf(']');
        if (a < 0 || b < 0) return Guid.Empty;
        return Guid.TryParse(s.Substring(a + 1, b - a - 1), out var g) ? g : Guid.Empty;
    }
    private void OnRestoreMeas(object s, RoutedEventArgs e)
    {
        var id = ParseId(TrashMeasBox.SelectedItem?.ToString());
        if (id != Guid.Empty) { _ctx.Meas.Restore(id); OnRefreshTrash(null, null); OnRefreshMeas(null, null); }
    }
    private void OnRestoreIntake(object s, RoutedEventArgs e)
    {
        var id = ParseId(TrashIntakeBox.SelectedItem?.ToString());
        if (id != Guid.Empty) { _ctx.Intakes.Restore(id); OnRefreshTrash(null, null); OnRefreshIntakes(null, null); }
    }
    private void OnClearTrash(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show("Удалить навсегда все измерения и приёмы из корзины?", "MedControl", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        { _ctx.Meas.ClearTrash(); _ctx.Intakes.ClearTrash(); OnRefreshTrash(null, null); }
    }
    private void OnRestoreWb(object s, RoutedEventArgs e)
    {
        var id = ParseId(TrashWbBox.SelectedItem?.ToString());
        if (id != Guid.Empty) { _ctx.Wb.Restore(id); OnRefreshTrash(null, null); OnRefreshWb(null, null); }
    }
    private void OnDeleteWbForever(object s, RoutedEventArgs e)
    {
        var id = ParseId(TrashWbBox.SelectedItem?.ToString());
        if (id != Guid.Empty && MessageBox.Show("Удалить оценку навсегда?", "MedControl", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        { _ctx.Wb.DeleteForever(id); OnRefreshTrash(null, null); }
    }
    private void OnClearWbTrash(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show("Очистить корзину самочувствия?", "MedControl", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        { _ctx.Wb.ClearTrash(); OnRefreshTrash(null, null); }
    }

    // ---------- Excel / PDF ----------
    private void OnExportExcel(object s, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = "medcontrol.xlsx" };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                _ctx.Excel.Export(dlg.FileName, DateTime.Now.AddDays(-30), DateTime.Now.AddDays(1), true, true, true, true);
                MessageBox.Show("Экспортировано: " + dlg.FileName);
            }
            catch (Exception ex) { MessageBox.Show("Ошибка экспорта: " + ex.Message); }
        }
    }
    private void OnImportExcel(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx" };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                var (imp, skip) = _ctx.Excel.Import(dlg.FileName);
                MessageBox.Show($"Импортировано новых измерений: {imp}\nПропущено (дубликаты/битые строки): {skip}");
                RefreshAll();
            }
            catch (Exception ex) { MessageBox.Show("Ошибка импорта: " + ex.Message); }
        }
    }
    private void OnBrowseCompare(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx" };
        if (dlg.ShowDialog() == true) ExcelCompareBox.Text = dlg.FileName;
    }
    private void OnClearCompare(object s, RoutedEventArgs e) => ExcelCompareBox.Text = "";
    private void OnCompareExcel(object s, RoutedEventArgs e)
    {
        var file = _ctx.Settings.ExcelComparePath;
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            MessageBox.Show("Сначала выберите эталонный файл в настройках («Эталонный Excel») и нажмите «Сохранить настройки».");
            return;
        }
        CompareInfo.Text = "Сверка…";
        Task.Run(() =>
        {
            try
            {
                var r = _ctx.Excel.CompareWithFile(file);
                Dispatcher.Invoke(() =>
                {
                    var t = $"В файле: {r.FileTotal}, в базе: {r.DbTotal}, совпало: {r.Matched}, битых строк: {r.InvalidRows}.";
                    if (r.OnlyInFile.Count > 0) t += "\n\nЕсть в файле, нет в базе:\n" + string.Join("\n", r.OnlyInFile);
                    if (r.OnlyInDb.Count > 0) t += "\n\nЕсть в базе, нет в файле:\n" + string.Join("\n", r.OnlyInDb);
                    if (r.OnlyInFile.Count == 0 && r.OnlyInDb.Count == 0) t += "\n\nПолное совпадение.";
                    CompareInfo.Text = t;
                });
            }
            catch (Exception ex) { Dispatcher.Invoke(() => CompareInfo.Text = "Ошибка сверки: " + ex.Message); }
        });
    }
    private void OnExcelTemplate(object s, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = "medcontrol_template.xlsx" };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                _ctx.Excel.CreateTemplate(dlg.FileName);
                MessageBox.Show("Шаблон сохранён: " + dlg.FileName + "\nУдалите строки-примеры и впишите свои замеры.");
            }
            catch (Exception ex) { MessageBox.Show("Ошибка шаблона: " + ex.Message); }
        }
    }
    private void OnPrintPreview(object s, RoutedEventArgs e)
    {
        var kind = (ReportKindBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "measurements";
        try
        {
            var path = _ctx.Reports.BuildPdf(DateTime.Now.AddDays(-30), DateTime.Now, LandscapeBox.IsChecked == true, 11, kind);
            ReportInfo.Text = "PDF сформирован: " + path;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show("Ошибка отчёта: " + ex.Message); }
    }
    private void OnSavePdf(object s, RoutedEventArgs e) => OnPrintPreview(s, e);
    private void OnOpenReports(object s, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Data.Paths.ReportDir) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }
    private void OnQuickPrint(object s, RoutedEventArgs e)
    {
        int idx = Steps.AskChoiceIndex(this, "Печать", "Что печатать?",
            ["Измерения давления", "Приём лекарств", "Совместный отчёт"]);
        if (idx < 0) return;
        string kind = idx switch { 1 => "intakes", 2 => "joint", _ => "measurements" };
        try
        {
            var path = _ctx.Reports.BuildPdf(DateTime.Now.AddDays(-30), DateTime.Now, false, 11, kind);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show("Ошибка отчёта: " + ex.Message); }
    }

    // ---------- Инструкции ----------
    private sealed record InstrRow(Guid Id, string Name, string HasText, string HasPhoto);
    private void OnRefreshInstructions(object? s, RoutedEventArgs? e)
    {
        if (_ctx == null || InstrGrid == null) return;
        InstrGrid.ItemsSource = _ctx.Meds.GetAll()
            .Where(m => !string.IsNullOrWhiteSpace(m.InstructionText) || !string.IsNullOrEmpty(m.PhotoInstructionPath))
            .OrderBy(m => m.Name)
            .Select(m => new InstrRow(m.Id,
                m.Name,
                string.IsNullOrWhiteSpace(m.InstructionText) ? "—" : "Да",
                string.IsNullOrEmpty(m.PhotoInstructionPath) ? "—" : "Да")).ToList();
    }
    private void OnShowInstruction(object? s, EventArgs e)
    {
        if (InstrGrid.SelectedItem is InstrRow r)
        {
            var m = _ctx.Meds.GetAll().FirstOrDefault(x => x.Id == r.Id);
            if (m == null) return;
            if (!string.IsNullOrEmpty(m.PhotoInstructionPath) && _ctx.Photos.Exists(m.PhotoInstructionPath))
                _ctx.Photos.Show(this, m.PhotoInstructionPath, "Инструкция — " + m.Name);
            else if (!string.IsNullOrWhiteSpace(m.InstructionText))
                Steps.Msg(this, $"{m.Name}:\n\n{m.InstructionText}");
            else Steps.Msg(this, "Инструкции нет.");
        }
    }

    // ---------- Настройки ----------
    private void RefreshTonos() => TonoBox.ItemsSource = _ctx.Meas.GetTonometers().Select(t => $"{t.Name} [{t.Id}]").ToList();
    private void OnAddTono(object s, RoutedEventArgs e)
    {
        var name = (NewTonoName.Text ?? "").Trim();
        var (ok, err) = ValidationService.ValidateTonometer(name);
        if (!ok) { MessageBox.Show(err); return; }
        if (_ctx.Meas.GetTonometers().Count >= 10) { MessageBox.Show("Максимум 10 тонометров (§15)."); return; }
        _ctx.Meas.SaveTonometer(new Tonometer { Name = name });
        NewTonoName.Text = "";
        RefreshTonos();
    }
    private void OnTestReminder(object s, RoutedEventArgs e)
    {
        // §42: тестовое напоминание настоящим звуком и громкостью, без мед. истории
        try
        {
            _ctx.Sound.Play();
            _ctx.Volume.Duck((int)VolumeSlider.Value);
            var w = new ReminderWindow(_ctx, new List<Scheduling.PlannedDose>
            {
                new(Guid.NewGuid(), Guid.NewGuid(), "Тестовое лекарство", "Проверка работы напоминания", "1 таблетка", DateTime.Now)
            }, isTest: true);
            w.ShowDialog();
            _ctx.Volume.Restore();
            SettingsStatus.Text = "Проверка напоминания выполнена, система возвращена в исходное состояние.";
        }
        catch (Exception ex) { MessageBox.Show("Ошибка теста: " + ex.Message); }
    }
    private void OnCheckDb(object s, RoutedEventArgs e)
    {
        var r = Data.Db.QuickCheck(Data.Paths.DbFile);
        SettingsStatus.Text = "Проверка БД: " + r;
        MessageBox.Show("Проверка базы данных: " + r, "MedControl");
    }
    private void OnCheckUpdates(object s, RoutedEventArgs e)
    {
        UpdateStatus.Text = "Проверка обновлений…";
        Task.Run(async () =>
        {
            try
            {
                var info = await _ctx.Updates.CheckAsync();
                Dispatcher.Invoke(() =>
                {
                    if (!info.Available)
                    {
                        UpdateStatus.Text = $"У вас последняя версия ({info.Current}).";
                        return;
                    }
                    UpdateStatus.Text = $"Доступна версия {info.Version} (у вас {info.Current}).";
                    string sizeTxt = info.Size > 0 ? $" (~{info.Size / 1048576} МБ)" : "";
                    var dr = MessageBox.Show(
                        $"Доступна версия {info.Version} (установлена {info.Current}){sizeTxt}.\n\n{info.Notes}\n\nСкачать, установить и перезапустить автоматически?",
                        "MedControl — обновление", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (dr != MessageBoxResult.Yes) return;
                    if (!info.Url.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(info.Url) { UseShellExecute = true }); }
                        catch (Exception ex) { MessageBox.Show("Не удалось открыть: " + ex.Message); }
                        return;
                    }
                    UpdateStatus.Text = "Загрузка обновления…";
                    Task.Run(async () =>
                    {
                        try
                        {
                            var dest = Path.Combine(Path.GetTempPath(), $"MedControl_Setup_{info.Version}.exe");
                            await _ctx.Updates.DownloadWithResumeAsync(info.Url, dest,
                                info.Size > 0 ? info.Size : null,
                                (got, total) => Dispatcher.Invoke(() =>
                                    UpdateStatus.Text = total != null && total > 0
                                        ? $"Загрузка… {got / 1048576} из {total / 1048576} МБ"
                                        : $"Загрузка… {got / 1048576} МБ"));
                            Dispatcher.Invoke(() =>
                            {
                                UpdateStatus.Text = "Установка…";
                                try
                                {
                                    // Тихая установка без окон; после неё установщик сам запустит программу.
                                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dest, "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES") { UseShellExecute = true });
                                }
                                catch (Exception ex) { MessageBox.Show("Не удалось запустить установку: " + ex.Message); return; }
                                AllowClose = true;
                                Application.Current.Shutdown();
                            });
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                UpdateStatus.Text = "Ошибка загрузки: " + ex.Message;
                                if (MessageBox.Show("Не скачалось автоматически:\n" + ex.Message +
                                    "\n\nОткрыть страницу загрузки в браузере?",
                                    "MedControl — обновление", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                                {
                                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(info.Url) { UseShellExecute = true }); }
                                    catch (Exception ex2) { MessageBox.Show("Не удалось открыть: " + ex2.Message); }
                                }
                            });
                        }
                    });
                });
            }
            catch (Exception ex) { Dispatcher.Invoke(() => UpdateStatus.Text = "Проверка не удалась: " + ex.Message); }
        });
    }
    private void OnChangeBackupDir(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Папка резервных копий" };
        if (dlg.ShowDialog() == true)
        {
            _ctx.Backups.ChangeDirectory(dlg.FolderName);
            SettingsStatus.Text = "Папка backup: " + dlg.FolderName;
            RefreshAll();
        }
    }
    private void RefreshManualBackups()
    {
        if (ManualBackupBox == null) return;
        ManualBackupBox.ItemsSource = _ctx.Backups.ListManual().Select(Path.GetFileName).ToList();
    }
    private void OnSaveSettings(object s, RoutedEventArgs e)
    {
        // Сохраняем всё выставленное + делаем ручную копию (хранится постоянно).
        try
        {
            if (GraceBox.SelectedItem is ComboBoxItem g && int.TryParse(g.Tag?.ToString(), out int grace))
                _ctx.Settings.MissedGraceMinutes = grace;
            _ctx.Settings.VolumeDuckPercent = (int)VolumeSlider.Value;
            if (double.TryParse((UiFontBox.Text ?? "").Trim(), out double uf))
                _ctx.Settings.UiFontSize = Math.Clamp(uf, 10, 28);
            if (double.TryParse((ReminderFontBox.Text ?? "").Trim(), out double rf))
                _ctx.Settings.ReminderFontSize = Math.Clamp(rf, 10, 48);
            FontSize = _ctx.Settings.UiFontSize;
            if (MeasureInputBox.SelectedItem is ComboBoxItem mi && mi.Tag?.ToString() is string tag)
                _ctx.Settings.MeasurementInput = tag;
            _ctx.Settings.ExcelComparePath = (ExcelCompareBox.Text ?? "").Trim();
            _ctx.Settings.DeviceName = (DeviceNameBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(_ctx.Settings.DeviceName)) _ctx.Settings.DeviceName = Environment.MachineName;
            if (int.TryParse((SyncPortBox.Text ?? "").Trim(), out int port) && port > 0 && port < 65536)
                _ctx.Settings.SyncPort = port;
            _ctx.Settings.SyncKey = SyncKeyBox.Password ?? "";
            _ctx.Settings.PeerAddress = (PeerIpBox.Text ?? "").Trim();
            bool wantServer = SyncServerBox.IsChecked == true;
            if (wantServer != _ctx.Settings.SyncServerEnabled)
            {
                _ctx.Settings.SyncServerEnabled = wantServer;
                try
                {
                    if (wantServer) _ctx.Sync.StartServer();
                    else _ctx.Sync.StopServer();
                }
                catch (Exception ex) { MessageBox.Show("Сервер не запустился: " + ex.Message); }
            }
            AutostartService.Set(AutostartBox.IsChecked == true, _ctx.Log);
            var (ok, msg) = _ctx.Backups.CreateManual();
            SettingsStatus.Text = ok ? "Настройки сохранены. " + msg : "Настройки сохранены. Копия: " + msg;
            RefreshAll();
        }
        catch (Exception ex) { MessageBox.Show("Ошибка сохранения: " + ex.Message); }
    }
    private string? SelectedManual()
    {
        var name = ManualBackupBox.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(name)) return null;
        var full = Path.Combine(_ctx.Backups.Dir, name);
        return File.Exists(full) ? full : null;
    }
    private void OnRestoreManual(object s, RoutedEventArgs e)
    {
        var f = SelectedManual();
        if (f == null) { MessageBox.Show("Выберите копию."); return; }
        if (MessageBox.Show($"Восстановить базу из копии?\n{Path.GetFileName(f)}\nТекущие данные будут заменены.", "MedControl", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var (ok, msg) = _ctx.Backups.RestoreManual(f);
        MessageBox.Show(msg, "MedControl", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Error);
        if (ok) { AllowClose = true; System.Diagnostics.Process.Start(Environment.ProcessPath!); Application.Current.Shutdown(); }
    }
    private void OnNetworkTransfer(object s, RoutedEventArgs e)
    {
        var ip = Steps.AskText(this, "Перенос по сети", "IP-адрес ведущего компьютера:",
            _ctx.Settings.PeerAddress, allowEmpty: false);
        if (ip == null) return;
        SettingsStatus.Text = "Перенос по сети…";
        Task.Run(() =>
        {
            var (ok, msg) = MedControl.Sync.NetworkTransfer.Run(_ctx.Sync, _ctx.Transfer, _ctx.Log, ip);
            Dispatcher.Invoke(() =>
            {
                SettingsStatus.Text = msg;
                MessageBox.Show(msg, "MedControl", MessageBoxButton.OK,
                    ok ? MessageBoxImage.Information : MessageBoxImage.Error);
                if (ok) { LoadSettings(); RefreshAll(); }
            });
        });
    }
    private void OnShowWizard(object s, RoutedEventArgs e)
    {
        new WizardWindow(_ctx).ShowDialog();
        LoadSettings();
        RefreshAll();
    }
    private void OnDeleteManual(object s, RoutedEventArgs e)
    {
        var f = SelectedManual();
        if (f == null) { MessageBox.Show("Выберите копию."); return; }
        if (MessageBox.Show($"Удалить копию навсегда?\n{Path.GetFileName(f)}", "MedControl", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var (ok, msg) = _ctx.Backups.DeleteManual(f);
        SettingsStatus.Text = msg;
        RefreshManualBackups();
    }
    private void OnPrepareTransfer(object s, RoutedEventArgs e)
    {
        var ips = MedControl.Sync.SyncService.GetLocalIps();
        var ip = Steps.AskText(this, "Перенос базы", "IP-адрес ЭТОГО (ведущего) компьютера для файла переноса:",
            ips.FirstOrDefault() ?? "", allowEmpty: false);
        if (ip == null) return;
        var dlg = new SaveFileDialog { Filter = "Перенос MedControl (*.mcpack)|*.mcpack", FileName = $"medcontrol_perenos_{DateTime.Now:yyyy-MM-dd}.mcpack" };
        if (dlg.ShowDialog() != true) return;
        var (ok, msg) = _ctx.Transfer.CreatePack(ip.Trim(), dlg.FileName);
        MessageBox.Show(msg, "MedControl", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Error);
    }

    // ---------- Синхронизация ----------
    private void OnSyncTest(object s, RoutedEventArgs e)
    {
        var host = (PeerIpBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(host)) { MessageBox.Show("Укажите IP-адрес второго ПК."); return; }
        SyncStatus.Text = "Проверка…";
        var key = SyncKeyBox.Password ?? "";
        int port = _ctx.Settings.SyncPort;
        Task.Run(() =>
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                if (!client.ConnectAsync(host, port).Wait(8000)) throw new TimeoutException("Нет соединения.");
                Dispatcher.Invoke(() => SyncStatus.Text = $"Связь есть: {host}:{port}. Проверьте, что ключ одинаковый, кнопкой «Синхронизировать».");
            }
            catch (Exception ex) { Dispatcher.Invoke(() => SyncStatus.Text = "Нет связи: " + ex.Message + " Проверьте брандмауэр/антивирус на обоих ПК и изоляцию Wi-Fi в роутере."); }
        });
    }
    private void OnFirewallAllow(object s, RoutedEventArgs e)
    {
        SyncStatus.Text = "Запрос прав администратора…";
        Task.Run(() =>
        {
            var (ok, msg) = Services.FirewallHelper.EnsureRule();
            Dispatcher.Invoke(() => SyncStatus.Text = (ok ? "✓ " : "✗ ") + msg);
        });
    }
    private void OnSyncNow(object s, RoutedEventArgs e)
    {
        var host = (PeerIpBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(host)) { MessageBox.Show("Укажите IP-адрес второго ПК."); return; }
        _ctx.Settings.PeerAddress = host;
        SyncStatus.Text = "Синхронизация…";
        Task.Run(() =>
        {
            var (ok, msg) = _ctx.Sync.SyncNow(host);
            Dispatcher.Invoke(() =>
            {
                SyncStatus.Text = (ok ? "✓ " : "✗ ") + msg;
                if (ok) RefreshAll();
            });
        });
    }
    private void OnDeleteAll(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show("Удалить ВСЕ данные? (1/2)", "MedControl", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (MessageBox.Show("Подтвердите удаление ВСЕХ данных. Без защитной копии (§32). (2/2)", "MedControl", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            foreach (var f in _ctx.Backups.ListAuto()) try { File.Delete(f); } catch { }
            foreach (var f in _ctx.Backups.ListManual()) try { File.Delete(f); } catch { }
            if (Directory.Exists(Data.Paths.ReportDir)) Directory.Delete(Data.Paths.ReportDir, true);
            if (Directory.Exists(Data.Paths.PhotosDir)) Directory.Delete(Data.Paths.PhotosDir, true);
            using var c = _ctx.Db.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM Medications; DELETE FROM MedicationDoses; DELETE FROM MedicationIntakes; DELETE FROM Measurements; DELETE FROM Tonometers; DELETE FROM WellBeingRatings; DELETE FROM WellBeingLinks; DELETE FROM AppSettings; DELETE FROM TechnicalLogs;";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex) { MessageBox.Show("Ошибка удаления: " + ex.Message); return; }
        AllowClose = true;
        Application.Current.Shutdown(); // без автоперезапуска (§32)
    }

    protected override void OnClosed(EventArgs e) { base.OnClosed(e); }

    private void TryBackup()
    {
        try
        {
            var today = DateTime.Now.Date;
            var planned = _ctx.Intakes.GetRange(today, today.AddDays(1))
                .Select(i => (i.ScheduledAt, i.Status)).ToList();
            if (_ctx.Backups.TryDailyBackup(planned))
                RefreshAll();
        }
        catch { }
    }
}

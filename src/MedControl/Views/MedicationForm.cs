using System.IO;
using System.Windows;
using MedControl.Models;
using MedControl.ViewModels;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using RadioButton = System.Windows.Controls.RadioButton;
using ListBox = System.Windows.Controls.ListBox;
using StackPanel = System.Windows.Controls.StackPanel;
using ScrollViewer = System.Windows.Controls.ScrollViewer;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TextWrapping = System.Windows.TextWrapping;
using FontWeights = System.Windows.FontWeights;
using ScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace MedControl.Views;

// Единое окно лекарства: все поля видны сразу + фото упаковки/таблетки/инструкции.
public sealed class MedicationForm : Window
{
    private readonly AppServices _ctx;
    private readonly Medication _med;
    private readonly bool _isNew;
    private readonly TextBox _name = new() { FontSize = 16 };
    private readonly TextBox _desc = new() { FontSize = 16, AcceptsReturn = true, Height = 70, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ListBox _doseList = new() { FontSize = 16, Height = 110 };
    private readonly ComboBox _hh = new() { FontSize = 18, Width = 90 };
    private readonly ComboBox _mm = new() { FontSize = 18, Width = 90 };
    private readonly TextBox _dosage = new() { FontSize = 16 };
    private readonly List<MedicationDose> _doses = new();
    private readonly RadioButton _daily = new() { Content = "Каждый день", FontSize = 16 };
    private readonly RadioButton _weekly = new() { Content = "По дням недели", FontSize = 16 };
    private readonly List<CheckBox> _days = new();
    private readonly System.Windows.Controls.DatePicker _start = new() { FontSize = 15 };
    private readonly System.Windows.Controls.DatePicker _end = new() { FontSize = 15 };
    private readonly CheckBox _noStart = new() { Content = "Постоянно (без даты начала)", FontSize = 15 };
    private readonly CheckBox _noEnd = new() { Content = "Постоянно (без даты окончания)", FontSize = 15 };
    private readonly CheckBox _active = new() { Content = "Активно", FontSize = 16 };
    private readonly TextBox _instrText = new() { FontSize = 15, AcceptsReturn = true, Height = 90, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private string _packRel, _pillRel, _instrRel;
    private readonly string _oldPack, _oldPill, _oldInstr;
    private string? _packNew, _pillNew, _instrNew;
    private TextBlock _packLbl = new(), _pillLbl = new(), _instrLbl = new();

    private MedicationForm(AppServices ctx, Medication? edit)
    {
        _ctx = ctx;
        _isNew = edit == null;
        _med = edit ?? new Medication();
        Title = _isNew ? "Новое лекарство" : "Изменить лекарство";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 800;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = true;
        FontSize = 15;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _name.Text = _med.Name;
        _name.MaxLength = 100;
        _desc.Text = _med.Description ?? "";
        _doses.AddRange(_med.Doses);
        for (int h = 0; h < 24; h++) _hh.Items.Add(h.ToString("D2"));
        for (int m = 0; m < 60; m++) _mm.Items.Add(m.ToString("D2"));
        _hh.SelectedIndex = 8; _mm.SelectedIndex = 0;
        _daily.IsChecked = _med.Mode == ScheduleMode.Daily;
        _weekly.IsChecked = _med.Mode == ScheduleMode.Weekly;
        string[] wd = ["Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс"];
        for (int i = 0; i < 7; i++)
            _days.Add(new CheckBox { Content = wd[i], FontSize = 15, IsChecked = _med.DaysOfWeek.Contains(i), Margin = new Thickness(0, 0, 10, 0) });
        _start.SelectedDate = _med.StartDate?.ToDateTime(TimeOnly.MinValue);
        _end.SelectedDate = _med.EndDate?.ToDateTime(TimeOnly.MinValue);
        _noStart.IsChecked = _med.StartDate == null;
        _noEnd.IsChecked = _med.EndDate == null;
        _active.IsChecked = _isNew || _med.IsActive;
        _instrText.Text = _med.InstructionText ?? "";
        _packRel = _med.PhotoPackPath ?? "";
        _pillRel = _med.PhotoPillPath ?? "";
        _instrRel = _med.PhotoInstructionPath ?? "";
        _oldPack = _packRel; _oldPill = _pillRel; _oldInstr = _instrRel;

        var p = new StackPanel { Margin = new Thickness(14) };
        p.Children.Add(B("Название (до 100):"));
        p.Children.Add(_name);
        p.Children.Add(B("Назначение/описание:"));
        p.Children.Add(_desc);
        p.Children.Add(B("Времена приёма:"));
        p.Children.Add(_doseList);
        // Строка 1: время + кнопки (всё влезает по ширине)
        var timeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        timeRow.Children.Add(_hh);
        timeRow.Children.Add(new TextBlock { Text = " : ", FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
        timeRow.Children.Add(_mm);
        var addBtn = new Button { Content = "Добавить", Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(16, 2, 16, 2) };
        addBtn.Click += (_, _) => AddDose();
        timeRow.Children.Add(addBtn);
        var delBtn = new Button { Content = "Убрать выбранное", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(16, 2, 16, 2) };
        delBtn.Click += (_, _) => { if (_doseList.SelectedIndex >= 0) { _doses.RemoveAt(_doseList.SelectedIndex); PaintDoses(); } };
        timeRow.Children.Add(delBtn);
        p.Children.Add(timeRow);
        // Строка 2: дозировка на всю ширину
        var doseGrid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 6, 0, 0) };
        doseGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
        doseGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        var doseLbl = new TextBlock { Text = "Доза: ", VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Controls.Grid.SetColumn(doseLbl, 0);
        _dosage.ToolTip = "Дозировка, например: 1 таблетка";
        _dosage.Text = "";
        System.Windows.Controls.Grid.SetColumn(_dosage, 1);
        doseGrid.Children.Add(doseLbl);
        doseGrid.Children.Add(_dosage);
        p.Children.Add(doseGrid);

        p.Children.Add(B("Режим:", new Thickness(0, 10, 0, 0)));
        var modeRow = new StackPanel { Orientation = Orientation.Horizontal };
        modeRow.Children.Add(_daily);
        _weekly.Margin = new Thickness(16, 0, 0, 0);
        modeRow.Children.Add(_weekly);
        p.Children.Add(modeRow);
        var dayRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var d in _days) dayRow.Children.Add(d);
        p.Children.Add(dayRow);

        p.Children.Add(B("Дата начала:", new Thickness(0, 10, 0, 0)));
        p.Children.Add(_start);
        p.Children.Add(_noStart);
        p.Children.Add(B("Дата окончания:", new Thickness(0, 6, 0, 0)));
        p.Children.Add(_end);
        p.Children.Add(_noEnd);
        p.Children.Add(_active);

        p.Children.Add(B("Фото упаковки:", new Thickness(0, 10, 0, 0)));
        p.Children.Add(PhotoRow(_packLbl, () => _packRel, () => _packNew, v => _packNew = v, v => _packRel = v));
        p.Children.Add(B("Фото таблетки:"));
        p.Children.Add(PhotoRow(_pillLbl, () => _pillRel, () => _pillNew, v => _pillNew = v, v => _pillRel = v));
        p.Children.Add(B("Фото инструкции:"));
        p.Children.Add(PhotoRow(_instrLbl, () => _instrRel, () => _instrNew, v => _instrNew = v, v => _instrRel = v));
        PaintPhotoLabels();
        p.Children.Add(B("Инструкция текстом:", new Thickness(0, 8, 0, 0)));
        p.Children.Add(_instrText);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "Сохранить", IsDefault = true, Padding = new Thickness(30, 6, 30, 6), Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Save();
        var cancel = new Button { Content = "Отмена", Padding = new Thickness(20, 6, 20, 6) };
        cancel.Click += (_, _) => DialogResult = false;
        btns.Children.Add(ok);
        btns.Children.Add(cancel);
        p.Children.Add(btns);

        PaintDoses();
        Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static TextBlock B(string t, Thickness? m = null) =>
        new() { Text = t, FontWeight = FontWeights.Bold, Margin = m ?? new Thickness(0, 8, 0, 0) };

    private void PaintDoses()
    {
        _doseList.Items.Clear();
        foreach (var d in _doses.OrderBy(x => x.Time))
            _doseList.Items.Add($"{d.Time:HH:mm} — {d.Dosage}");
    }

    private void AddDose()
    {
        var dg = (_dosage.Text ?? "").Trim();
        if (dg.Length > 100) { Steps.Msg(this, "Дозировка — до 100 символов."); return; }
        var t = new TimeOnly(_hh.SelectedIndex, _mm.SelectedIndex);
        if (_doses.Any(d => d.Time == t)) { Steps.Msg(this, "Такое время уже есть."); return; }
        _doses.Add(new MedicationDose { MedicationId = _med.Id, Time = t, Dosage = dg });
        _dosage.Text = "";
        PaintDoses();
    }

    private StackPanel PhotoRow(TextBlock lbl, Func<string> getRel, Func<string?> getNew, Action<string?> setNew, Action<string> setRel)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(lbl);
        var pick = new Button { Content = "Выбрать…", Margin = new Thickness(8, 0, 0, 0) };
        pick.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.webp" };
            if (dlg.ShowDialog() == true) { setNew(dlg.FileName); PaintPhotoLabels(); }
        };
        var view = new Button { Content = "Просмотр", Margin = new Thickness(8, 0, 0, 0) };
        view.Click += (_, _) =>
        {
            var fresh = getNew();
            if (!string.IsNullOrEmpty(fresh)) { PreviewExternal(fresh); return; }
            var rel = getRel();
            if (!string.IsNullOrEmpty(rel)) _ctx.Photos.Show(this, rel, "Фото");
        };
        var clr = new Button { Content = "Убрать", Margin = new Thickness(8, 0, 0, 0) };
        clr.Click += (_, _) => { setRel(""); setNew(null); PaintPhotoLabels(); };
        row.Children.Add(pick);
        row.Children.Add(view);
        row.Children.Add(clr);
        var wrap = new StackPanel();
        wrap.Children.Add(row);
        return wrap;
    }

    private void PreviewExternal(string file)
    {
        try
        {
            var w = new Window
            {
                Owner = this, Title = "Фото", Topmost = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 640, Height = 640, ShowInTaskbar = false,
            };
            var img = new System.Windows.Controls.Image { Stretch = System.Windows.Media.Stretch.Uniform };
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(file);
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.EndInit();
            img.Source = bmp;
            w.Content = new ScrollViewer { Content = img };
            w.ShowDialog();
        }
        catch (Exception ex) { Steps.Msg(this, ex.Message); }
    }

    private void PaintPhotoLabels()
    {
        _packLbl.Text = LabelFor(_packRel, _packNew);
        _pillLbl.Text = LabelFor(_pillRel, _pillNew);
        _instrLbl.Text = LabelFor(_instrRel, _instrNew);
    }

    private string LabelFor(string rel, string? fresh)
    {
        if (!string.IsNullOrEmpty(fresh)) return "выбрано: " + Path.GetFileName(fresh);
        if (!string.IsNullOrEmpty(rel) && _ctx.Photos.Exists(rel)) return "есть фото";
        if (!string.IsNullOrEmpty(rel)) return "файл не найден";
        return "нет";
    }

    private string CommitPhoto(string rel, string? fresh)
    {
        if (!string.IsNullOrEmpty(fresh))
        {
            var imported = _ctx.Photos.Import(fresh);
            if (!string.IsNullOrEmpty(rel)) _ctx.Photos.Delete(rel);
            return imported;
        }
        return rel ?? "";
    }

    private void Save()
    {
        var name = (_name.Text ?? "").Trim();
        if (string.IsNullOrEmpty(name)) { Steps.Msg(this, "Укажите название лекарства."); return; }
        if (name.Length > 100) { Steps.Msg(this, "Название — до 100 символов."); return; }
        if (_doses.Count == 0) { Steps.Msg(this, "Добавьте хотя бы одно время приёма."); return; }
        var mode = _weekly.IsChecked == true ? ScheduleMode.Weekly : ScheduleMode.Daily;
        var days = _days.Select((b, i) => (b, i)).Where(x => x.b.IsChecked == true).Select(x => x.i).ToList();
        if (mode == ScheduleMode.Weekly && days.Count == 0) { Steps.Msg(this, "Выберите хотя бы один день недели."); return; }

        _med.Name = name;
        _med.Description = _desc.Text ?? "";
        _med.Doses = _doses.ToList();
        _med.Mode = mode;
        _med.DaysOfWeek = days;
        _med.StartDate = _noStart.IsChecked == true ? null : (_start.SelectedDate == null ? null : DateOnly.FromDateTime(_start.SelectedDate.Value));
        _med.EndDate = _noEnd.IsChecked == true ? null : (_end.SelectedDate == null ? null : DateOnly.FromDateTime(_end.SelectedDate.Value));
        _med.IsActive = _active.IsChecked == true;
        _med.PhotoPackPath = CommitPhoto(_packRel, _packNew);
        _med.PhotoPillPath = CommitPhoto(_pillRel, _pillNew);
        _med.PhotoInstructionPath = CommitPhoto(_instrRel, _instrNew);
        if (_oldPack != _med.PhotoPackPath) _ctx.Photos.Delete(_oldPack);
        if (_oldPill != _med.PhotoPillPath) _ctx.Photos.Delete(_oldPill);
        if (_oldInstr != _med.PhotoInstructionPath) _ctx.Photos.Delete(_oldInstr);
        _med.InstructionText = _instrText.Text ?? "";
        _ctx.Meds.Save(_med);
        _ctx.Scheduler.Reschedule();
        DialogResult = true;
    }

    public static bool Show(AppServices ctx, Window owner, Medication? edit)
    {
        var w = new MedicationForm(ctx, edit) { Owner = owner };
        return w.ShowDialog() == true;
    }
}

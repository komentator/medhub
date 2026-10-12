using System.Windows;
using MedControl.Models;
using MedControl.ViewModels;
using MedControl.Services;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBlock = System.Windows.Controls.TextBlock;
using StackPanel = System.Windows.Controls.StackPanel;
using ScrollViewer = System.Windows.Controls.ScrollViewer;
using UniformGrid = System.Windows.Controls.Primitives.UniformGrid;
using Orientation = System.Windows.Controls.Orientation;
using ScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TextWrapping = System.Windows.TextWrapping;
using FontWeights = System.Windows.FontWeights;
using MessageBox = System.Windows.MessageBox;

namespace MedControl.Views;

// Единое окно измерения: все поля видны сразу.
// Сверху тонометр, далее верхнее/нижнее/пульс, шкала самочувствия.
// По «OK» — окно комментария (OK/Отмена), затем сохранение.
public sealed class MeasurementForm : Window
{
    private readonly AppServices _ctx;
    private readonly Measurement? _edit;
    private readonly bool _askDateTime;
    private readonly ComboBox _tono = new() { FontSize = 18 };
    private readonly ComboBox _sys = new() { FontSize = 20 };
    private readonly ComboBox _dia = new() { FontSize = 20 };
    private readonly ComboBox _pulse = new() { FontSize = 20 };
    private System.Windows.Controls.DatePicker? _date;
    private ComboBox? _hh, _mm;
    private int _wbScore;
    private readonly List<Button> _wbButtons = new();

    private MeasurementForm(AppServices ctx, Measurement? edit, bool historic)
    {
        _ctx = ctx;
        _edit = edit;
        _askDateTime = edit == null && historic;
        Title = edit == null ? (historic ? "Историческое измерение" : "Новое измерение") : "Редактировать измерение";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = true;
        FontSize = 16;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var tonos = ctx.Meas.GetTonometers();
        foreach (var t in tonos) _tono.Items.Add(t.Name);
        if (edit != null)
        {
            int i = tonos.FindIndex(t => t.Id == edit.TonometerId);
            _tono.SelectedIndex = i >= 0 ? i : 0;
        }
        else if (tonos.Count > 0) _tono.SelectedIndex = 0;

        Fill(_sys, 0, 50, 300, edit?.Systolic ?? 120);
        Fill(_dia, 0, 30, 200, edit?.Diastolic ?? 80);
        Fill(_pulse, 0, 0, 250, edit?.Pulse ?? 70);

        var p = new StackPanel { Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "Тонометр:", FontWeight = FontWeights.Bold });
        p.Children.Add(_tono);

        if (_askDateTime)
        {
            p.Children.Add(new TextBlock { Text = "Дата:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 0) });
            _date = new System.Windows.Controls.DatePicker { SelectedDate = DateTime.Now, FontSize = 16 };
            p.Children.Add(_date);
            p.Children.Add(new TextBlock { Text = "Время (часы : минуты):", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 0) });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            _hh = new ComboBox { FontSize = 20, Width = 100 };
            _mm = new ComboBox { FontSize = 20, Width = 100 };
            for (int h = 0; h < 24; h++) _hh.Items.Add(h.ToString("D2"));
            for (int m = 0; m < 60; m++) _mm.Items.Add(m.ToString("D2"));
            _hh.SelectedIndex = DateTime.Now.Hour;
            _mm.SelectedIndex = DateTime.Now.Minute;
            row.Children.Add(_hh);
            row.Children.Add(new TextBlock { Text = " : ", FontSize = 20, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(_mm);
            p.Children.Add(row);
        }
        else if (edit != null)
        {
            p.Children.Add(new TextBlock { Text = $"Дата/время: {edit.TakenAt:yyyy-MM-dd HH:mm} (не меняется)", Margin = new Thickness(0, 8, 0, 0) });
        }
        else
        {
            p.Children.Add(new TextBlock { Text = "Дата/время — автоматически в момент сохранения.", Margin = new Thickness(0, 8, 0, 0) });
        }

        p.Children.Add(new TextBlock { Text = "Верхнее давление:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 0) });
        p.Children.Add(_sys);
        p.Children.Add(new TextBlock { Text = "Нижнее давление:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 0) });
        p.Children.Add(_dia);
        p.Children.Add(new TextBlock { Text = "Пульс:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 0) });
        p.Children.Add(_pulse);

        if (edit == null)
        {
            p.Children.Add(new TextBlock { Text = "Самочувствие (необязательно):", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 0) });
            var grid = new UniformGrid { Columns = 5 };
            for (int i = 1; i <= 10; i++)
            {
                int v = i;
                var b = new Button { Content = v.ToString(), FontSize = 18, Padding = new Thickness(8) };
                b.Click += (_, _) => { _wbScore = v; PaintWb(); };
                _wbButtons.Add(b);
                grid.Children.Add(b);
            }
            p.Children.Add(grid);
        }

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", IsDefault = true, Padding = new Thickness(30, 6, 30, 6), Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Save();
        var cancel = new Button { Content = "Отмена", Padding = new Thickness(20, 6, 20, 6) };
        cancel.Click += (_, _) => DialogResult = false;
        btns.Children.Add(ok);
        btns.Children.Add(cancel);
        p.Children.Add(btns);

        Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void PaintWb()
    {
        for (int i = 0; i < _wbButtons.Count; i++)
            _wbButtons[i].FontWeight = (i + 1) == _wbScore ? FontWeights.Bold : FontWeights.Normal;
    }

    private static void Fill(ComboBox cb, int zero, int min, int max, int current)
    {
        cb.Items.Add(zero);
        for (int i = min; i <= max; i++) cb.Items.Add(i);
        cb.SelectedItem = cb.Items.Contains(current) ? current : cb.Items[0];
    }

    private void Save()
    {
        if (_tono.SelectedItem == null) { Steps.Msg(this, "Выберите тонометр."); return; }
        int s = (int)_sys.SelectedItem, d = (int)_dia.SelectedItem, pu = (int)_pulse.SelectedItem;
        var (ok1, e1) = ValidationService.ValidatePressure(s, d);
        if (!ok1) { Steps.Msg(this, e1 ?? "Неверное давление."); return; }
        var (ok2, e2) = ValidationService.ValidatePulse(pu);
        if (!ok2) { Steps.Msg(this, e2 ?? "Неверный пульс."); return; }
        var tonos = _ctx.Meas.GetTonometers();
        var t = tonos.FirstOrDefault(x => x.Name == _tono.SelectedItem.ToString()) ?? tonos[0];

        DateTime taken;
        if (_askDateTime)
        {
            if (_date?.SelectedDate == null || _hh == null || _mm == null) { Steps.Msg(this, "Выберите дату и время."); return; }
            taken = _date.SelectedDate.Value.Date + new TimeSpan(_hh.SelectedIndex, _mm.SelectedIndex, 0);
        }
        else if (_edit != null) taken = _edit.TakenAt;
        else taken = DateTime.Now;

        var comment = Steps.AskText(this, "Комментарий", "Введите комментарий (можно пустой):",
            _edit?.Comment ?? "", multiline: true, allowEmpty: true);
        if (comment == null) return; // отмена

        if (_edit == null)
        {
            _ctx.Meas.Upsert(new Measurement
            {
                TonometerId = t.Id, TonometerName = t.Name, TakenAt = taken,
                Systolic = s, Diastolic = d, Pulse = pu, Comment = comment,
                CreatedAt = taken,
            });
            if (_wbScore >= 1)
            {
                try { _ctx.WellBeing.Create(_wbScore, comment, DateTime.Now); }
                catch (Exception ex) { Steps.Msg(this, ex.Message); }
            }
        }
        else
        {
            bool changed = _edit.Systolic != s || _edit.Diastolic != d || _edit.Pulse != pu
                || _edit.Comment != comment || _edit.TonometerId != t.Id;
            _edit.TonometerId = t.Id; _edit.TonometerName = t.Name;
            _edit.Systolic = s; _edit.Diastolic = d; _edit.Pulse = pu; _edit.Comment = comment;
            if (changed) _edit.ModifiedAt = DateTime.Now;
            _ctx.Meas.Upsert(_edit);
        }
        DialogResult = true;
    }

    public static bool Show(AppServices ctx, Window owner, Measurement? edit, bool historic)
    {
        if (edit == null && ctx.Meas.GetTonometers().Count == 0) return false;
        var w = new MeasurementForm(ctx, edit, historic) { Owner = owner };
        UiTips.Apply(w);
        return w.ShowDialog() == true;
    }
}

// Подробный отчёт по измерению (двойной клик в списке).
public static class MeasurementDetails
{
    public static void Show(AppServices ctx, Window owner, Measurement m)
    {
        List<WellBeingRating> linked = new();
        try
        {
            var from = m.TakenAt.AddMinutes(-16);
            var to = m.TakenAt.AddMinutes(16);
            var near = ctx.Wb.GetRange(from, to);
            var ids = new HashSet<Guid>();
            foreach (var r in near)
                foreach (var l in ctx.Wb.GetLinks(r.Id))
                    if (l.LinkedKind == "Measurement" && l.LinkedId == m.Id) ids.Add(r.Id);
            linked = ctx.Wb.GetByIds(ids);
        }
        catch { }
        string wb = linked.Count == 0 ? "—"
            : string.Join("\n", linked.Select(x => $"{x.CreatedAt:dd.MM HH:mm} — {x.Score}/10" + (string.IsNullOrWhiteSpace(x.Comment) ? "" : $" ({x.Comment})")));
        var text =
            $"Тонометр: {m.TonometerName}\n" +
            $"Дата: {m.TakenAt:yyyy-MM-dd}\n" +
            $"Время: {m.TakenAt:HH:mm}\n" +
            $"Верхнее: {m.Systolic}\n" +
            $"Нижнее: {m.Diastolic}\n" +
            $"Пульс: {m.Pulse}\n" +
            $"Самочувствие:\n{wb}\n" +
            $"Комментарий: {(string.IsNullOrWhiteSpace(m.Comment) ? "—" : m.Comment)}\n" +
            (m.ModifiedAt == null ? "" : $"Изменено: {m.ModifiedAt:yyyy-MM-dd HH:mm}\n");
        var w = new Window
        {
            Owner = owner, Title = "Измерение — подробно", Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 440, MaxWidth = 600, FontSize = 16,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
        };
        var p = new StackPanel { Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        var close = new Button { Content = "Закрыть", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        close.Click += (_, _) => w.DialogResult = false;
        p.Children.Add(close);
        w.Content = p;
        UiTips.Apply(w);
        w.ShowDialog();
    }
}

using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using MedControl.Models;
using MedControl.ViewModels;
using MedControl.Services;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using StackPanel = System.Windows.Controls.StackPanel;
using ScrollViewer = System.Windows.Controls.ScrollViewer;
using UniformGrid = System.Windows.Controls.Primitives.UniformGrid;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TextWrapping = System.Windows.TextWrapping;
using FontWeights = System.Windows.FontWeights;
using ScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility;
using DataObject = System.Windows.DataObject;
using DataFormats = System.Windows.DataFormats;
using MessageBox = System.Windows.MessageBox;

namespace MedControl.Views;

// Новое окно ввода: тонометры — колонками рядом (название + верхнее/нижнее/пульс),
// ниже — оценка здоровья 1–10, ниже — комментарии, внизу OK/Отмена.
// Значения вводятся вручную. После OK — окно проверки введённых данных.
public sealed class MeasureMultiForm : Window
{
    private readonly AppServices _ctx;
    private readonly List<Tonometer> _tonos;
    private readonly List<(TextBox Sys, TextBox Dia, TextBox Pulse)> _fields = new();
    private readonly TextBox _comment = new();
    private readonly List<Button> _wbButtons = new();
    private int _wbScore;

    private MeasureMultiForm(AppServices ctx, List<Tonometer> tonos)
    {
        _ctx = ctx;
        _tonos = tonos;
        Title = "Новое измерение";
        Width = 720;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = true;
        FontSize = 16;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var p = new StackPanel { Margin = new Thickness(14) };

        var cols = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var t in tonos)
        {
            var col = new StackPanel { Width = 210, Margin = new Thickness(0, 0, 16, 0) };
            col.Children.Add(new TextBlock { Text = t.Name, FontWeight = FontWeights.Bold, FontSize = 17, TextWrapping = TextWrapping.Wrap });
            var sys = NumBox();
            var dia = NumBox();
            var pulse = NumBox();
            _fields.Add((sys, dia, pulse));
            col.Children.Add(new TextBlock { Text = "Верхнее:", Margin = new Thickness(0, 8, 0, 0) });
            col.Children.Add(sys);
            col.Children.Add(new TextBlock { Text = "Нижнее:", Margin = new Thickness(0, 6, 0, 0) });
            col.Children.Add(dia);
            col.Children.Add(new TextBlock { Text = "Пульс:", Margin = new Thickness(0, 6, 0, 0) });
            col.Children.Add(pulse);
            cols.Children.Add(col);
        }
        p.Children.Add(new ScrollViewer
        {
            Content = cols,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });

        p.Children.Add(new TextBlock { Text = "Оценка состояния здоровья (необязательно):", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 4) });
        var grid = new UniformGrid { Columns = 10 };
        for (int i = 1; i <= 10; i++)
        {
            int v = i;
            var b = new Button { Content = v.ToString(), FontSize = 18, Padding = new Thickness(6) };
            b.Click += (_, _) => { _wbScore = _wbScore == v ? 0 : v; PaintWb(); };
            _wbButtons.Add(b);
            grid.Children.Add(b);
        }
        p.Children.Add(grid);

        p.Children.Add(new TextBlock { Text = "Комментарии:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 4) });
        _comment.AcceptsReturn = true;
        _comment.Height = 80;
        _comment.TextWrapping = TextWrapping.Wrap;
        _comment.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        p.Children.Add(_comment);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", IsDefault = true, Padding = new Thickness(34, 6, 34, 6), Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Save();
        var cancel = new Button { Content = "Отмена", Padding = new Thickness(24, 6, 24, 6) };
        cancel.Click += (_, _) => DialogResult = false;
        btns.Children.Add(ok);
        btns.Children.Add(cancel);
        p.Children.Add(btns);

        Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static TextBox NumBox()
    {
        var box = new TextBox { FontSize = 20, MaxLength = 3 };
        box.PreviewTextInput += (_, e) => { e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$"); };
        DataObject.AddPastingHandler(box, (s, e) =>
        {
            if (e.DataObject.GetDataPresent(DataFormats.Text))
            {
                var t = (e.DataObject.GetData(DataFormats.Text) as string) ?? "";
                if (!Regex.IsMatch(t, "^[0-9]+$")) e.CancelCommand();
            }
            else e.CancelCommand();
        });
        return box;
    }

    private void PaintWb()
    {
        for (int i = 0; i < _wbButtons.Count; i++)
            _wbButtons[i].FontWeight = (i + 1) == _wbScore ? FontWeights.Bold : FontWeights.Normal;
    }

    private void Save()
    {
        var taken = DateTime.Now;
        var comment = _comment.Text ?? "";
        var rows = new List<(Tonometer Tono, int S, int D, int P)>();
        for (int i = 0; i < _tonos.Count; i++)
        {
            var t = _tonos[i];
            var (bs, bd, bp) = _fields[i];
            string ss = (bs.Text ?? "").Trim(), sd = (bd.Text ?? "").Trim(), sp = (bp.Text ?? "").Trim();
            if (ss == "" && sd == "" && sp == "") continue; // колонка не заполнена — пропускаем
            if (ss == "" || sd == "" || sp == "")
            {
                Steps.Msg(this, $"«{t.Name}»: заполните все три поля (верхнее, нижнее, пульс).");
                return;
            }
            if (!int.TryParse(ss, out int s) || !int.TryParse(sd, out int d) || !int.TryParse(sp, out int pu))
            {
                Steps.Msg(this, $"«{t.Name}»: только числа.");
                return;
            }
            var (ok1, e1) = ValidationService.ValidatePressure(s, d);
            if (!ok1) { Steps.Msg(this, $"«{t.Name}»: {e1}"); return; }
            var (ok2, e2) = ValidationService.ValidatePulse(pu);
            if (!ok2) { Steps.Msg(this, $"«{t.Name}»: {e2}"); return; }
            rows.Add((t, s, d, pu));
        }
        if (rows.Count == 0) { Steps.Msg(this, "Введите хотя бы один замер."); return; }

        // Окно проверки введённых данных
        var lines = rows.Select(r => $"{r.Tono.Name}: {r.S} / {r.D}, пульс {r.P}").ToList();
        lines.Add($"Время: {taken:dd.MM.yyyy HH:mm}");
        if (_wbScore >= 1) lines.Add($"Самочувствие: {_wbScore}/10");
        if (!string.IsNullOrWhiteSpace(comment)) lines.Add($"Комментарий: {comment}");
        var w = new Window
        {
            Owner = this, Title = "Проверьте введённые данные", Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 420, MaxWidth = 620, FontSize = 16,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
        };
        var p = new StackPanel { Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "Проверьте введённые данные:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        p.Children.Add(new TextBlock { Text = string.Join("\n", lines), TextWrapping = TextWrapping.Wrap });
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var save = new Button { Content = "Сохранить", IsDefault = true, Padding = new Thickness(24, 6, 24, 6), Margin = new Thickness(0, 0, 8, 0) };
        save.Click += (_, _) => { w.DialogResult = true; };
        var back = new Button { Content = "Исправить", Padding = new Thickness(24, 6, 24, 6) };
        back.Click += (_, _) => { w.DialogResult = false; };
        btns.Children.Add(save);
        btns.Children.Add(back);
        p.Children.Add(btns);
        w.Content = p;
        if (w.ShowDialog() != true) return; // вернуться к форме

        foreach (var r in rows)
        {
            _ctx.Meas.Upsert(new Measurement
            {
                TonometerId = r.Tono.Id, TonometerName = r.Tono.Name, TakenAt = taken,
                Systolic = r.S, Diastolic = r.D, Pulse = r.P, Comment = comment,
                CreatedAt = taken,
            });
        }
        if (_wbScore >= 1)
        {
            try { _ctx.WellBeing.Create(_wbScore, comment, DateTime.Now); }
            catch (Exception ex) { Steps.Msg(this, ex.Message); }
        }
        DialogResult = true;
    }

    public static bool Show(AppServices ctx, Window owner)
    {
        var tonos = ctx.Meas.GetTonometers();
        if (tonos.Count == 0) return false;
        var w = new MeasureMultiForm(ctx, tonos) { Owner = owner };
        return w.ShowDialog() == true;
    }
}

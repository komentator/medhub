using System.Windows;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBlock = System.Windows.Controls.TextBlock;
using StackPanel = System.Windows.Controls.StackPanel;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using FontWeights = System.Windows.FontWeights;
using Visibility = System.Windows.Visibility;

namespace MedControl.Views;

// Шаг 2: выбираем период отчёта.
// Выбор дат появляется только в режиме «Выбрать даты…»; в остальных режимах
// период считается от сегодняшнего дня.
public sealed class PrintPeriodDialog : Window
{
    private readonly ComboBox _period = new();
    private readonly System.Windows.Controls.DatePicker _from = new();
    private readonly System.Windows.Controls.DatePicker _to = new();
    private readonly StackPanel _rangeRow = new();
    private readonly System.Windows.Controls.TextBox _points = new();
    private (DateTime From, DateTime To, int Points)? _result;

    private PrintPeriodDialog(int chartPoints)
    {
        Title = "Печать — за какой период?";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = true;
        FontSize = 16;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var p = new StackPanel { Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "За какой период?", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });

        _period.Items.Add("Последние 30 дней");
        _period.Items.Add("День");
        _period.Items.Add("Неделя");
        _period.Items.Add("Месяц");
        _period.Items.Add("Год");
        _period.Items.Add("Выбрать даты…");
        _period.SelectedIndex = 0;
        _period.SelectionChanged += (_, _) => PaintDates();
        p.Children.Add(_period);

        _rangeRow = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        _rangeRow.Children.Add(new TextBlock { Text = "С:" });
        _from.SelectedDate = DateTime.Now.AddDays(-30);
        _from.ToolTip = "Начало периода";
        _rangeRow.Children.Add(_from);
        _rangeRow.Children.Add(new TextBlock { Text = "По:", Margin = new Thickness(0, 6, 0, 0) });
        _to.SelectedDate = DateTime.Now;
        _to.ToolTip = "Конец периода (включительно)";
        _rangeRow.Children.Add(_to);
        p.Children.Add(_rangeRow);
        PaintDates();

        var pointsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        pointsRow.Children.Add(new TextBlock { Text = "Точек на графике:", VerticalAlignment = VerticalAlignment.Center });
        _points.Text = chartPoints > 0 ? chartPoints.ToString() : "0";
        _points.Width = 90;
        _points.Margin = new Thickness(8, 0, 0, 0);
        _points.ToolTip = "Сколько последних точек показывать на графике. 0 — все точки.";
        pointsRow.Children.Add(_points);
        pointsRow.Children.Add(new TextBlock
        {
            Text = "— 0 значит все",
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = System.Windows.Media.Brushes.Gray
        });
        p.Children.Add(pointsRow);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "Создать отчёт", IsDefault = true, Padding = new Thickness(24, 6, 24, 6), Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Build();
        var cancel = new Button { Content = "Отмена", Padding = new Thickness(24, 6, 24, 6) };
        cancel.Click += (_, _) => DialogResult = false;
        btns.Children.Add(ok);
        btns.Children.Add(cancel);
        p.Children.Add(btns);

        Content = p;
    }

    // Строка выбора дат — только в режиме «Выбрать даты…».
    private void PaintDates()
    {
        bool custom = _period.SelectedIndex == 5;
        _rangeRow.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Build()
    {
        var (from, to) = Range(
            _period.SelectedIndex,
            _from.SelectedDate,
            _to.SelectedDate);
        if (to <= from) { Steps.Msg(this, "Дата «по» должна быть позже даты «с»."); return; }
        if (!int.TryParse(_points.Text.Trim(), out int points) || points < 0)
        {
            Steps.Msg(this, "Количество точек должно быть целым числом (0 — все точки).");
            return;
        }
        _result = (from, to, points);
        DialogResult = true;
    }

    /// <summary>Период по индексу режима: 0 — 30 дней, 1 — день, 2 — неделя, 3 — месяц, 4 — год, 5 — свои даты.</summary>
    public static (DateTime From, DateTime To) Range(int mode, DateTime? from, DateTime? to)
    {
        var today = DateTime.Today;
        return mode switch
        {
            1 => (today, today.AddDays(1)),
            2 => (today.AddDays(-6), today.AddDays(1)),
            3 => (today.AddDays(-29), today.AddDays(1)),
            4 => (today.AddDays(-364), today.AddDays(1)),
            5 => ((from ?? today.AddDays(-30)).Date, (to ?? today).Date.AddDays(1)),
            _ => (today.AddDays(-30), today.AddDays(1)),
        };
    }

    public static (DateTime From, DateTime To, int Points)? Show(Window owner, int chartPoints)
    {
        var w = new PrintPeriodDialog(chartPoints) { Owner = owner };
        UiTips.Apply(w);
        return w.ShowDialog() == true ? w._result : null;
    }
}

using System.Windows;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;
using TextBlock = System.Windows.Controls.TextBlock;
using StackPanel = System.Windows.Controls.StackPanel;
using CheckBox = System.Windows.Controls.CheckBox;
using SelectionMode = System.Windows.Controls.SelectionMode;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TextWrapping = System.Windows.TextWrapping;
using FontWeights = System.Windows.FontWeights;
using Visibility = System.Windows.Visibility;

namespace MedControl.Views;

// Окно печати: что печатать (включая графики) + период (день/неделя/месяц/год/даты с–по).
public sealed record PrintRequest(string Kind, DateTime From, DateTime To);

public sealed class QuickPrintDialog : Window
{
    private readonly ListBox _kind = new();
    private readonly ComboBox _period = new();
    private readonly System.Windows.Controls.DatePicker _anchor = new();
    private readonly StackPanel _dateRow = new();
    private readonly System.Windows.Controls.DatePicker _from = new();
    private readonly System.Windows.Controls.DatePicker _to = new();
    private readonly StackPanel _rangeRow = new();
    private PrintRequest? _result;

    private QuickPrintDialog()
    {
        Title = "Печать";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = true;
        FontSize = 16;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var p = new StackPanel { Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "Что печатать?", FontWeight = FontWeights.Bold });
        _kind.Items.Add("Измерения давления");
        _kind.Items.Add("Приём лекарств");
        _kind.Items.Add("Совместный отчёт");
        _kind.Items.Add("Графики");
        _kind.SelectedIndex = 0;
        _kind.SelectionMode = SelectionMode.Single;
        _kind.Margin = new Thickness(0, 4, 0, 0);
        p.Children.Add(_kind);

        p.Children.Add(new TextBlock { Text = "За какой период?", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 0) });
        _period.Items.Add("Последние 30 дней");
        _period.Items.Add("День");
        _period.Items.Add("Неделя");
        _period.Items.Add("Месяц");
        _period.Items.Add("Год");
        _period.Items.Add("Выбрать даты…");
        _period.SelectedIndex = 0;
        _period.SelectionChanged += (_, _) => PaintDates();
        p.Children.Add(_period);

        _dateRow = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        _dateRow.Children.Add(new TextBlock { Text = "Дата:" });
        _anchor.SelectedDate = DateTime.Now;
        _dateRow.Children.Add(_anchor);
        p.Children.Add(_dateRow);

        _rangeRow = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        _rangeRow.Children.Add(new TextBlock { Text = "С:" });
        _from.SelectedDate = DateTime.Now.AddDays(-30);
        _rangeRow.Children.Add(_from);
        _rangeRow.Children.Add(new TextBlock { Text = "По:", Margin = new Thickness(0, 6, 0, 0) });
        _to.SelectedDate = DateTime.Now;
        _rangeRow.Children.Add(_to);
        p.Children.Add(_rangeRow);
        PaintDates();

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "Печатать", IsDefault = true, Padding = new Thickness(24, 6, 24, 6), Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Save();
        var cancel = new Button { Content = "Отмена", Padding = new Thickness(24, 6, 24, 6) };
        cancel.Click += (_, _) => DialogResult = false;
        btns.Children.Add(ok);
        btns.Children.Add(cancel);
        p.Children.Add(btns);

        Content = p;
    }

    private void PaintDates()
    {
        bool custom = _period.SelectedIndex == 5;
        _dateRow.Visibility = custom ? Visibility.Collapsed : Visibility.Visible;
        _rangeRow.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Save()
    {
        if (_kind.SelectedItem == null) { Steps.Msg(this, "Выберите, что печатать."); return; }
        string kind = _kind.SelectedIndex switch { 1 => "intakes", 2 => "joint", 3 => "charts", _ => "measurements" };
        var anchor = _anchor.SelectedDate ?? DateTime.Now;
        (DateTime from, DateTime to) = _period.SelectedIndex switch
        {
            1 => (anchor.Date, anchor.Date.AddDays(1)),
            2 => (anchor.Date.AddDays(-6), anchor.Date.AddDays(1)),
            3 => (anchor.Date.AddDays(-29), anchor.Date.AddDays(1)),
            4 => (anchor.Date.AddDays(-364), anchor.Date.AddDays(1)),
            5 => ((_from.SelectedDate ?? DateTime.Now.AddDays(-30)).Date,
                  ((_to.SelectedDate ?? DateTime.Now).Date).AddDays(1)),
            _ => (DateTime.Now.AddDays(-30), DateTime.Now.AddDays(1)),
        };
        if (to <= from) { Steps.Msg(this, "Дата «по» должна быть позже даты «с»."); return; }
        _result = new PrintRequest(kind, from, to);
        DialogResult = true;
    }

    public static PrintRequest? Show(Window owner)
    {
        var w = new QuickPrintDialog { Owner = owner };
        UiTips.Apply(w);
        return w.ShowDialog() == true ? w._result : null;
    }
}

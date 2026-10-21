using System.Windows;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using TextBlock = System.Windows.Controls.TextBlock;
using StackPanel = System.Windows.Controls.StackPanel;
using SelectionMode = System.Windows.Controls.SelectionMode;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using FontWeights = System.Windows.FontWeights;

namespace MedControl.Views;

// Шаг 1: выбираем, что печатать.
public sealed class PrintKindDialog : Window
{
    private readonly ListBox _kind = new();
    private string? _result;

    private PrintKindDialog()
    {
        Title = "Печать — что печатать?";
        Width = 460;
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
        _kind.ToolTip = "Выберите раздел отчёта и нажмите «Далее»";
        p.Children.Add(_kind);

        var btns = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "Далее", IsDefault = true, Padding = new Thickness(24, 6, 24, 6), Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Next();
        var cancel = new Button { Content = "Отмена", Padding = new Thickness(24, 6, 24, 6) };
        cancel.Click += (_, _) => DialogResult = false;
        btns.Children.Add(ok);
        btns.Children.Add(cancel);
        p.Children.Add(btns);

        Content = p;
    }

    private void Next()
    {
        if (_kind.SelectedItem == null) { Steps.Msg(this, "Выберите, что печатать."); return; }
        _result = _kind.SelectedIndex switch { 1 => "intakes", 2 => "joint", 3 => "charts", _ => "measurements" };
        DialogResult = true;
    }

    public static string? Show(Window owner)
    {
        var w = new PrintKindDialog { Owner = owner };
        UiTips.Apply(w);
        return w.ShowDialog() == true ? w._result : null;
    }
}

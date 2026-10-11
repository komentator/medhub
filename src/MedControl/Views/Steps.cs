using System.Windows;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using StackPanel = System.Windows.Controls.StackPanel;
using CheckBox = System.Windows.Controls.CheckBox;
using SelectionMode = System.Windows.Controls.SelectionMode;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility;
using MessageBox = System.Windows.MessageBox;
using FontWeights = System.Windows.FontWeights;
using TextWrapping = System.Windows.TextWrapping;
using MedControl.ViewModels;

namespace MedControl.Views;

// Безопасные файловые диалоги: владелец (модальность), стартовая папка,
// запоминание последней папки, запись в журнал (диагностика зависаний).
public static class FileDlg
{
    public static string InitialDir(AppServices ctx)
    {
        try
        {
            var d = ctx.Settings.LastFileDir;
            if (!string.IsNullOrWhiteSpace(d) && System.IO.Directory.Exists(d)) return d;
        }
        catch { }
        try
        {
            var docs = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
            if (System.IO.Directory.Exists(docs)) return docs;
        }
        catch { }
        return AppDomain.CurrentDomain.BaseDirectory;
    }

    public static void Remember(AppServices ctx, string? file)
    {
        try
        {
            var d = System.IO.Path.GetDirectoryName(file ?? "");
            if (!string.IsNullOrEmpty(d) && System.IO.Directory.Exists(d))
                ctx.Settings.LastFileDir = d;
        }
        catch { }
    }
}

// Пошаговый ввод: каждый параметр — отдельное окно поверх вызывающего.
// Все окна Topmost + Owner, крупный шрифт, без ручного ввода форматов:
// числа и время выбираются из списков, даты — календарём.
public static class Steps
{
    private static Window Base(Window? owner, string title)
    {
        return new Window
        {
            Owner = owner,
            Title = title,
            Topmost = true,
            WindowStartupLocation = owner == null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 460,
            MaxWidth = 620,
            FontSize = 16,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };
    }

    private static StackPanel Panel(string prompt)
    {
        var p = new StackPanel { Margin = new Thickness(16) };
        p.Children.Add(new TextBlock
        {
            Text = prompt, TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12),
        });
        return p;
    }

    private static StackPanel Buttons(Window w, Button ok, string okText = "Далее")
    {
        ok.Content = okText;
        ok.IsDefault = true;
        ok.Margin = new Thickness(0, 12, 8, 0);
        var cancel = new Button { Content = "Отмена", Margin = new Thickness(0, 12, 0, 0) };
        cancel.Click += (_, _) => { w.DialogResult = false; };
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(ok);
        row.Children.Add(cancel);
        var p = new StackPanel();
        p.Children.Add(row);
        return p;
    }

    public static void Msg(Window? owner, string text)
    {
        try { MessageBox.Show(owner ?? System.Windows.Application.Current?.MainWindow, text, "MedControl"); }
        catch { MessageBox.Show(text, "MedControl"); }
    }

    // Текст (для комментария — многострочный, можно пустой).
    public static string? AskText(Window? owner, string title, string prompt, string initial = "",
        bool multiline = false, bool allowEmpty = true, Func<string, string?>? validate = null)
    {
        var w = Base(owner, title);
        var p = Panel(prompt);
        var box = new TextBox { Text = initial ?? "", Margin = new Thickness(0, 0, 0, 4), FontSize = 16 };
        if (multiline) { box.AcceptsReturn = true; box.Height = 120; box.TextWrapping = TextWrapping.Wrap; box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
        else box.MaxLength = 100;
        p.Children.Add(box);
        string? result = null;
        var ok = new Button();
        ok.Click += (_, _) =>
        {
            var v = box.Text ?? "";
            if (!allowEmpty && string.IsNullOrWhiteSpace(v)) { Msg(w, "Поле не должно быть пустым."); return; }
            if (validate != null)
            {
                var err = validate(v);
                if (err != null) { Msg(w, err); return; }
            }
            result = v;
            w.DialogResult = true;
        };
        p.Children.Add(Buttons(w, ok));
        w.Content = p;
        return w.ShowDialog() == true ? result : null;
    }

    // Число выбором из списка (без ручного ввода).
    public static int? AskInt(Window? owner, string title, string prompt, IList<int> options, int initial)
    {
        var w = Base(owner, title);
        var p = Panel(prompt);
        var cb = new ComboBox { FontSize = 20, Margin = new Thickness(0, 0, 0, 4) };
        foreach (var o in options) cb.Items.Add(o);
        cb.SelectedItem = options.Contains(initial) ? initial : options[0];
        p.Children.Add(cb);
        int? result = null;
        var ok = new Button();
        ok.Click += (_, _) => { result = (int)cb.SelectedItem; w.DialogResult = true; };
        p.Children.Add(Buttons(w, ok));
        w.Content = p;
        return w.ShowDialog() == true ? result : null;
    }

    // Один вариант из списка.
    public static int AskChoiceIndex(Window? owner, string title, string prompt, IList<string> options, int selected = 0)
    {
        var w = Base(owner, title);
        var p = Panel(prompt);
        var lb = new ListBox { FontSize = 18, SelectionMode = SelectionMode.Single, MaxHeight = 300 };
        foreach (var o in options) lb.Items.Add(o);
        lb.SelectedIndex = Math.Clamp(selected, 0, options.Count - 1);
        p.Children.Add(lb);
        int result = -1;
        var ok = new Button();
        ok.Click += (_, _) =>
        {
            if (lb.SelectedIndex < 0) { Msg(w, "Выберите вариант."); return; }
            result = lb.SelectedIndex;
            w.DialogResult = true;
        };
        p.Children.Add(Buttons(w, ok, "Выбрать"));
        w.Content = p;
        return w.ShowDialog() == true ? result : -1;
    }

    // Обобщённый выбор объекта (отображаются только названия, без ID).
    public static T? AskObject<T>(Window? owner, string title, string prompt, IList<T> options, Func<T, string> label, int selected = 0) where T : class
    {
        var labels = options.Select(label).ToList();
        int idx = AskChoiceIndex(owner, title, prompt, labels, selected);
        return idx < 0 ? null : options[idx];
    }

    // Несколько вариантов галочками (дни недели).
    public static List<int> AskMulti(Window? owner, string title, string prompt, IList<string> options, IEnumerable<int>? preselected = null)
    {
        var w = Base(owner, title);
        var p = Panel(prompt);
        var pre = preselected?.ToHashSet() ?? new HashSet<int>();
        var boxes = new List<CheckBox>();
        for (int i = 0; i < options.Count; i++)
        {
            var cb = new CheckBox { Content = options[i], FontSize = 18, IsChecked = pre.Contains(i), Margin = new Thickness(0, 4, 0, 4) };
            boxes.Add(cb);
            p.Children.Add(cb);
        }
        var result = new List<int>();
        var ok = new Button();
        ok.Click += (_, _) =>
        {
            result = boxes.Select((b, i) => (b, i)).Where(x => x.b.IsChecked == true).Select(x => x.i).ToList();
            if (result.Count == 0) { Msg(w, "Выберите хотя бы один вариант."); return; }
            w.DialogResult = true;
        };
        p.Children.Add(Buttons(w, ok, "Выбрать"));
        w.Content = p;
        return w.ShowDialog() == true ? result : new List<int>();
    }

    // Дата календарём. Возвращает (дата, отмена): «Без даты» = (null, false).
    public static (DateTime? Date, bool Cancelled) AskDate(Window? owner, string title, string prompt, DateTime? initial, bool allowEmpty)
    {
        var w = Base(owner, title);
        var p = Panel(prompt);
        var dp = new System.Windows.Controls.DatePicker { SelectedDate = initial ?? DateTime.Now, FontSize = 16 };
        p.Children.Add(dp);
        DateTime? result = null;
        bool chosen = false;
        var ok = new Button();
        ok.Click += (_, _) =>
        {
            if (dp.SelectedDate == null) { Msg(w, "Выберите дату."); return; }
            result = dp.SelectedDate.Value;
            chosen = true;
            w.DialogResult = true;
        };
        p.Children.Add(Buttons(w, ok, allowEmpty ? "Далее" : "Выбрать"));
        if (allowEmpty)
        {
            var skip = new Button { Content = "Без даты", Margin = new Thickness(0, 8, 0, 0) };
            skip.Click += (_, _) => { w.DialogResult = true; };
            p.Children.Add(skip);
        }
        w.Content = p;
        if (w.ShowDialog() != true) return (null, true);
        return (chosen ? result : null, false);
    }

    // Время двумя списками: часы и минуты (без ручного ввода).
    public static TimeOnly? AskTime(Window? owner, string title, string prompt, TimeOnly initial)
    {
        var w = Base(owner, title);
        var p = Panel(prompt);
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        var hh = new ComboBox { FontSize = 22, Width = 100, Margin = new Thickness(0, 0, 8, 0) };
        var mm = new ComboBox { FontSize = 22, Width = 100 };
        for (int h = 0; h < 24; h++) hh.Items.Add(h.ToString("D2"));
        for (int m = 0; m < 60; m++) mm.Items.Add(m.ToString("D2"));
        hh.SelectedIndex = initial.Hour;
        mm.SelectedIndex = initial.Minute;
        row.Children.Add(hh);
        row.Children.Add(new TextBlock { Text = " : ", FontSize = 22, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(mm);
        p.Children.Add(row);
        TimeOnly? result = null;
        var ok = new Button();
        ok.Click += (_, _) =>
        {
            result = new TimeOnly(hh.SelectedIndex, mm.SelectedIndex);
            w.DialogResult = true;
        };
        p.Children.Add(Buttons(w, ok, "Выбрать"));
        w.Content = p;
        return w.ShowDialog() == true ? result : null;
    }

    // Вопрос Да/Нет (закрытие окна = отмена).
    public static bool? AskYesNo(Window? owner, string title, string prompt)
    {
        var w = Base(owner, title);
        var p = Panel(prompt);
        bool? result = null;
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        var yes = new Button { Content = "Да", FontSize = 18, Padding = new Thickness(30, 8, 30, 8), Margin = new Thickness(0, 0, 12, 0) };
        var no = new Button { Content = "Нет", FontSize = 18, Padding = new Thickness(30, 8, 30, 8) };
        yes.Click += (_, _) => { result = true; w.DialogResult = true; };
        no.Click += (_, _) => { result = false; w.DialogResult = true; };
        row.Children.Add(yes);
        row.Children.Add(no);
        p.Children.Add(row);
        w.Content = p;
        return w.ShowDialog() == true ? result : null;
    }
}

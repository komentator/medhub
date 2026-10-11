using System.Windows;
using MedControl.ViewModels;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using StackPanel = System.Windows.Controls.StackPanel;
using UniformGrid = System.Windows.Controls.Primitives.UniformGrid;
using ScrollViewer = System.Windows.Controls.ScrollViewer;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace MedControl.Views;

public sealed class WellBeingDialog : Window
{
    private readonly AppServices _ctx;
    private TextBox _comment = new();
    public WellBeingDialog(AppServices ctx, string title)
    {
        _ctx = ctx;
        Title = "Самочувствие";
        Width = 480; Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = true; // всегда поверх вызвавшего окна
        var p = new StackPanel { Margin = new Thickness(10) };
        p.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        var grid = new UniformGrid { Columns = 5 };
        for (int i = 1; i <= 10; i++)
        {
            int v = i;
            var b = new Button { Content = v.ToString(), FontSize = 20, Padding = new Thickness(10) };
            b.Click += (_, _) => Save(v);
            grid.Children.Add(b);
        }
        p.Children.Add(grid);
        p.Children.Add(new TextBlock { Text = "Комментарий (необязательно):" });
        p.Children.Add(_comment);
        var skip = new Button { Content = "Пропустить" };
        skip.Click += (_, _) => DialogResult = false;
        p.Children.Add(skip);
        Content = p;
    }
    private void Save(int score)
    {
        try { _ctx.WellBeing.Create(score, _comment.Text ?? "", DateTime.Now); DialogResult = true; }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }
}

public sealed class WizardWindow : Window
{
    private readonly AppServices _ctx;
    private string? _packFile;
    private TextBlock _packLabel = new();
    private TextBox _netIp = new();
    private TextBlock _netStatus = new();

    public WizardWindow(AppServices ctx)
    {
        _ctx = ctx;
        Title = "MedControl — первый запуск"; Width = 560; Height = 640; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        var p = new StackPanel { Margin = new Thickness(14) };
        p.Children.Add(new TextBlock { Text = "Добро пожаловать в MedControl!", TextWrapping = TextWrapping.Wrap });
        p.Children.Add(new TextBlock
        {
            Text = "Если у вас есть файл переноса (.mcpack) с другого компьютера — " +
                   "выберите его: база, фото и инструкции восстановятся, а адрес ведущего " +
                   "компьютера подставится сам для первой синхронизации.\nИначе начните с нуля:",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8),
        });
        var pick = new Button { Content = "Выбрать файл переноса…", HorizontalAlignment = HorizontalAlignment.Left };
        pick.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "Перенос MedControl (*.mcpack)|*.mcpack", InitialDirectory = FileDlg.InitialDir(_ctx), RestoreDirectory = true, CheckFileExists = true };
            if (dlg.ShowDialog(this) == true) { FileDlg.Remember(_ctx, dlg.FileName); _packFile = dlg.FileName; PaintPack(); }
        };
        p.Children.Add(pick);
        _packLabel.TextWrapping = TextWrapping.Wrap;
        _packLabel.Margin = new Thickness(0, 4, 0, 0);
        PaintPack();
        p.Children.Add(_packLabel);
        p.Children.Add(new TextBlock
        {
            Text = "…или перенесите базу по сети: впишите IP ведущего компьютера " +
                   "(на нём должны быть включены сервер и одинаковый ключ синхронизации):",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 4),
        });
        _netIp.Margin = new Thickness(0, 0, 0, 4);
        p.Children.Add(_netIp);
        var netBtn = new Button { Content = "Перенести по сети", HorizontalAlignment = HorizontalAlignment.Left };
        netBtn.Click += (_, _) => DoNetTransfer();
        p.Children.Add(netBtn);
        _netStatus.TextWrapping = TextWrapping.Wrap;
        _netStatus.Margin = new Thickness(0, 4, 0, 0);
        p.Children.Add(_netStatus);
        p.Children.Add(new TextBlock
        {
            Text = "1. Добавьте первый тонометр в разделе Настройки.\n2. Добавьте лекарства в разделе Лекарства.\n3. Проверьте напоминание кнопкой «Проверить напоминание».",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10),
        });
        var done = new Button { Content = "Готово", IsDefault = true };
        done.Click += (_, _) => Finish();
        p.Children.Add(done);
        Content = new ScrollViewer { Content = p };
    }

    private void PaintPack()
        => _packLabel.Text = _packFile == null ? "Файл переноса не выбран." : "Файл: " + _packFile;

    private void DoNetTransfer()
    {
        _netStatus.Text = "Перенос…";
        var (ok, msg) = MedControl.Sync.NetworkTransfer.Run(_ctx.Sync, _ctx.Transfer, _ctx.Log, _netIp.Text ?? "");
        _netStatus.Text = (ok ? "Готово. " : "") + msg;
        if (ok) { _packFile = null; PaintPack(); }
    }

    private void Finish()
    {
        if (_packFile != null)
        {
            var (ok, msg) = _ctx.Transfer.RestorePack(_packFile);
            MessageBox.Show(msg, "MedControl", MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Error);
            if (!ok) return;
        }
        _ctx.Settings.WizardDone = true;
        DialogResult = true;
    }
}

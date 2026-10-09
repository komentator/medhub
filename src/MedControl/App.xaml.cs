using System.IO;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MedControl.Data;
using MedControl.Services;
using MedControl.ViewModels;
using MedControl.Views;
using Microsoft.Win32;

namespace MedControl;

public partial class App : Application
{
    private static SingleInstance? _instance;
    private AppServices? _ctx;
    private System.Windows.Forms.NotifyIcon? _tray;
    private EventWaitHandle? _showEvent;
    private Thread? _showThread;
    private volatile bool _showStop;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ev) =>
        {
            FatalReport("Dispatcher", ev.Exception);
            ev.Handled = true;
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
            FatalReport("AppDomain", ev.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            FatalReport("Task", ev.Exception);
            ev.SetObserved();
        };
        _instance = new SingleInstance();
        if (!_instance.Acquire())
        {
            // Уже висит в трее: будим первый процесс (откроет окно) и выходим молча.
            if (!SingleInstance.SignalFirst())
                MessageBox.Show("MedControl уже запущен.", "MedControl");
            Shutdown();
            return;
        }
        // Слушаем просьбы показаться (повторный запуск с ярлыка).
        try
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SingleInstance.ShowEventName);
            _showThread = new Thread(() =>
            {
                while (!_showStop)
                {
                    try
                    {
                        if (_showEvent?.WaitOne(1000) == true && !_showStop)
                            Dispatcher.Invoke(ShowMain);
                    }
                    catch { Thread.Sleep(1000); }
                }
            })
            { IsBackground = true, Name = "ShowSignal" };
            _showThread.Start();
        }
        catch { }

        _ctx = new AppServices();
        bool minimized = e.Args.Contains("--minimized");

        // Проверка целостности при признаках повреждения (§3): пробуем открыть БД
        try
        {
            using var c = _ctx.Db.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Medications;";
            cmd.ExecuteScalar();
        }
        catch (Exception ex) when (Db.IsCorruptionMessage(ex))
        {
            var (ok, msg, used) = _ctx.Backups.AutoRecover(ex.Message);
            MessageBox.Show(ok ? $"Обнаружено повреждение БД. Восстановлено из копии:{Environment.NewLine}{used}{Environment.NewLine}{msg}"
                : $"Обнаружено повреждение БД, валидных копий нет.{Environment.NewLine}{msg}",
                "MedControl — восстановление", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (ok) System.Diagnostics.Process.Start(Environment.ProcessPath!);
            Shutdown();
            return;
        }

        // Трей (§37)
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Visible = true,
            Text = "MedControl",
            Icon = LoadAppIcon(),
        };
        _tray.DoubleClick += (_, _) => ShowMain();
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => ShowMain());
        menu.Items.Add("Настройки", null, (_, _) => { ShowMain(); (MainWindow as MainWindow)?.GoSettings(); });
        menu.Items.Add("Выход", null, (_, _) => { _tray.Visible = false; if (MainWindow is MainWindow mw) mw.AllowClose = true; Shutdown(); });
        _tray.ContextMenuStrip = menu;

        var main = new MainWindow(_ctx);
        MainWindow = main;
        _ctx.Scheduler.Due += doses => Dispatcher.Invoke(() =>
        {
            var w = new ReminderWindow(_ctx, doses);
            w.ShowDialog();
        });
        _ctx.Scheduler.Start();
        _ctx.Synced += () => Dispatcher.Invoke(() =>
        {
            (MainWindow as MainWindow)?.RefreshAll();
            _ctx.Scheduler.Reschedule();
        });

        // Сервер синхронизации (если включён в настройках)
        if (_ctx.Settings.SyncServerEnabled)
        {
            try { _ctx.Sync.StartServer(); }
            catch (Exception ex)
            {
                _ctx.Log.Write("Sync", "Автозапуск сервера: " + ex.Message);
                MessageBox.Show("Не удалось запустить сервер синхронизации (порт занят?).\n" + ex.Message,
                    "MedControl — синхронизация", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Resume из сна/гибернации + смена системного времени (§39–40)
        SystemEvents.PowerModeChanged += (_, ev) =>
        {
            if (ev.Mode == PowerModes.Resume) Dispatcher.Invoke(() => { _ctx.Reminders.RecalcMissed(DateTime.Now); _ctx.Scheduler.Reschedule(); });
        };
        SystemEvents.TimeChanged += (_, _) => Dispatcher.Invoke(() => { _ctx.Reminders.RecalcMissed(DateTime.Now); _ctx.Scheduler.Reschedule(); });

        if (!_ctx.Settings.WizardDone)
        {
            var wiz = new WizardWindow(_ctx);
            wiz.ShowDialog();
        }

        // После восстановления из пакета — первая связь с ведущим компьютером.
        if (_ctx.Settings.PendingInitialSync)
        {
            _ctx.Settings.PendingInitialSync = false;
            var peer = _ctx.Settings.PeerAddress;
            Task.Run(() =>
            {
                var (ok, msg) = _ctx.Sync.SyncNow(peer);
                _ctx.Log.Write("Sync", "Первая связь: " + msg);
                Dispatcher.Invoke(() =>
                {
                    try { _tray?.ShowBalloonTip(6000, "MedControl", ok ? "Связь с ведущим установлена." : "Не удалось связаться: " + msg, System.Windows.Forms.ToolTipIcon.Info); } catch { }
                    (MainWindow as MainWindow)?.RefreshAll();
                });
            });
        }

        if (!minimized) main.Show();
        _ctx.Log.Write("App", "Главное окно инициализировано");
    }

    private void ShowMain()
    {
        if (MainWindow is MainWindow w)
        {
            w.Show();
            w.WindowState = WindowState.Normal;
            // Вытаскиваем поверх остальных (иначе Windows только мигает иконкой).
            w.Topmost = true;
            w.Topmost = false;
            w.Activate();
        }
    }

    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/app.ico");
            using var s = System.Windows.Application.GetResourceStream(uri)?.Stream;
            if (s != null) return new System.Drawing.Icon(s);
        }
        catch { }
        return System.Drawing.SystemIcons.Application;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _showStop = true;
            try { _showEvent?.Set(); } catch { }
            _showThread?.Join(1500);
            _showEvent?.Dispose();
        }
        catch { }
        try { _ctx?.Sync.Dispose(); } catch { }
        try { _ctx?.Volume.Restore(); } catch { }
        try { _ctx?.Log.Write("App", "Завершение MedControl"); } catch { }
        _tray?.Dispose();
        base.OnExit(e);
    }

    private static void FatalReport(string source, Exception? ex)
    {
        var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex?.GetType().Name}: {ex?.Message}{Environment.NewLine}{ex?.StackTrace}{Environment.NewLine}";
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MedControl", "Logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "medcontrol.log"), text);
            File.WriteAllText(Path.Combine(dir, $"crash_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log"), text);
        }
        catch { }
        try { MessageBox.Show("MedControl завершён из-за ошибки. Подробности записаны в журнал (папка Logs).\n\n" + ex?.Message, "MedControl — ошибка", MessageBoxButton.OK, MessageBoxImage.Error); }
        catch { }
    }
}

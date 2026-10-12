using System.Windows;
using MedControl.Scheduling;
using MedControl.ViewModels;
using MedControl.Models;

namespace MedControl.Views;

// §10. Собственное окно поверх остальных, нельзя закрыть крестиком — только «Принял»/«Отложить».
public partial class ReminderWindow : Window
{
    private readonly AppServices _ctx;
    private readonly List<PlannedDose> _doses;
    private readonly bool _isTest;
    private bool _closedByButton;

    public ReminderWindow(AppServices ctx, List<PlannedDose> doses, bool isTest = false)
    {
        InitializeComponent();
        UiTips.Apply(this);
        _ctx = ctx; _doses = doses; _isTest = isTest;
        // Окно — 80% рабочей области экрана, по центру.
        var wa = SystemParameters.WorkArea;
        Width = wa.Width * 0.8;
        Height = wa.Height * 0.8;
        Left = wa.Left + wa.Width * 0.1;
        Top = wa.Top + wa.Height * 0.1;
        // Размер шрифта окна напоминания настраивается независимо (§35).
        double fs = ctx.Settings.ReminderFontSize;
        if (fs < 10) fs = 10;
        if (fs > 48) fs = 48;
        TitleText.FontSize = fs + 8;
        BodyText.FontSize = fs;
        TakenButton.FontSize = fs + 2;
        SnoozeButton.FontSize = fs + 2;
        BodyText.Text = string.Join(Environment.NewLine + "— — —" + Environment.NewLine,
            doses.Select(d => $"{d.ScheduledAt:HH:mm} — {d.MedicationName}{Environment.NewLine}{d.Description}{Environment.NewLine}Доза: {d.Dosage}"));
        Closing += (_, e) =>
        {
            if (!_closedByButton) e.Cancel = true; // крестик не закрывает (§10)
        };
        if (!isTest) ctx.Reminders.MarkShown(doses, DateTime.Now); // время показа для «Подробнее»
    }

    private void OnTaken(object s, RoutedEventArgs e)
    {
        _closedByButton = true;
        if (!_isTest)
        {
            _ctx.Reminders.ConfirmGroup(_doses, DateTime.Now, ConfirmMethod.Reminder);
            // Громкость восстанавливаем только когда не осталось активных событий (§11).
            if (_ctx.Reminders.GetActive(DateTime.Now).Count == 0)
                _ctx.Volume.Restore();
            TryBackup();
            var wb = new WellBeingDialog(_ctx, "Оценить самочувствие после приёма? (необязательно)") { Owner = this, Topmost = true };
            wb.ShowDialog();
        }
        Close();
    }

    private void OnSnooze(object s, RoutedEventArgs e)
    {
        _closedByButton = true;
        if (!_isTest) _ctx.Reminders.SnoozeGroup(_doses);
        // Отложенное не восстанавливает громкость (§11)
        Close();
    }

    private void TryBackup()
    {
        try
        {
            var today = DateTime.Now.Date;
            var planned = _ctx.Intakes.GetRange(today, today.AddDays(1)).Select(i => (i.ScheduledAt, i.Status)).ToList();
            _ctx.Backups.TryDailyBackup(planned);
        }
        catch { }
    }
}

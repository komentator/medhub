using System.Windows;
using MedControl.Models;
using MedControl.ViewModels;
using MedControl.Services;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using StackPanel = System.Windows.Controls.StackPanel;
using TextWrapping = System.Windows.TextWrapping;
using FontWeights = System.Windows.FontWeights;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace MedControl.Views;

// Пошаговые мастера (оставшиеся): лекарства и внеплановый приём.
// Возвращают true, если данные сохранены; false/null на любом шаге — отмена.
public static class MedicationWizard
{
    private static readonly string[] WeekDays = ["Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота", "Воскресенье"];

    public static bool Run(AppServices ctx, Window owner, Medication? edit)
    {
        var m = edit ?? new Medication();
        if (AskName(owner, m) == null) return false;
        if (!AskSchedule(ctx, owner, m, edit != null)) return false;
        m.IsActive = true;
        ctx.Meds.Save(m);
        ctx.Scheduler.Reschedule();
        return true;
    }

    // Только расписание существующего лекарства (для потока «внеплановый → в расписание»).
    public static bool RunScheduleOnly(AppServices ctx, Window owner, Medication m)
    {
        if (!AskSchedule(ctx, owner, m, isEdit: false)) return false;
        m.IsActive = true;
        ctx.Meds.Save(m);
        ctx.Scheduler.Reschedule();
        return true;
    }

    private static Medication? AskName(Window owner, Medication m)
    {
        var name = Steps.AskText(owner, "Название", "Шаг 1. Название лекарства (до 100 символов):",
            m.Name, allowEmpty: false, validate: v =>
                string.IsNullOrWhiteSpace(v) ? "Укажите название." :
                v.Trim().Length > 100 ? "Название — до 100 символов." : null);
        if (name == null) return null;
        var desc = Steps.AskText(owner, "Назначение", "Шаг 2. Назначение/описание (можно пропустить):",
            m.Description, multiline: true, allowEmpty: true);
        if (desc == null) return null;
        m.Name = name.Trim();
        m.Description = desc ?? "";
        return m;
    }

    private static bool AskSchedule(AppServices ctx, Window owner, Medication m, bool isEdit)
    {
        // Времена приёма: по одному окну на время + дозировку
        var doses = isEdit ? m.Doses.ToList() : new List<MedicationDose>();
        if (isEdit) doses.Clear(); // расписание задаём заново
        while (true)
        {
            var t = Steps.AskTime(owner, "Время приёма", doses.Count == 0
                ? "Время приёма:" : $"Время приёма №{doses.Count + 1}:", new TimeOnly(8, 0));
            if (t == null)
            {
                if (doses.Count == 0) return false; // ни одного времени — отмена
                break;
            }
            var dg = Steps.AskText(owner, "Дозировка", $"Дозировка на {t.Value:HH:mm} (например: 1 таблетка):",
                "", allowEmpty: true, validate: v => v.Length > 100 ? "Дозировка — до 100 символов." : null);
            if (dg == null) { if (doses.Count == 0) return false; break; }
            doses.Add(new MedicationDose { MedicationId = m.Id, Time = t.Value, Dosage = dg.Trim() });
            var more = Steps.AskYesNo(owner, "Ещё время", "Добавить ещё одно время приёма?");
            if (more != true) break;
        }
        if (doses.Count == 0) return false;
        m.Doses = doses;

        int mode = Steps.AskChoiceIndex(owner, "Режим", "Приём:",
            ["Каждый день", "По дням недели"], m.Mode == ScheduleMode.Daily ? 0 : 1);
        if (mode < 0) return false;
        m.Mode = mode == 0 ? ScheduleMode.Daily : ScheduleMode.Weekly;
        if (m.Mode == ScheduleMode.Weekly)
        {
            var days = Steps.AskMulti(owner, "Дни недели", "Выберите дни приёма:", WeekDays, m.DaysOfWeek);
            if (days.Count == 0) return false;
            m.DaysOfWeek = days;
        }

        var (sd, c1) = Steps.AskDate(owner, "Дата начала", "Дата начала (можно без даты — постоянно):", null, allowEmpty: true);
        if (c1) return false;
        m.StartDate = sd == null ? (DateOnly?)null : DateOnly.FromDateTime(sd.Value);
        var (ed, c2) = Steps.AskDate(owner, "Дата окончания", "Дата окончания (можно без даты — постоянно):", null, allowEmpty: true);
        if (c2) return false;
        m.EndDate = ed == null ? (DateOnly?)null : DateOnly.FromDateTime(ed.Value);
        return true;
    }
}

public static class UnplannedWizard
{
    public static bool Run(AppServices ctx, Window owner)
    {
        var meds = ctx.Meds.GetAll();
        int idx = PickWindow(owner, meds);
        if (idx == -2) // «Добавить новое» — создаём и остаёмся в выборе
        {
            var created = CreateNew(ctx, owner);
            if (created == null) return false;
            meds = ctx.Meds.GetAll();
            idx = PickWindow(owner, meds, created.Id);
            if (idx < 0) return false;
        }
        else if (idx < 0) return false;

        Medication med = meds[idx];

        var dg = Steps.AskText(owner, "Дозировка", $"Шаг 2. Дозировка «{med.Name}»:",
            med.Doses.FirstOrDefault()?.Dosage ?? "", allowEmpty: true,
            validate: v => v.Length > 100 ? "Дозировка — до 100 символов." : null);
        if (dg == null) return false;

        var ok = Steps.AskYesNo(owner, "Подтверждение",
            $"Записать внеплановый приём?\n{med.Name} — {dg}\nВремя: {DateTime.Now:HH:mm}");
        if (ok != true) return false;
        ctx.Reminders.ConfirmUnplanned(med.Id, dg.Trim(), DateTime.Now);
        return true;
    }

    // Шаг 1: список лекарств (только названия) + три кнопки в ряд:
    // слева «Добавить новое», далее «Выбрать» и «Отмена».
    // Возвращает индекс, -1 = отмена, -2 = добавить новое.
    private static int PickWindow(Window owner, List<Medication> meds, Guid? preselect = null)
    {
        var w = new Window
        {
            Owner = owner, Title = "Внеплановый приём", Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 460, SizeToContent = SizeToContent.Height,
            FontSize = 16, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
        };
        var p = new StackPanel { Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "Шаг 1. Выберите лекарство:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        var lb = new ListBox { FontSize = 17, MaxHeight = 260 };
        foreach (var m in meds) lb.Items.Add(m.Name);
        if (meds.Count == 0) lb.Items.Add("(нет лекарств — добавьте новое)");
        int sel = preselect == null ? 0 : Math.Max(0, meds.FindIndex(m => m.Id == preselect));
        lb.SelectedIndex = meds.Count == 0 ? -1 : sel;
        p.Children.Add(lb);
        int result = -1;
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var add = new Button { Content = "Добавить новое", Margin = new Thickness(0, 0, 8, 0) };
        add.Click += (_, _) => { result = -2; w.DialogResult = true; };
        var pick = new Button { Content = "Выбрать", IsDefault = true, Padding = new Thickness(24, 4, 24, 4), Margin = new Thickness(0, 0, 8, 0) };
        pick.Click += (_, _) =>
        {
            if (lb.SelectedIndex < 0 || meds.Count == 0) { Steps.Msg(w, "Выберите лекарство или добавьте новое."); return; }
            result = lb.SelectedIndex;
            w.DialogResult = true;
        };
        var cancel = new Button { Content = "Отмена", Padding = new Thickness(24, 4, 24, 4) };
        cancel.Click += (_, _) => { result = -1; w.DialogResult = false; };
        row.Children.Add(add);
        row.Children.Add(pick);
        row.Children.Add(cancel);
        p.Children.Add(row);
        w.Content = p;
        w.ShowDialog();
        return result;
    }

    private static Medication? CreateNew(AppServices ctx, Window owner)
    {
        var name = Steps.AskText(owner, "Новое лекарство", "Название нового лекарства:",
            "", allowEmpty: false, validate: v =>
                string.IsNullOrWhiteSpace(v) ? "Укажите название." :
                v.Trim().Length > 100 ? "Название — до 100 символов." : null);
        if (name == null) return null;
        var med = new Medication { Name = name.Trim(), IsActive = true, Mode = ScheduleMode.Daily };
        var toSchedule = Steps.AskYesNo(owner, "Расписание",
            $"Добавить «{med.Name}» к основному приёму (задать расписание)?");
        if (toSchedule == null) return null;
        if (toSchedule == true)
        {
            if (!MedicationWizard.RunScheduleOnly(ctx, owner, med)) return null;
        }
        else ctx.Meds.Save(med);
        return med;
    }
}

// Массовый ввод: несколько лекарств × несколько времён.
// Например: 3 лекарства на 9:00, 4 в обед, 5 вечером и перед сном.
public static class BulkDoseWizard
{
    public static bool Run(AppServices ctx, Window owner)
    {
        var meds = ctx.Meds.GetAll();
        if (meds.Count == 0) { Steps.Msg(owner, "Сначала добавьте лекарства."); return false; }
        var idxs = Steps.AskMulti(owner, "Лекарства", "Шаг 1. Выберите лекарства:",
            meds.Select(m => m.Name).ToList());
        if (idxs.Count == 0) return false;
        var chosen = idxs.Select(i => meds[i]).ToList();

        var times = new List<TimeOnly>();
        while (true)
        {
            var t = Steps.AskTime(owner, "Время", times.Count == 0
                ? "Шаг 2. Время приёма (например, 09:00):"
                : $"Время №{times.Count + 1}:", new TimeOnly(9, 0));
            if (t == null)
            {
                if (times.Count == 0) return false;
                break;
            }
            if (!times.Contains(t.Value)) times.Add(t.Value);
            var more = Steps.AskYesNo(owner, "Ещё время", "Добавить ещё одно время?");
            if (more != true) break;
        }
        if (times.Count == 0) return false;

        int added = 0;
        foreach (var m in chosen)
        {
            var dg = Steps.AskText(owner, "Дозировка", $"Дозировка «{m.Name}» (применится ко всем выбранным временам):",
                m.Doses.FirstOrDefault()?.Dosage ?? "", allowEmpty: true,
                validate: v => v.Length > 100 ? "Дозировка — до 100 символов." : null);
            if (dg == null) return false;
            foreach (var t in times)
            {
                if (m.Doses.Any(d => d.Time == t)) continue;
                m.Doses.Add(new MedicationDose { MedicationId = m.Id, Time = t, Dosage = dg.Trim() });
                added++;
            }
            ctx.Meds.Save(m);
        }
        ctx.Scheduler.Reschedule();
        Steps.Msg(owner, $"Готово: добавлено времён приёма: {added}.");
        return true;
    }
}

// «Подробнее» о приёме: дата/время, таблетки, показ окна, действия пользователя.
public static class IntakeDetails
{
    public static void Show(AppServices ctx, Window owner, MedicationIntake i)
    {
        string wb = "";
        try
        {
            var scores = ctx.Wb.GetScoresByIntake([i.Id]);
            if (scores.TryGetValue(i.Id, out var s)) wb = string.Join(",", s);
        }
        catch { }
        string status = i.Status switch
        {
            IntakeStatus.Taken => "Принял",
            IntakeStatus.TakenLate => "Принял позже",
            IntakeStatus.Missed => "Пропущено",
            _ => "Внеплановый приём",
        };
        var text =
            $"Лекарство: {i.MedicationName}\n" +
            (string.IsNullOrWhiteSpace(i.MedicationDescription) ? "" : $"Назначение: {i.MedicationDescription}\n") +
            $"Дозировка: {i.Dosage}\n" +
            $"Запланировано: {i.ScheduledAt:yyyy-MM-dd HH:mm}\n" +
            $"Подтверждено: {(i.ActualAt?.ToString("yyyy-MM-dd HH:mm") ?? "—")}\n" +
            $"Окно показано: {(i.ShownAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—")}\n" +
            $"Кнопка «Отложить»: {(i.SnoozedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—")}\n" +
            $"Статус: {status}\n" +
            $"Способ: {i.ConfirmMethod}\n" +
            $"Самочувствие: {(string.IsNullOrEmpty(wb) ? "—" : wb)}\n" +
            $"Комментарий: {(string.IsNullOrWhiteSpace(i.Comment) ? "—" : i.Comment)}";
        var w = new Window
        {
            Owner = owner, Title = "Подробнее о приёме", Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 460, MaxWidth = 620, FontSize = 16,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
        };
        var p = new StackPanel { Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        var box = new TextBox { Text = i.Comment ?? "", AcceptsReturn = true, Height = 80, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        p.Children.Add(new TextBlock { Text = "Комментарий (можно изменить):", Margin = new Thickness(0, 8, 0, 0) });
        p.Children.Add(box);
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var save = new Button { Content = "Сохранить", IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        save.Click += (_, _) => { i.Comment = box.Text ?? ""; ctx.Intakes.Upsert(i); w.DialogResult = true; };
        var close = new Button { Content = "Закрыть" };
        close.Click += (_, _) => { w.DialogResult = false; };
        row.Children.Add(save);
        row.Children.Add(close);
        p.Children.Add(row);
        w.Content = p;
        if (w.ShowDialog() == true) Steps.Msg(owner, "Комментарий сохранён.");
    }

    // Группа приёмов в одно время — одно событие: все лекарства списком.
    public static void ShowGroup(AppServices ctx, Window owner, List<MedicationIntake> group)
    {
        if (group.Count == 0) return;
        if (group.Count == 1) { Show(ctx, owner, group[0]); return; }
        var first = group[0];
        Dictionary<Guid, List<int>> scores = new();
        try { scores = ctx.Wb.GetScoresByIntake(group.Select(g => g.Id)); } catch { }
        var lines = new List<string> { $"Запланировано: {first.ScheduledAt:yyyy-MM-dd HH:mm}" };
        foreach (var i in group)
        {
            string status = i.Status switch
            {
                IntakeStatus.Taken => "Принял",
                IntakeStatus.TakenLate => "Принял позже",
                IntakeStatus.Missed => "Пропущено",
                _ => "Внеплановый",
            };
            var wb = scores.TryGetValue(i.Id, out var s) ? string.Join(",", s) : "—";
            lines.Add($"• {i.MedicationName} ({i.Dosage}) — {status}, факт: {(i.ActualAt?.ToString("HH:mm") ?? "—")}, самочувствие: {wb}" +
                (string.IsNullOrWhiteSpace(i.Comment) ? "" : $", комментарий: {i.Comment}"));
        }
        lines.Add($"Окно показано: {(first.ShownAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—")}");
        lines.Add($"Кнопка «Отложить»: {(first.SnoozedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—")}");
        var w = new Window
        {
            Owner = owner, Title = "Подробнее о приёме", Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 460, MaxWidth = 640, FontSize = 16,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
        };
        var p = new StackPanel { Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = string.Join("\n", lines), TextWrapping = TextWrapping.Wrap });
        var close = new Button { Content = "Закрыть", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        close.Click += (_, _) => w.DialogResult = false;
        p.Children.Add(close);
        w.Content = p;
        w.ShowDialog();
    }
}

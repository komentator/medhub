using System.Windows;

namespace MedControl.Views;

// Запрос на печать: что и за период.
public sealed record PrintRequest(string Kind, DateTime From, DateTime To);

// Печать в два шага: 1) что печатать → «Далее»; 2) за какой период → «Создать отчёт».
public static class QuickPrintDialog
{
    public static PrintRequest? Show(Window owner)
    {
        var kind = PrintKindDialog.Show(owner);
        if (kind == null) return null;

        var period = PrintPeriodDialog.Show(owner);
        if (period == null) return null;

        return new PrintRequest(kind, period.Value.From, period.Value.To);
    }
}

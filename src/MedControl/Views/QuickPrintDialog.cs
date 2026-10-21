using System.Windows;

namespace MedControl.Views;

// Запрос на печать: что, за период и сколько точек на графике.
public sealed record PrintRequest(string Kind, DateTime From, DateTime To, int ChartPoints);

// Печать в два шага: 1) что печатать → «Далее»; 2) за какой период → «Создать отчёт».
public static class QuickPrintDialog
{
    public static PrintRequest? Show(Window owner, int chartPoints)
    {
        var kind = PrintKindDialog.Show(owner);
        if (kind == null) return null;

        var period = PrintPeriodDialog.Show(owner, chartPoints);
        if (period == null) return null;

        return new PrintRequest(kind, period.Value.From, period.Value.To, period.Value.Points);
    }
}

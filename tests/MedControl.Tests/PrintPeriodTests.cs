using MedControl.Views;

namespace MedControl.Tests;

public sealed class PrintPeriodTests
{
    private static readonly DateTime Today = DateTime.Today;

    [Fact]
    public void Mode30_Days_Last_Month()
    {
        var (f, t) = PrintPeriodDialog.Range(0, null, null);
        Assert.Equal(Today.AddDays(-30), f);
        Assert.Equal(Today.AddDays(1), t);
    }

    [Fact]
    public void ModeDay_Is_Today()
    {
        var (f, t) = PrintPeriodDialog.Range(1, null, null);
        Assert.Equal(Today, f);
        Assert.Equal(Today.AddDays(1), t);
    }

    [Fact]
    public void ModeWeek_Is_7_Days()
    {
        var (f, t) = PrintPeriodDialog.Range(2, null, null);
        Assert.Equal(Today.AddDays(-6), f);
        Assert.Equal(Today.AddDays(1), t);
    }

    [Fact]
    public void ModeMonth_Is_30_Days()
    {
        var (f, t) = PrintPeriodDialog.Range(3, null, null);
        Assert.Equal(Today.AddDays(-29), f);
        Assert.Equal(Today.AddDays(1), t);
    }

    [Fact]
    public void ModeYear_Is_365_Days()
    {
        var (f, t) = PrintPeriodDialog.Range(4, null, null);
        Assert.Equal(Today.AddDays(-364), f);
        Assert.Equal(Today.AddDays(1), t);
    }

    [Fact]
    public void ModeCustom_Uses_Own_Dates_And_Includes_Last_Day()
    {
        var (f, t) = PrintPeriodDialog.Range(5, new DateTime(2026, 9, 1), new DateTime(2026, 9, 30));
        Assert.Equal(new DateTime(2026, 9, 1), f);
        Assert.Equal(new DateTime(2026, 10, 1), t);   // «по» включается в отчёт
    }
}

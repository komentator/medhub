using MedControl.Services;

namespace MedControl.Tests;

public class UpdateCheckTests
{
    [Theory]
    [InlineData("v1.0.1", "1.0.1")]
    [InlineData("1.0.1", "1.0.1")]
    [InlineData("V2.3", "2.3")]
    [InlineData("  v1.0.1  ", "1.0.1")]
    [InlineData("oops", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseTag(string? tag, string? expected)
    {
        var v = UpdateCheck.ParseTag(tag);
        Assert.Equal(expected, v?.ToString());
    }

    [Theory]
    [InlineData("1.0.0", "1.0.1", true)]
    [InlineData("1.0.1", "1.0.1", false)]
    [InlineData("1.0.2", "1.0.1", false)]
    [InlineData("1.0.9", "1.0.10", true)]
    public void IsNewer_Rule(string cur, string latest, bool expected)
        => Assert.Equal(expected, UpdateCheck.IsNewer(Version.Parse(cur), Version.Parse(latest)));

    [Theory]
    [InlineData("1.0.11.0", "1.0.11")]
    [InlineData("1.1.1.0", "1.1.1")]
    [InlineData("1.0.4", "1.0.4")]
    public void Short_HidesRevision(string full, string expected)
        => Assert.Equal(expected, UpdateCheck.Short(Version.Parse(full)));
}

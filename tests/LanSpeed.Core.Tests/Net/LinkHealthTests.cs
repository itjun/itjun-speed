using LanSpeed.Core.Net;

namespace LanSpeed.Core.Tests.Net;

public class LinkHealthTests
{
    [Theory]
    [InlineData(1000, LinkClass.GigabitPlus)]
    [InlineData(2500, LinkClass.GigabitPlus)]
    [InlineData(10000, LinkClass.GigabitPlus)]
    [InlineData(100, LinkClass.BelowGigabit)]
    [InlineData(999, LinkClass.BelowGigabit)]
    [InlineData(0, LinkClass.Unknown)]
    [InlineData(-1, LinkClass.Unknown)]
    public void Classify(long mbps, LinkClass expected) =>
        Assert.Equal(expected, LinkHealth.Classify(mbps));

    [Fact]
    public void ClassifyPath_TakesMin()
    {
        Assert.Equal(LinkClass.BelowGigabit, LinkHealth.ClassifyPath(1000, 100));
        Assert.Equal(LinkClass.GigabitPlus, LinkHealth.ClassifyPath(1000, 2500));
        Assert.Equal(LinkClass.Unknown, LinkHealth.ClassifyPath(0, 0));
        Assert.Equal(LinkClass.BelowGigabit, LinkHealth.ClassifyPath(0, 100));
    }

    [Fact]
    public void InvestigationHint_BelowGigabit()
    {
        string? hint = LinkHealth.InvestigationHint(100);
        Assert.NotNull(hint);
        Assert.Contains("100", hint);
        Assert.Contains("千兆", hint);
        Assert.Null(LinkHealth.InvestigationHint(1000));
    }
}

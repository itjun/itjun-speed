using LanSpeed.Core.Update;

namespace LanSpeed.Core.Tests.Update;

public class AppVersionTests
{
    [Theory]
    [InlineData("0.2.0", 0, 2, 0)]
    [InlineData("v0.2.0", 0, 2, 0)]
    [InlineData("V0.10.1", 0, 10, 1)]
    [InlineData("0.2.0.0", 0, 2, 0)]
    [InlineData("0.2.0.9", 0, 2, 0)]
    [InlineData("0.2", 0, 2, 0)]
    [InlineData("1", 1, 0, 0)]
    [InlineData(" 0.2.0-beta ", 0, 2, 0)]
    public void Parse_NumericTriple(string text, int major, int minor, int patch)
    {
        Assert.True(AppVersion.TryParse(text, out AppVersion version));
        Assert.Equal(new AppVersion(major, minor, patch), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("v")]
    [InlineData("ver0.2.0")]
    [InlineData("1.2.3.4.5")]
    [InlineData("-1.0.0")]
    [InlineData("0.2.0.x")]
    public void Parse_Rejects(string text) => Assert.False(AppVersion.TryParse(text, out _));

    [Fact]
    public void Compare_IsNumeric_NotText()
    {
        Assert.True(Parse("0.9.0") < Parse("0.10.0"));
        Assert.True(Parse("0.2.0") < Parse("0.2.1"));
        Assert.True(Parse("0.2.1") < Parse("0.3.0"));
        Assert.True(Parse("0.3.0") < Parse("1.0.0"));
        Assert.Equal(Parse("0.2.0"), Parse("0.2.0.0"));
        Assert.Equal(0, Parse("0.2.0").CompareTo(Parse("v0.2.0")));
    }

    private static AppVersion Parse(string text)
    {
        Assert.True(AppVersion.TryParse(text, out AppVersion version));
        return version;
    }
}

using LanSpeed.Core.Update;

namespace LanSpeed.Core.Tests.Update;

public class UpdatePolicyTests
{
    private const string X64 = "https://github.com/itjun/itjun-speed/releases/download/v0.3.0/LanSpeed-0.3.0-win-x64.exe";

    private const string Arm = "https://github.com/itjun/itjun-speed/releases/download/v0.3.0/LanSpeed-0.3.0-win-arm64.exe";

    [Theory]
    [InlineData("0.2.0", "0.2.0", "0.1.0", UpdateRequirement.None)]
    [InlineData("0.2.0", "0.2.0.0", "0.2.0", UpdateRequirement.None)]
    [InlineData("0.3.0", "0.2.0", "0.1.0", UpdateRequirement.None)]
    [InlineData("0.2.0", "0.3.0", "0.2.0", UpdateRequirement.Optional)]
    [InlineData("0.2.0", "0.3.0", "0.1.0", UpdateRequirement.Optional)]
    [InlineData("0.10.0", "0.10.1", "0.10.0", UpdateRequirement.Optional)]
    [InlineData("0.9.0", "0.10.0", "0.10.0", UpdateRequirement.Required)]
    [InlineData("0.1.0", "0.3.0", "0.2.0", UpdateRequirement.Required)]
    [InlineData("0.1.9", "0.3.0", "0.2.0", UpdateRequirement.Required)]
    [InlineData("0.2.0", "0.2.0", "0.3.0", UpdateRequirement.Required)]
    public void Decide_BelowMinimumIsRequired_OtherwiseOptionalWhenNewer(string current, string latest, string minimum, UpdateRequirement expected) =>
        Assert.Equal(expected, UpdatePolicy.Decide(V(current), V(latest), V(minimum)));

    [Fact]
    public void Decide_MissingMinimum_DoesNotForce() =>
        Assert.Equal(UpdateRequirement.Optional, UpdatePolicy.Decide(V("0.1.0"), V("0.2.0"), null));

    [Fact]
    public void Evaluate_PicksAmd64_AndIgnoresArm()
    {
        UpdateOffer offer = UpdatePolicy.Evaluate(V("0.2.0"), Release(V("0.3.0"), V("0.2.0")), manifest: null);
        Assert.Equal(UpdateRequirement.Optional, offer.Requirement);
        Assert.Equal(X64, offer.DownloadUrl);
        Assert.Equal("LanSpeed-0.3.0-win-x64.exe", offer.FileName);
    }

    [Fact]
    public void Evaluate_RequiredWhenBelowMinimum_EvenIfArmIsTheOnlyOtherAsset()
    {
        UpdateOffer offer = UpdatePolicy.Evaluate(V("0.1.0"), Release(V("0.3.0"), V("0.2.0")), manifest: null);
        Assert.Equal(UpdateRequirement.Required, offer.Requirement);
        Assert.Equal(X64, offer.DownloadUrl);
    }

    [Fact]
    public void Evaluate_ArmOnly_HasNoDownload()
    {
        var release = new ParsedRelease(
            V("0.3.0"),
            V("0.1.0"),
            [new ReleaseAsset("LanSpeed-0.3.0-win-arm64.exe", Arm)],
            null);
        UpdateOffer offer = UpdatePolicy.Evaluate(V("0.2.0"), release, manifest: null);
        Assert.Equal(UpdateRequirement.Optional, offer.Requirement);
        Assert.Null(offer.DownloadUrl);
    }

    [Fact]
    public void Evaluate_ManifestMinimumOverridesBody_AndNamesTheFile()
    {
        var manifest = new UpdateManifest(V("0.3.0"), V("0.3.0"), "LanSpeed-0.3.0-win-x64.exe");
        UpdateOffer offer = UpdatePolicy.Evaluate(V("0.2.0"), Release(V("0.3.0"), V("0.1.0")), manifest);
        Assert.Equal(UpdateRequirement.Required, offer.Requirement);
        Assert.Equal(V("0.3.0"), offer.Minimum);
        Assert.Equal(X64, offer.DownloadUrl);
    }

    [Fact]
    public void Evaluate_DoesNotInstallWhenLatestIsNotNewer()
    {
        var release = new ParsedRelease(V("0.2.0"), V("0.3.0"), [new ReleaseAsset("LanSpeed-0.2.0-win-x64.exe", X64.Replace("0.3.0", "0.2.0", StringComparison.Ordinal))], null);
        UpdateOffer offer = UpdatePolicy.Evaluate(V("0.2.0"), release, manifest: null);
        Assert.Equal(UpdateRequirement.Required, offer.Requirement);
        Assert.Null(offer.DownloadUrl);
    }

    [Fact]
    public void Evaluate_RejectsManifestVersionMismatch()
    {
        var manifest = new UpdateManifest(V("0.9.0"), V("0.1.0"), "LanSpeed-0.3.0-win-x64.exe");
        Assert.Throws<InvalidOperationException>(() => UpdatePolicy.Evaluate(V("0.2.0"), Release(V("0.3.0"), V("0.1.0")), manifest));
    }

    private static ParsedRelease Release(AppVersion latest, AppVersion minimum) => new(
        latest,
        minimum,
        [
            new ReleaseAsset("LanSpeed-0.3.0-win-arm64.exe", Arm),
            new ReleaseAsset("LanSpeed-0.3.0-win-x64.exe", X64),
            new ReleaseAsset("update.json", "https://github.com/itjun/itjun-speed/releases/download/v0.3.0/update.json"),
        ],
        "说明");

    private static AppVersion V(string text)
    {
        Assert.True(AppVersion.TryParse(text, out AppVersion version));
        return version;
    }
}

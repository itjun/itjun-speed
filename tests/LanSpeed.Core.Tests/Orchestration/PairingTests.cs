using LanSpeed.Core.Iperf;
using LanSpeed.Core.Orchestration;

namespace LanSpeed.Core.Tests.Orchestration;

public class PairingTests
{
    private static readonly string[] Hosts = ["h1", "h2", "h3"];

    [Fact]
    public void Star_TwoRoundsBidir()
    {
        var p = new Params();
        var pairs = Pairing.Build(GroupMode.Star, Hosts, "h1", p);

        Assert.Equal(2, pairs.Count);
        Assert.All(pairs, x => Assert.Equal("h1", x.A));
        Assert.Equal(Directions.Bidir, p.Direction);
    }

    [Fact]
    public void Mesh_SixRoundsForward()
    {
        var p = new Params();
        var pairs = Pairing.Build(GroupMode.Mesh, Hosts, null, p);

        Assert.Equal(6, pairs.Count);
        Assert.Equal(Directions.Forward, p.Direction);
        Assert.Equal(6, pairs.Select(x => (x.A, x.B)).Distinct().Count());
    }

    [Fact]
    public void Star_CenterNotInGroup_Throws()
    {
        var p = new Params();
        Assert.Throws<ArgumentException>(() => Pairing.Build(GroupMode.Star, Hosts, "h9", p));
    }

    [Fact]
    public void TooFewHosts_Throws()
    {
        var p = new Params();
        Assert.Throws<ArgumentException>(() => Pairing.Build(GroupMode.Mesh, new[] { "h1" }, null, p));
    }

    [Fact]
    public void DuplicateHosts_Deduped()
    {
        var p = new Params();
        var pairs = Pairing.Build(GroupMode.Mesh, new[] { "h1", "h1", "h2" }, null, p);
        Assert.Equal(2, pairs.Count);
    }
}

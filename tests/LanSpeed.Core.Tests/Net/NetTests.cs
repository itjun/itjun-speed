using System.Net;
using LanSpeed.Core.Net;

namespace LanSpeed.Core.Tests.Net;

public class SubnetTests
{
    [Fact]
    public void Same24_SameSubnet()
    {
        Assert.True(Subnet.SameSubnet(IPAddress.Parse("192.168.1.10"), 24, IPAddress.Parse("192.168.1.200"), 24));
    }

    [Fact]
    public void Different24_DifferentSubnet()
    {
        Assert.False(Subnet.SameSubnet(IPAddress.Parse("192.168.1.10"), 24, IPAddress.Parse("192.168.2.1"), 24));
    }

    [Fact]
    public void ShorterPrefixWins()
    {
        // 按 /8 判定为同网段
        Assert.True(Subnet.SameSubnet(IPAddress.Parse("10.0.0.5"), 8, IPAddress.Parse("10.1.2.3"), 16));
    }

    [Fact]
    public void ZeroOrFullPrefix_NotSame()
    {
        Assert.False(Subnet.SameSubnet(IPAddress.Parse("10.0.0.5"), 0, IPAddress.Parse("10.0.0.6"), 24));
        Assert.False(Subnet.SameSubnet(IPAddress.Parse("10.0.0.5"), 24, IPAddress.Parse("10.0.0.6"), 32));
    }
}

public class AddrClassifyTests
{
    [Theory]
    [InlineData("10.1.2.3", AddrKind.Lan)]
    [InlineData("172.16.0.1", AddrKind.Lan)]
    [InlineData("172.31.255.255", AddrKind.Lan)]
    [InlineData("192.168.1.1", AddrKind.Lan)]
    [InlineData("100.64.0.1", AddrKind.Overlay)]
    [InlineData("100.127.255.255", AddrKind.Overlay)]
    [InlineData("8.8.8.8", AddrKind.Wan)]
    [InlineData("172.32.0.1", AddrKind.Wan)]
    public void Classify(string ip, AddrKind want)
    {
        Assert.Equal(want, AddrClassify.Classify(IPAddress.Parse(ip)));
    }
}

public class PathSelectTests
{
    [Fact]
    public void SelectLanPath_PicksSameSubnetPair()
    {
        var a = new[]
        {
            new NetAddr("192.168.1.10", 24, "以太网", 1000),
            new NetAddr("10.0.0.5", 8, "以太网 2", 10000),
        };
        var b = new[]
        {
            new NetAddr("10.1.2.3", 16, "以太网", 1000),
        };

        var path = PathSelect.SelectLanPath(a, b);

        Assert.NotNull(path);
        Assert.Equal("10.1.2.3", path.TargetIp);
        Assert.Equal("10.0.0.5", path.BindIp);
        Assert.Equal(1000, path.LinkMbps);
    }

    [Fact]
    public void SelectLanPath_NoCommonSubnet_ReturnsNull()
    {
        var a = new[] { new NetAddr("192.168.1.10", 24) };
        var b = new[] { new NetAddr("10.1.2.3", 24) };
        Assert.Null(PathSelect.SelectLanPath(a, b));
    }

    [Fact]
    public void ForTarget_LoopbackBindsLoopback()
    {
        var path = PathSelect.ForTarget("127.0.0.1", new[] { new NetAddr("192.168.1.10", 24) });
        Assert.NotNull(path);
        Assert.Equal("127.0.0.1", path.BindIp);
    }
}

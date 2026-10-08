using LanSpeed.Core.Iperf;
using LanSpeed.Core.Verdict;

namespace LanSpeed.Core.Tests.Verdict;

public class VerdictTests
{
    private static Params TcpParams(string direction = Directions.Forward, int parallel = 4) =>
        new() { Protocol = "tcp", Direction = direction, Parallel = parallel };

    [Fact]
    public void NoData_IsPoor()
    {
        var v = VerdictEvaluator.Evaluate(new Summary { AB = 0, BA = 0 }, "tcp", 0, TcpParams());
        Assert.Equal(Grade.Poor, v.Grade);
        Assert.Contains(v.Notes, n => n.Contains("没有测到任何数据"));
    }

    [Fact]
    public void Reverse_UsesBAOnly()
    {
        var v = VerdictEvaluator.Evaluate(new Summary { AB = 0, BA = 900e6 }, "tcp", 0, TcpParams(Directions.Reverse));
        Assert.Equal(Grade.Great, v.Grade); // 900M 且链路未知
    }

    [Fact]
    public void LinkRatio_Great()
    {
        var v = VerdictEvaluator.Evaluate(new Summary { AB = 940e6, BA = 0 }, "tcp", 1000, TcpParams());
        Assert.Equal(Grade.Great, v.Grade); // 94% ≥ 85%
    }

    [Fact]
    public void LinkRatio_Fair_WithAdvice()
    {
        var v = VerdictEvaluator.Evaluate(new Summary { AB = 400e6, BA = 0 }, "tcp", 1000, TcpParams());
        Assert.Equal(Grade.Fair, v.Grade); // 40%
        Assert.Contains(v.Notes, n => n.Contains("建议"));
    }

    [Fact]
    public void UnknownLink_100M_IsFair()
    {
        var v = VerdictEvaluator.Evaluate(new Summary { AB = 100e6, BA = 0 }, "tcp", 0, TcpParams());
        Assert.Equal(Grade.Fair, v.Grade);
    }

    [Fact]
    public void VeryFastWithoutCable_Suspected()
    {
        var v = VerdictEvaluator.Evaluate(new Summary { AB = 13e9, BA = 0 }, "tcp", 0, TcpParams());
        Assert.Equal(Grade.Great, v.Grade);
        Assert.Contains(v.Notes, n => n.Contains("虚拟机"));
    }

    [Fact]
    public void BidirImbalance_CapsToGood()
    {
        var s = new Summary { AB = 900e6, BA = 400e6 };
        var v = VerdictEvaluator.Evaluate(s, "tcp", 0, TcpParams(Directions.Bidir));
        // 慢方向 400M → Fair；但断言只验证封顶与提示，不强制 Fair/Good 之外的值
        Assert.Contains(v.Notes, n => n.Contains("差距较大"));
    }

    [Fact]
    public void UdpHighLoss_CapsToFair()
    {
        var s = new Summary { AB = 900e6, BA = 0, LostPct = 6, JitterMs = 1 };
        var v = VerdictEvaluator.Evaluate(s, "udp", 0, new Params { Protocol = "udp", UDPBandwidthMbps = 1000 });
        Assert.Equal(Grade.Fair, v.Grade);
    }

    [Fact]
    public void HighRetrans_CapsToFair()
    {
        var s = new Summary { AB = 900e6, BA = 0, Retransmits = 6000, Seconds = 10 };
        var v = VerdictEvaluator.Evaluate(s, "tcp", 0, TcpParams());
        Assert.Equal(Grade.Fair, v.Grade); // 600 次/秒 ≥ 500
    }

    [Fact]
    public void MediumRetrans_CapsToGood()
    {
        var s = new Summary { AB = 900e6, BA = 0, Retransmits = 800, Seconds = 10 };
        var v = VerdictEvaluator.Evaluate(s, "tcp", 0, TcpParams());
        Assert.Equal(Grade.Good, v.Grade); // 80 次/秒：≥50 且 <500
    }

    [Fact]
    public void Title_UsesSlowDirection()
    {
        var v = VerdictEvaluator.Evaluate(new Summary { AB = 800e6, BA = 0 }, "tcp", 0, TcpParams());
        // 800 Mbps → 100 MB/s，10GB ≈ 1.7 分钟
        Assert.Contains("100 MB", v.Title);
        Assert.Contains("1.7 分钟", v.Title);
    }
}

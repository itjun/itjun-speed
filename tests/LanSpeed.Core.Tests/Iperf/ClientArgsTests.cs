using LanSpeed.Core.Iperf;

namespace LanSpeed.Core.Tests.Iperf;

public class ClientArgsTests
{
    [Fact]
    public void UdpReverse_SplitsBandwidthPerStream()
    {
        var p = new Params
        {
            Protocol = "udp",
            Parallel = 4,
            UDPBandwidthMbps = 1000,
            Direction = Directions.Reverse,
        };
        p.Normalize();

        var args = ClientArgs.Build("10.0.0.2", 5201, p, Flow.New("b", "reverse"));

        AssertSeq(args, "-c", "10.0.0.2");
        AssertSeq(args, "-p", "5201");
        AssertSeq(args, "-P", "4");
        AssertSeq(args, "-u", "-b", "250000000");
        AssertSeq(args, "-R");
        Assert.Contains("--json-stream", args);
    }

    [Fact]
    public void TcpForward_Defaults()
    {
        var p = new Params();
        p.Normalize();

        var args = ClientArgs.Build("192.168.1.23", 5202, p, Flow.New("b", "forward"));

        AssertSeq(args, "-c", "192.168.1.23");
        AssertSeq(args, "-t", "10");
        AssertSeq(args, "-P", "4");
        AssertSeq(args, "-i", "1");
        AssertSeq(args, "--connect-timeout", "3000");
        AssertSeq(args, "--json-stream", "--forceflush");
        Assert.DoesNotContain("-R", args);
        Assert.DoesNotContain("--bidir", args);
    }

    [Fact]
    public void Bidir_WinsOverReverse()
    {
        var p = new Params { Direction = Directions.Bidir };
        p.Normalize();
        var args = ClientArgs.Build("10.0.0.2", 5201, p, Flow.New("b", "bidir"));
        Assert.Contains("--bidir", args);
        Assert.DoesNotContain("-R", args);
    }

    [Fact]
    public void BindIp_AppendsMinusB()
    {
        var p = new Params();
        p.Normalize();
        var args = ClientArgs.Build("192.168.1.23", 5201, p, Flow.New("b", "forward"), bindIp: "192.168.1.10");
        AssertSeq(args, "-B", "192.168.1.10");
    }

    [Fact]
    public void HalfSecondInterval_Kept()
    {
        var p = new Params { Interval = 0.5 };
        p.Normalize();
        var args = ClientArgs.Build("10.0.0.2", 5201, p, Flow.New("b", "forward"));
        AssertSeq(args, "-i", "0.5");
    }

    private static void AssertSeq(IReadOnlyList<string> args, params string[] seq)
    {
        for (int i = 0; i + seq.Length <= args.Count; i++)
        {
            if (Enumerable.Range(0, seq.Length).All(j => args[i + j] == seq[j]))
            {
                return;
            }
        }
        Assert.Fail($"序列 [{string.Join(" ", seq)}] 未出现在 [{string.Join(" ", args)}]");
    }
}

public class ParamsTests
{
    [Fact]
    public void Normalize_ClampsAndDefaults()
    {
        var p = new Params { Parallel = 0, Duration = 1, Port = 80, Interval = 0.3, Protocol = " UDP " };
        p.Normalize();
        Assert.Equal(4, p.Parallel);
        Assert.Equal(3, p.Duration);
        Assert.Equal(0, p.Port);
        Assert.Equal(1, p.Interval);
        Assert.Equal("udp", p.Protocol);

        var q = new Params { Parallel = 100, Duration = 999, Omit = 11, MSS = 9001 };
        q.Normalize();
        Assert.Equal(64, q.Parallel);
        Assert.Equal(300, q.Duration);
        Assert.Equal(0, q.Omit);
        Assert.Equal(0, q.MSS);
    }

    [Fact]
    public void Normalize_PortInRange_Kept()
    {
        var p = new Params { Port = 5201 };
        p.Normalize();
        Assert.Equal(5201, p.Port);
    }
}

using LanSpeed.Core.Iperf;

namespace LanSpeed.Core.Tests.Iperf;

public class StreamParserTests
{
    private static (StreamParser Parser, List<Sample> Samples) ParseFile(string file, Flow flow)
    {
        var parser = new StreamParser(flow, trustRtt: true);
        var samples = new List<Sample>();
        foreach (var line in TestFiles.ReadLines(file))
        {
            if (parser.Feed(line) is { } s)
            {
                samples.Add(s);
            }
        }
        return (parser, samples);
    }

    [Fact]
    public void TcpForward_LandsOnAB()
    {
        var (parser, samples) = ParseFile("tcp_p2.jsonl", Flow.New("b", "forward"));

        Assert.Equal(2, samples.Count);
        var first = samples[0];
        Assert.True(first.AB >= 4e8, $"AB={first.AB}");
        Assert.Equal(0, first.BA);
        Assert.Equal(2, first.StreamsAB.Count);
        Assert.True(first.RTTMs > 0);

        var sum = parser.BuildSummary();
        Assert.True(sum.AB >= 4e8, $"AB={sum.AB}");
        Assert.Equal(0, sum.BA);
        Assert.Equal(1, sum.Retransmits);
        Assert.True(sum.RTTMs > 0);
    }

    [Fact]
    public void Reverse_LandsOnBA()
    {
        var flow = Flow.New("b", "reverse");
        Assert.True(flow.Reverse);

        var (parser, samples) = ParseFile("tcp_reverse.jsonl", flow);

        var sum = parser.BuildSummary();
        Assert.True(sum.BA > 0, $"BA={sum.BA}");
        Assert.Equal(0, sum.AB);
        Assert.Equal(2, samples[0].StreamsBA.Count);
    }

    [Fact]
    public void ClientOnB_FlipsDirection()
    {
        var flow = Flow.New("a", "forward");
        Assert.True(flow.Reverse);
        Assert.False(flow.ClientIsA);

        var (parser, _) = ParseFile("tcp_reverse.jsonl", flow);

        var sum = parser.BuildSummary();
        Assert.True(sum.AB > 0, $"AB={sum.AB}");
        Assert.Equal(0, sum.BA);
    }

    [Fact]
    public void Bidir_BothDirections()
    {
        var (parser, samples) = ParseFile("tcp_bidir.jsonl", Flow.New("b", "bidir"));

        var sum = parser.BuildSummary();
        Assert.True(sum.AB > 0);
        Assert.True(sum.BA > 0);
        Assert.Single(samples[0].StreamsAB);
        Assert.Single(samples[0].StreamsBA);
    }

    [Fact]
    public void UdpBidir_JitterAndBothDirections()
    {
        var (parser, samples) = ParseFile("udp_bidir.jsonl", Flow.New("b", "bidir"));

        Assert.True(samples[0].JitterMs > 0);
        var sum = parser.BuildSummary();
        Assert.True(sum.AB >= 1.9e8, $"AB={sum.AB}");
        Assert.True(sum.BA >= 1.9e8, $"BA={sum.BA}");
        Assert.True(sum.JitterMs > 0);
    }

    [Fact]
    public void ErrorEvent_KeepsMessage()
    {
        var parser = new StreamParser(Flow.New("b", "forward"), trustRtt: true);
        parser.Feed("""{"event":"error","data":"unable to connect to server: Connection refused"}""");
        Assert.NotNull(parser.ErrorMessage);
        Assert.Contains("refused", parser.ErrorMessage);
    }

    [Theory]
    // (serverSide, direction, 期望 AB, 期望 BA)：用户语义的 A→B 数据始终落在 AB、B→A 落在 BA。
    // 合成 interval：sum=1e8（客户端→服务端方向），bidir 时另有 sum_bidir_reverse=5e7（服务端→客户端）。
    [InlineData("b", "forward", 1e8, 0.0)]
    [InlineData("b", "reverse", 0.0, 1e8)]
    [InlineData("b", "bidir", 1e8, 5e7)]
    [InlineData("a", "forward", 1e8, 0.0)]
    [InlineData("a", "reverse", 0.0, 1e8)]
    [InlineData("a", "bidir", 5e7, 1e8)]
    public void DirectionMatrix_UserSemanticsPreserved(string serverSide, string direction, double wantAB, double wantBA)
    {
        var flow = Flow.New(serverSide, direction);
        // 合成 interval：sum=1e8（客户端→服务端方向），bidir 时另有 sum_bidir_reverse=5e7（服务端→客户端）
        const string sum = "{\"end\":1.0,\"seconds\":1.0,\"bits_per_second\":100000000,\"retransmits\":0,\"sender\":true}";
        string bidirSum = direction == "bidir"
            ? ",\"sum_bidir_reverse\":{\"end\":1.0,\"seconds\":1.0,\"bits_per_second\":50000000,\"retransmits\":0,\"sender\":false}"
            : string.Empty;
        string line = "{\"event\":\"interval\",\"data\":{\"streams\":[" + sum + "],\"sum\":" + sum + bidirSum + "}}";

        var parser = new StreamParser(flow, trustRtt: false);
        var sample = parser.Feed(line);

        Assert.NotNull(sample);
        // 客户端在发（sender=true）：c→s 视角为 A→B 当且仅当客户端是 A
        if (flow.C2sIsAB)
        {
            Assert.Single(sample.StreamsAB);
        }
        else
        {
            Assert.Single(sample.StreamsBA);
        }
        Assert.Equal(wantAB, sample.AB, 1);
        Assert.Equal(wantBA, sample.BA, 1);
    }
}

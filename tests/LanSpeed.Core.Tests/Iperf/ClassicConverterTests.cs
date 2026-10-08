using LanSpeed.Core.Iperf;

namespace LanSpeed.Core.Tests.Iperf;

public class ClassicConverterTests
{
    [Fact]
    public void ClassicJson_BecomesStreamLines()
    {
        // 旧版 iperf3 -J 输出（Ubuntu 24.04 iperf3 3.16 形态）：单文档 start/intervals/end
        const string classic =
            """
            {"start":{"connected":[{}]},"intervals":[
              {"streams":[{"end":1.0,"seconds":1.0,"bits_per_second":5e8,"sender":true}],"sum":{"end":1.0,"seconds":1.0,"bits_per_second":5e8,"retransmits":2,"sender":true}},
              {"streams":[{"end":2.0,"seconds":1.0,"bits_per_second":6e8,"sender":true}],"sum":{"end":2.0,"seconds":1.0,"bits_per_second":6e8,"retransmits":0,"sender":true}}
            ],"end":{"streams":[],"sum_sent":{"end":2.0,"seconds":2.0,"bits_per_second":5.5e8,"retransmits":2,"sender":true},"cpu_utilization_percent":{"host_total":3.5,"remote_total":2.1}}}
            """;

        var lines = ClassicConverter.ToJsonStreamLines(classic).ToList();

        Assert.Equal(3, lines.Count); // 2×interval + 1×end
        var parser = new StreamParser(Flow.New("b", "forward"), trustRtt: false);
        foreach (var line in lines)
        {
            parser.Feed(line);
        }
        Assert.Equal(2, parser.Samples.Count);
        Assert.True(parser.Samples[0].AB >= 5e8);
        var summary = parser.BuildSummary();
        Assert.True(summary.AB >= 5.5e8); // end 事件优先
        Assert.Equal(2, summary.Retransmits);
        Assert.Equal(3.5, summary.CPUClient, 1);
    }

    [Fact]
    public void ClassicError_BecomesErrorEvent()
    {
        const string classic = """{"error":"unable to connect to server: Connection refused"}""";
        var parser = new StreamParser(Flow.New("b", "forward"), trustRtt: false);
        foreach (var line in ClassicConverter.ToJsonStreamLines(classic))
        {
            parser.Feed(line);
        }
        Assert.NotNull(parser.ErrorMessage);
        Assert.Contains("refused", parser.ErrorMessage);
    }

    [Fact]
    public void GarbageInput_ProducesNothing()
    {
        Assert.Empty(ClassicConverter.ToJsonStreamLines("not json at all"));
    }
}

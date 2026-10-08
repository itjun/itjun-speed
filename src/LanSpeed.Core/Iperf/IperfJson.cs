using System.Text.Json.Serialization;

namespace LanSpeed.Core.Iperf;

// iperf3 --json-stream 输出的内部 JSON 结构（附录 A.4），仅解析需要的字段。
// 字段为可空引用类型的，表示 iperf3 在对应场景下不输出该键。

internal class IvSum
{
    [JsonPropertyName("end")]
    public double End { get; set; }

    [JsonPropertyName("seconds")]
    public double Seconds { get; set; }

    [JsonPropertyName("bits_per_second")]
    public double BitsPerSecond { get; set; }

    [JsonPropertyName("retransmits")]
    public int Retransmits { get; set; }

    [JsonPropertyName("jitter_ms")] // 只有 UDP 接收端有
    public double? JitterMs { get; set; }

    [JsonPropertyName("lost_percent")]
    public double LostPercent { get; set; }

    [JsonPropertyName("omitted")]
    public bool Omitted { get; set; }

    [JsonPropertyName("sender")]
    public bool Sender { get; set; }
}

internal sealed class IvStream : IvSum
{
    [JsonPropertyName("rtt")]
    public double Rtt { get; set; } // 微秒
}

internal sealed class IntervalData
{
    [JsonPropertyName("streams")]
    public List<IvStream>? Streams { get; set; }

    [JsonPropertyName("sum")]
    public IvSum? Sum { get; set; }

    [JsonPropertyName("sum_bidir_reverse")]
    public IvSum? SumBidirReverse { get; set; }
}

internal sealed class EndStreamSide
{
    [JsonPropertyName("mean_rtt")]
    public double MeanRtt { get; set; } // 微秒
}

internal sealed class EndStream
{
    [JsonPropertyName("sender")]
    public EndStreamSide? Sender { get; set; }

    [JsonPropertyName("receiver")]
    public EndStreamSide? Receiver { get; set; }
}

internal sealed class CpuUtilization
{
    [JsonPropertyName("host_total")]
    public double HostTotal { get; set; }

    [JsonPropertyName("remote_total")]
    public double RemoteTotal { get; set; }
}

internal sealed class EndData
{
    [JsonPropertyName("streams")]
    public List<EndStream>? Streams { get; set; }

    [JsonPropertyName("sum")]
    public IvSum? Sum { get; set; }

    [JsonPropertyName("sum_sent")]
    public IvSum? SumSent { get; set; }

    [JsonPropertyName("sum_received")]
    public IvSum? SumReceived { get; set; }

    [JsonPropertyName("sum_bidir_reverse")]
    public IvSum? SumBidirReverse { get; set; }

    [JsonPropertyName("sum_sent_bidir_reverse")]
    public IvSum? SumSentBidirReverse { get; set; }

    [JsonPropertyName("sum_received_bidir_reverse")]
    public IvSum? SumReceivedBidirReverse { get; set; }

    [JsonPropertyName("cpu_utilization_percent")]
    public CpuUtilization? Cpu { get; set; }
}

internal sealed class ProbeDoc
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("end")]
    public EndData? End { get; set; }
}

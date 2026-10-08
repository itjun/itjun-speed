using System.Text.Json;

namespace LanSpeed.Core.Iperf;

/// <summary>一个采样点，吞吐单位 bit/s，方向已落到用户视角的 A→B / B→A。</summary>
public sealed class Sample
{
    public double T { get; set; }
    public double AB { get; set; }
    public double BA { get; set; }
    public int Retransmits { get; set; }
    public double RTTMs { get; set; }
    public double JitterMs { get; set; }
    public double LostPct { get; set; }
    public bool Omitted { get; set; }
    public List<double> StreamsAB { get; } = [];
    public List<double> StreamsBA { get; } = [];
}

/// <summary>单对测速汇总（附录 A.4 / A.5）。</summary>
public sealed class Summary
{
    public double AB { get; set; }
    public double BA { get; set; }
    public double PeakAB { get; set; }
    public double PeakBA { get; set; }
    public int Retransmits { get; set; }
    public double RTTMs { get; set; }
    public double JitterMs { get; set; }
    public double LostPct { get; set; }
    public double Seconds { get; set; }
    public double CPUClient { get; set; }
    public double CPUServer { get; set; }
}

/// <summary>
/// 逐行解析 iperf3 --json-stream 输出（附录 A.4 / A.5）。
/// 每行形如 {"event":"start|interval|end|error","data":...}；不以 { 开头的行忽略。
/// </summary>
public sealed class StreamParser
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly Flow _flow;
    private readonly bool _trustRtt;
    private readonly List<Sample> _samples = [];
    private EndData? _end;
    private string? _errMsg;

    public StreamParser(Flow flow, bool trustRtt)
    {
        _flow = flow;
        _trustRtt = trustRtt;
    }

    public string? ErrorMessage => _errMsg;

    public IReadOnlyList<Sample> Samples => _samples;

    /// <summary>喂入一行输出；interval 事件返回新采样点，其余返回 null。</summary>
    public Sample? Feed(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith('{'))
        {
            return null;
        }
        using var doc = JsonDocument.Parse(trimmed);
        var root = doc.RootElement;
        if (!root.TryGetProperty("event", out var eventEl))
        {
            return null;
        }
        switch (eventEl.GetString())
        {
            case "interval":
                if (!root.TryGetProperty("data", out var dataEl))
                {
                    return null;
                }
                var iv = dataEl.Deserialize<IntervalData>(JsonOpts);
                if (iv?.Sum == null)
                {
                    return null;
                }
                var sample = SampleFrom(iv);
                _samples.Add(sample);
                return sample;
            case "end":
                if (root.TryGetProperty("data", out var endEl))
                {
                    _end = endEl.Deserialize<EndData>(JsonOpts);
                }
                return null;
            case "error":
                _errMsg = root.TryGetProperty("data", out var errEl) && errEl.ValueKind == JsonValueKind.String
                    ? errEl.GetString()
                    : null;
                return null;
            default:
                return null;
        }
    }

    // 把客户端视角的 c→s / s→c 吞吐落到 A→B / B→A
    private void Assign(bool c2s, double v, Sample s)
    {
        if (c2s == _flow.C2sIsAB)
        {
            s.AB += v;
        }
        else
        {
            s.BA += v;
        }
    }

    private Sample SampleFrom(IntervalData iv)
    {
        var sum = iv.Sum!;
        var s = new Sample { T = sum.End, Omitted = sum.Omitted };
        bool mainC2S = !_flow.Reverse;
        Assign(mainC2S, sum.BitsPerSecond, s);
        s.Retransmits += sum.Retransmits;
        TakeUdp(sum, s);
        if (iv.SumBidirReverse != null)
        {
            Assign(false, iv.SumBidirReverse.BitsPerSecond, s);
            s.Retransmits += iv.SumBidirReverse.Retransmits;
            TakeUdp(iv.SumBidirReverse, s);
        }
        double rttSum = 0;
        int rttN = 0;
        if (iv.Streams != null)
        {
            foreach (var st in iv.Streams)
            {
                // 流的 sender 是客户端视角：true 即客户端在发（c→s）
                if (st.Sender == _flow.C2sIsAB)
                {
                    s.StreamsAB.Add(st.BitsPerSecond);
                }
                else
                {
                    s.StreamsBA.Add(st.BitsPerSecond);
                }
                if (_trustRtt && st.Sender && st.Rtt > 0)
                {
                    rttSum += st.Rtt;
                    rttN++;
                }
            }
        }
        if (rttN > 0)
        {
            s.RTTMs = rttSum / rttN / 1000.0;
        }
        return s;
    }

    // 接收端的 sum 才带抖动与丢包，取两方向中较差的
    private static void TakeUdp(IvSum sum, Sample s)
    {
        if (sum.JitterMs is not { } jitter)
        {
            return;
        }
        if (jitter > s.JitterMs)
        {
            s.JitterMs = jitter;
        }
        if (sum.LostPercent > s.LostPct)
        {
            s.LostPct = sum.LostPercent;
        }
    }

    /// <summary>汇总：优先用 end 事件（接收端统计），中途停止没有 end 时按采样平均。</summary>
    public Summary BuildSummary()
    {
        var output = new Summary();
        int nAB = 0, nBA = 0;
        double sumAB = 0, sumBA = 0;
        double rttSum = 0;
        int rttN = 0;
        foreach (var s in _samples)
        {
            if (s.Omitted)
            {
                continue;
            }
            output.PeakAB = Math.Max(output.PeakAB, s.AB);
            output.PeakBA = Math.Max(output.PeakBA, s.BA);
            if (s.AB > 0)
            {
                sumAB += s.AB;
                nAB++;
            }
            if (s.BA > 0)
            {
                sumBA += s.BA;
                nBA++;
            }
            output.Retransmits += s.Retransmits;
            output.JitterMs = Math.Max(output.JitterMs, s.JitterMs);
            output.LostPct = Math.Max(output.LostPct, s.LostPct);
            output.Seconds = s.T;
            if (s.RTTMs > 0)
            {
                rttSum += s.RTTMs;
                rttN++;
            }
        }
        if (nAB > 0)
        {
            output.AB = sumAB / nAB;
        }
        if (nBA > 0)
        {
            output.BA = sumBA / nBA;
        }
        if (rttN > 0)
        {
            output.RTTMs = rttSum / rttN;
        }

        var e = _end;
        if (e == null)
        {
            return output;
        }
        var main = FirstSum(e.SumReceived, e.Sum, e.SumSent);
        if (main != null)
        {
            double ab = output.AB, ba = output.BA;
            var tmp = new Sample();
            Assign(!_flow.Reverse, main.BitsPerSecond, tmp);
            if (_flow.Bidir)
            {
                var rev = FirstSum(e.SumReceivedBidirReverse, e.SumBidirReverse);
                if (rev != null)
                {
                    Assign(false, rev.BitsPerSecond, tmp);
                }
            }
            output.AB = tmp.AB;
            output.BA = tmp.BA;
            if (output.AB == 0)
            {
                output.AB = ab;
            }
            if (output.BA == 0)
            {
                output.BA = ba;
            }
            output.Seconds = main.Seconds;
        }
        output.Retransmits = 0;
        if (e.SumSent != null)
        {
            output.Retransmits += e.SumSent.Retransmits;
        }
        if (e.SumSentBidirReverse != null)
        {
            output.Retransmits += e.SumSentBidirReverse.Retransmits;
        }
        var udp = new Sample();
        foreach (var s in new[] { e.Sum, e.SumReceived, e.SumBidirReverse, e.SumReceivedBidirReverse })
        {
            if (s != null)
            {
                TakeUdp(s, udp);
            }
        }
        if (udp.JitterMs > 0 || udp.LostPct > 0)
        {
            output.JitterMs = udp.JitterMs;
            output.LostPct = udp.LostPct;
        }
        if (_trustRtt && e.Streams != null)
        {
            double endRtt = 0;
            int n = 0;
            foreach (var st in e.Streams)
            {
                double rtt = st.Sender?.MeanRtt ?? 0;
                if (rtt > 0)
                {
                    endRtt += rtt;
                    n++;
                }
            }
            if (n > 0)
            {
                output.RTTMs = endRtt / n / 1000.0;
            }
        }
        output.CPUClient = e.Cpu?.HostTotal ?? 0;
        output.CPUServer = e.Cpu?.RemoteTotal ?? 0;
        return output;
    }

    // 第一个非空且 bps>0 的
    private static IvSum? FirstSum(params IvSum?[] sums)
    {
        foreach (var s in sums)
        {
            if (s != null && s.BitsPerSecond > 0)
            {
                return s;
            }
        }
        return null;
    }
}

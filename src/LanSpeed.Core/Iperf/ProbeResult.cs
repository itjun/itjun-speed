using System.Text.Json;

namespace LanSpeed.Core.Iperf;

/// <summary>连通探测结果（附录 A.6）。busy 视为 ok：服务端忙说明 TCP 已连通。</summary>
public sealed record ProbeResult(bool Ok, double RttMs, string Reason)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>解析 iperf3 -J 探测输出（整体输出，非 json-stream）。</summary>
    public static ProbeResult Parse(string output, int port)
    {
        output ??= string.Empty;
        int brace = output.IndexOf('{');
        string text = brace >= 0 ? output[brace..] : string.Empty;

        ProbeDoc? doc = null;
        bool parsed = false;
        if (text.Length > 0)
        {
            try
            {
                doc = JsonSerializer.Deserialize<ProbeDoc>(text, JsonOpts);
                parsed = true;
            }
            catch (JsonException)
            {
                // 解析失败走下面的超时/首行判定
            }
        }
        if (!parsed)
        {
            // 文本为空或以 { 开头却解析失败 → 视为超时；否则取第一行（截断 200 字符）作为原因
            if (output.Length == 0 || output.TrimStart().StartsWith('{'))
            {
                return new ProbeResult(false, 0, $"探测 TCP {port} 未完成（超时或连接中断）：可能被防火墙拦截；UDP 测速还需放行 UDP {port}");
            }
            string firstLine = output.Split('\n', 2)[0].Trim();
            if (firstLine.Length > 200)
            {
                firstLine = firstLine[..200];
            }
            return new ProbeResult(false, 0, firstLine);
        }

        if (!string.IsNullOrEmpty(doc!.Error))
        {
            string err = doc.Error.ToLowerInvariant();
            if (err.Contains("busy"))
            {
                return new ProbeResult(true, 0, string.Empty);
            }
            if (err.Contains("timed out"))
            {
                return new ProbeResult(false, 0, $"连接 TCP {port} 超时：可能被防火墙拦截；请放行 TCP {port}（UDP 测速另需 UDP {port}）");
            }
            if (err.Contains("refused"))
            {
                return new ProbeResult(false, 0, $"连接被拒绝：端口 {port} 未监听或被防火墙拒绝");
            }
            if (err.Contains("no route") || err.Contains("unreachable"))
            {
                return new ProbeResult(false, 0, "没有到该地址的路由");
            }
            return new ProbeResult(false, 0, doc.Error);
        }

        // 成功：rttMs 取 end.streams 中第一个 sender.mean_rtt > 0 的值（微秒 → 毫秒）。
        // Windows 版 iperf3 作客户端时该值不可信，由流程层置 0 并用 ping 覆盖。
        double rtt = 0;
        var streams = doc.End?.Streams;
        if (streams != null)
        {
            foreach (var st in streams)
            {
                double mean = st.Sender?.MeanRtt ?? 0;
                if (mean > 0)
                {
                    rtt = mean / 1000.0;
                    break;
                }
            }
        }
        return new ProbeResult(true, rtt, string.Empty);
    }
}

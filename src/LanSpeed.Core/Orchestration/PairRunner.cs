using LanSpeed.Core.Control;
using LanSpeed.Core.Iperf;
using LanSpeed.Core.Net;

namespace LanSpeed.Core.Orchestration;

public sealed record PairResult(string Status, Summary? Summary, long LinkMbps, string Reason)
{
    public static PairResult Done(Summary s, long linkMbps) => new("done", s, linkMbps, string.Empty);

    public static PairResult Failed(string status, string reason) => new(status, null, 0, reason);
}

/// <summary>单对测速流程（§6.2 六步）：A 为客户端侧，B 作服务端。</summary>
public sealed class PairRunner
{
    /// <param name="a">A 侧节点客户端（发起机自身时为 LocalNodeClient）。</param>
    /// <param name="b">B 侧节点客户端。</param>
    /// <param name="p">测速参数（方法内 Normalize）。</param>
    /// <param name="targetIp">显式目标地址；null 时按同网段自动选择。</param>
    public async Task<PairResult> RunAsync(
        INodeClient a,
        INodeClient b,
        Params p,
        string? targetIp = null,
        IProgress<Sample>? progress = null,
        CancellationToken ct = default)
    {
        p.Normalize();
        var flow = Flow.New(Sides.B, p.Direction);
        // 服务端存活上限：时长 + 忽略 + 60（流程层 Duration+Omit+30 再加 30 秒余量）
        int maxSeconds = p.Duration + p.Omit + 60;

        var aAddrs = ToNetAddrs(await a.AddrsAsync(ct));
        var bAddrs = ToNetAddrs(await b.AddrsAsync(ct));
        var path = targetIp != null ? PathSelect.ForTarget(targetIp, aAddrs) : PathSelect.SelectLanPath(aAddrs, bAddrs);
        if (path == null)
        {
            return PairResult.Failed("nolan", "双方没有同网段的 lan 地址");
        }

        var server = await b.ServerStartAsync(0, maxSeconds, ct);
        try
        {
            var probe = await a.ProbeAsync(path.TargetIp, server.Port, ct);
            if (!probe.Ok)
            {
                return PairResult.Failed("failed", probe.Reason);
            }
            var ping = await a.PingAsync(path.TargetIp, ct);

            var parser = new StreamParser(flow, trustRtt: false);
            var req = new ClientRunRequest(path.TargetIp, server.Port, path.BindIp, p, new FlowDto(Sides.B, p.Direction));
            await foreach (var line in a.ClientRunAsync(req, ct))
            {
                if (parser.Feed(line) is { } sample)
                {
                    progress?.Report(sample);
                }
            }

            var summary = parser.BuildSummary();
            if (parser.ErrorMessage != null)
            {
                return PairResult.Failed("failed", parser.ErrorMessage);
            }
            if (summary.AB <= 0 && summary.BA <= 0)
            {
                return PairResult.Failed("failed", "没有测到任何数据，可能是防火墙拦截或连接中途断开");
            }
            // Windows 版 iperf3 的 RTT 不可信，用 ping 覆盖（ping > 0 才覆盖）
            if (ping.RttMs > 0)
            {
                summary.RTTMs = ping.RttMs;
            }
            return PairResult.Done(summary, path.LinkMbps);
        }
        finally
        {
            await b.ServerStopAsync(server.Handle, CancellationToken.None);
        }
    }

    private static List<NetAddr> ToNetAddrs(List<NodeAddrDto> dtos) =>
        dtos.Select(x => new NetAddr(x.Ip, x.Prefix, x.Iface, x.SpeedMbps, x.DefaultRoute)).ToList();
}

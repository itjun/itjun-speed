using LanSpeed.Core.Iperf;
using LanSpeed.Core.Net;
using LanSpeed.Core.Runner;

namespace LanSpeed.Core.Control;

/// <summary>
/// 节点操作：控制接口（HTTP）与本机直调（LocalNodeClient）共用的实现，
/// 保证两条路径行为一致（发起机自身是 A 或 B 时走本机直调，§6.2）。
/// </summary>
public sealed class NodeOps(IIperfRunner runner, ILocalNode node)
{
    public bool Accept => node.Accept;

    public NodeHello Hello() => new(
        T: "hello",
        V: 1,
        Id: node.Id,
        Name: node.Name,
        Version: node.Version,
        Iperf: node.IperfVersion,
        Ips: node.Addrs(),
        CtrlPort: node.CtrlPort,
        Accept: node.Accept,
        Busy: runner.ServerRunning || runner.ClientRunning);

    public Task<int> ServerStartAsync(int port, int maxSeconds, CancellationToken ct = default)
    {
        // 硬性上限：时长上限 300 + 忽略 10 + 余量（§9）
        maxSeconds = Math.Clamp(maxSeconds, 1, 600);
        return runner.StartServerAsync(port, maxSeconds, ct);
    }

    public Task ServerStopAsync(CancellationToken ct = default) => runner.StopServerAsync();

    public IReadOnlyList<string> BuildClientArgs(ClientRunRequest req)
    {
        var p = req.Params;
        p.Normalize();
        var flow = Flow.New(req.Flow.ServerSide, req.Flow.Direction);
        return ClientArgs.Build(req.Target, req.Port, p, flow, req.BindIp, Runner.IperfLocator.Current.SupportsJsonStream);
    }

    public IAsyncEnumerable<string> ClientRunAsync(ClientRunRequest req, CancellationToken ct = default) =>
        runner.RunClientStreamAsync(BuildClientArgs(req), ct);

    public Task ClientStopAsync(CancellationToken ct = default) => runner.StopClientAsync();

    public async Task<ProbeResponse> ProbeAsync(string target, int port, CancellationToken ct = default)
    {
        var output = await runner.RunProbeAsync(ClientArgs.Probe(target, port), ct);
        var r = ProbeResult.Parse(output, port);
        // Windows 版 iperf3 作客户端时 RTT 不可信，置 0，由流程层用 ping 覆盖（§6.5）
        return new ProbeResponse(r.Ok, 0, r.Reason);
    }

    public async Task<PingResponse> PingAsync(string target, CancellationToken ct = default)
    {
        double rtt = await PingHelper.AverageRttMsAsync(target, ct: ct);
        return new PingResponse(rtt);
    }
}

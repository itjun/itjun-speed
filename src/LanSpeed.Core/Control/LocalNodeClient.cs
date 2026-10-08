using System.Net.Http.Json;

namespace LanSpeed.Core.Control;

/// <summary>本机直调：不走 HTTP，供发起机自身作为 A 或 B 时使用。</summary>
public sealed class LocalNodeClient(NodeOps ops) : INodeClient
{
    public string Endpoint => "local";

    public Task<NodeHello> HelloAsync(CancellationToken ct = default) => Task.FromResult(ops.Hello());

    public Task<List<NodeAddrDto>> AddrsAsync(CancellationToken ct = default) => Task.FromResult(ops.Hello().Ips);

    public async Task<ServerStartResponse> ServerStartAsync(int port, int maxSeconds, CancellationToken ct = default)
    {
        int actual = await ops.ServerStartAsync(port, maxSeconds, ct);
        return new ServerStartResponse(actual, Guid.NewGuid().ToString("N"));
    }

    public Task ServerStopAsync(string handle, CancellationToken ct = default) => ops.ServerStopAsync(ct);

    public IAsyncEnumerable<string> ClientRunAsync(ClientRunRequest req, CancellationToken ct = default) =>
        ops.ClientRunAsync(req, ct);

    public Task ClientStopAsync(CancellationToken ct = default) => ops.ClientStopAsync(ct);

    public Task<ProbeResponse> ProbeAsync(string target, int port, CancellationToken ct = default) =>
        ops.ProbeAsync(target, port, ct);

    public Task<PingResponse> PingAsync(string target, CancellationToken ct = default) => ops.PingAsync(target, ct);

    public void Dispose()
    {
    }
}

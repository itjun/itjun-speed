namespace LanSpeed.Core.Control;

/// <summary>节点客户端抽象：HTTP 远程与本地直调各一个实现（§6.2）。</summary>
public interface INodeClient : IDisposable
{
    string Endpoint { get; }

    Task<NodeHello> HelloAsync(CancellationToken ct = default);

    Task<List<NodeAddrDto>> AddrsAsync(CancellationToken ct = default);

    Task<ServerStartResponse> ServerStartAsync(int port, int maxSeconds, CancellationToken ct = default);

    Task ServerStopAsync(string handle, CancellationToken ct = default);

    /// <summary>以该节点为客户端运行 iperf3，逐行流回 --json-stream 输出。</summary>
    IAsyncEnumerable<string> ClientRunAsync(ClientRunRequest req, CancellationToken ct = default);

    Task ClientStopAsync(CancellationToken ct = default);

    Task<ProbeResponse> ProbeAsync(string target, int port, CancellationToken ct = default);

    Task<PingResponse> PingAsync(string target, CancellationToken ct = default);
}

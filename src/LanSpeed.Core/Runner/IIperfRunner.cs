namespace LanSpeed.Core.Runner;

/// <summary>节点忙（已有服务端或客户端在运行），控制接口映射为 409。</summary>
public sealed class BusyException(string message) : Exception(message);

/// <summary>本机 iperf3 引擎抽象：每节点同时最多一个服务端与一个客户端（§5 约束）。</summary>
public interface IIperfRunner : IDisposable
{
    bool ServerRunning { get; }

    bool ClientRunning { get; }

    /// <summary>启动服务端；port=0 时在 5201–5210 自动选择。返回实际端口。</summary>
    Task<int> StartServerAsync(int port, int maxSeconds, CancellationToken ct = default);

    /// <summary>停止当前服务端（无服务端时为空操作）。</summary>
    Task StopServerAsync();

    /// <summary>以客户端身份运行，逐行产出 --json-stream 输出。</summary>
    IAsyncEnumerable<string> RunClientStreamAsync(IReadOnlyList<string> args, CancellationToken ct = default);

    /// <summary>运行一次连通探测（-J 整体输出），整体超时 6 秒由实现保证。</summary>
    Task<string> RunProbeAsync(IReadOnlyList<string> args, CancellationToken ct = default);

    /// <summary>中止当前客户端。</summary>
    Task StopClientAsync();
}

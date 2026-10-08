using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using LanSpeed.Core.Control;
using LanSpeed.Core.Net;

namespace LanSpeed.Core.Discovery;

/// <summary>
/// 发现服务（§4.1）：UDP 39300。监听广播——收到 discover 单播应答 hello；
/// 主动扫描时发送 discover 并收集 hello 应答；节点启动 / 网卡变化时广播 hello。
/// </summary>
public sealed class DiscoveryService(Func<NodeHello> helloFactory, int port = 39300) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private volatile bool _started;
    private DateTime _lastAnnounce = DateTime.MinValue;

    /// <summary>收到其他节点的 hello（应答或主动广播）。发起机在 DiscoverAsync 里订阅。</summary>
    public event Action<NodeHello, IPEndPoint>? HelloSeen;

    public bool IsRunning => _started;

    /// <summary>开始监听（幂等）。serve 模式常驻；scan 前也会自动调用。</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started)
            {
                return;
            }
            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            _udp.EnableBroadcast = true;
            _cts = new CancellationTokenSource();
            _started = true;
            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        }
    }

    /// <summary>扫描：广播 discover，等待应答，按节点 ID 去重（排除自己）。</summary>
    public async Task<List<NodeHello>> DiscoverAsync(TimeSpan wait, CancellationToken ct = default)
    {
        Start();
        string selfId = helloFactory().Id;
        var seen = new Dictionary<string, NodeHello>();
        void OnHello(NodeHello h, IPEndPoint _)
        {
            lock (seen)
            {
                if (!string.IsNullOrEmpty(h.Id) && h.Id != selfId)
                {
                    seen[h.Id] = h;
                }
            }
        }
        HelloSeen += OnHello;
        try
        {
            await BroadcastAsync(JsonSerializer.SerializeToUtf8Bytes(new DiscoverPacket("discover", 1, selfId), JsonOpts));
            await Task.Delay(wait, ct);
        }
        finally
        {
            HelloSeen -= OnHello;
        }
        lock (seen)
        {
            return seen.Values.ToList();
        }
    }

    /// <summary>主动广播一次 hello（节点启动、网卡变化时调用；内部限频 5 秒）。</summary>
    public async Task AnnounceAsync(bool force = false)
    {
        Start();
        lock (_gate)
        {
            if (!force && (DateTime.UtcNow - _lastAnnounce).TotalSeconds < 5)
            {
                return;
            }
            _lastAnnounce = DateTime.UtcNow;
        }
        var hello = helloFactory();
        await BroadcastAsync(JsonSerializer.SerializeToUtf8Bytes(hello, JsonOpts));
    }

    private async Task BroadcastAsync(byte[] payload)
    {
        var udp = _udp ?? throw new InvalidOperationException("发现服务未启动");
        var targets = new List<IPEndPoint> { new(IPAddress.Broadcast, port) };
        targets.AddRange(NicFilter.LocalNics().Select(n => new IPEndPoint(n.Broadcast, port)));
        foreach (var target in targets.Distinct())
        {
            try
            {
                await udp.SendAsync(payload, target);
            }
            catch (SocketException)
            {
                // 个别网卡不允许定向广播，跳过
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var udp = _udp!;
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(ct);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException or SocketException)
            {
                break;
            }
            HandleDatagram(udp, result);
        }
    }

    private void HandleDatagram(UdpClient udp, UdpReceiveResult result)
    {
        NodeHello? hello = null;
        DiscoverPacket? discover = null;
        try
        {
            hello = JsonSerializer.Deserialize<NodeHello>(result.Buffer, JsonOpts);
            discover = JsonSerializer.Deserialize<DiscoverPacket>(result.Buffer, JsonOpts);
        }
        catch (JsonException)
        {
            return;
        }
        if (hello?.T == "hello" && !string.IsNullOrEmpty(hello.Id))
        {
            if (hello.Id != helloFactory().Id)
            {
                HelloSeen?.Invoke(hello, result.RemoteEndPoint);
            }
            return;
        }
        if (discover?.T == "discover" && !string.IsNullOrEmpty(discover.Id))
        {
            // 单播应答给发起机（发起机的 39300 监听会收到）
            var reply = JsonSerializer.SerializeToUtf8Bytes(helloFactory(), JsonOpts);
            try
            {
                udp.Send(reply, reply.Length, result.RemoteEndPoint);
            }
            catch (SocketException)
            {
                // 对端已退出扫描，忽略
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _udp?.Close();
            _udp?.Dispose();
            _udp = null;
            _started = false;
        }
    }

    private sealed record DiscoverPacket(
        [property: System.Text.Json.Serialization.JsonPropertyName("t")] string? T,
        [property: System.Text.Json.Serialization.JsonPropertyName("v")] int V,
        [property: System.Text.Json.Serialization.JsonPropertyName("id")] string? Id);
}

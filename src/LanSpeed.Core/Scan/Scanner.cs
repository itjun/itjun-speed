using System.Net;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using LanSpeed.Core.Control;
using LanSpeed.Core.Discovery;
using LanSpeed.Core.Net;

namespace LanSpeed.Core.Scan;

/// <summary>主机表条目：IP、MAC、主机名、发现来源与最终状态（§4.3）。</summary>
public sealed record HostEntry(
    string Ip,
    string? Mac,
    bool PingOk,
    bool ArpSeen,
    NodeHello? Hello,
    bool PortOk)
{
    public string? Hostname { get; set; }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public string Status
    {
        get
        {
            if (Hello != null)
            {
                if (!PortOk)
                {
                    return "已安装·控制端口不通";
                }
                if (!Hello.Accept)
                {
                    return "已安装·拒绝被测";
                }
                if (Hello.Busy)
                {
                    return "已安装·忙";
                }
                return "已安装·可测";
            }
            if (PingOk || ArpSeen)
            {
                return "在线·未安装";
            }
            return "无响应";
        }
    }

    /// <summary>对该 IP 的 39301 控制接口做 HTTP 探测（§4.2 第三层，超时 1 秒）。</summary>
    public static async Task<NodeHello?> ProbeCtrlAsync(string ip, int port, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri($"http://{ip}:{port}/"), Timeout = TimeSpan.FromSeconds(1) };
            return await http.GetFromJsonAsync<NodeHello>("v1/hello", JsonOpts, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }
}

/// <summary>局域网扫描（§4.2）：发现广播 + ping + ARP + 端口探测 + 主机名，合并为主机表。</summary>
public sealed class Scanner(DiscoveryService discovery)
{
    public async Task<List<HostEntry>> ScanAsync(TimeSpan? discoveryWait = null, CancellationToken ct = default)
    {
        // 第一层：UDP 发现已安装节点
        var hellos = await discovery.DiscoverAsync(discoveryWait ?? TimeSpan.FromSeconds(1.5), ct);
        var byIp = new Dictionary<string, (NodeHello Hello, bool PortOk)>();
        foreach (var hello in hellos)
        {
            foreach (var ip in hello.Ips.Where(x => AddrClassify.Classify(IPAddress.TryParse(x.Ip, out var a) ? a : null) == AddrKind.Lan))
            {
                var probed = await HostEntry.ProbeCtrlAsync(ip.Ip, hello.CtrlPort != 0 ? hello.CtrlPort : 39301, ct);
                byIp[ip.Ip] = (hello, probed != null);
            }
        }

        // 第二层：网段内 ping + ARP，补齐未安装主机
        var candidates = CandidateIps();
        var pingOk = await PingSweepAsync(candidates, ct);
        var arp = await ArpTable.ReadAsync();

        var entries = new Dictionary<string, HostEntry>();
        foreach (var (ip, helloTuple) in byIp)
        {
            entries[ip] = new HostEntry(ip, arp.GetValueOrDefault(ip), pingOk.GetValueOrDefault(ip), arp.ContainsKey(ip), helloTuple.Hello, helloTuple.PortOk);
        }
        foreach (var ip in candidates.Where(ip => !entries.ContainsKey(ip)))
        {
            bool alive = pingOk.GetValueOrDefault(ip) || arp.ContainsKey(ip);
            NodeHello? hello = null;
            bool portOk = false;
            if (alive)
            {
                // 广播丢失的已安装节点靠端口探测补齐
                hello = await HostEntry.ProbeCtrlAsync(ip, 39301, ct);
                portOk = hello != null;
            }
            entries[ip] = new HostEntry(ip, arp.GetValueOrDefault(ip), pingOk.GetValueOrDefault(ip), arp.ContainsKey(ip), hello, portOk);
        }

        // 主机名：反向解析（超时 1.5 秒，失败为空）
        await ResolveHostnamesAsync(entries.Values.Where(e => e.PingOk || e.ArpSeen).ToList(), ct);

        return entries.Values
            .Where(e => e.Hello != null || e.PingOk || e.ArpSeen)
            .OrderBy(e => IPAddress.Parse(e.Ip).GetAddressBytes()[0])
            .ThenBy(e => IPAddress.Parse(e.Ip).GetAddressBytes()[1])
            .ThenBy(e => IPAddress.Parse(e.Ip).GetAddressBytes()[2])
            .ThenBy(e => IPAddress.Parse(e.Ip).GetAddressBytes()[3])
            .ToList();
    }

    // 每块物理网卡所在网段；掩码大于 /22 时只扫本机所在的 /22（最多约 1024 个地址，§4.2）
    internal static List<string> CandidateIps()
    {
        var ips = new List<string>();
        foreach (var nic in NicFilter.LocalNics())
        {
            var ipBytes = IPAddress.Parse(nic.Ip).GetAddressBytes();
            int prefix = Math.Max(nic.Prefix, 22);
            var mask = PrefixToMask(prefix);
            var network = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                network[i] = (byte)(ipBytes[i] & mask[i]);
            }
            uint start = ToUint(network) + 1;
            uint end = ToUint(network) + (1u << (32 - prefix)) - 2; // 去掉网络地址与广播地址
            for (uint v = start; v <= end; v++)
            {
                ips.Add(FromUint(v));
            }
        }
        return ips.Distinct().ToList();
    }

    private static async Task<Dictionary<string, bool>> PingSweepAsync(IEnumerable<string> ips, CancellationToken ct)
    {
        var result = new Dictionary<string, bool>();
        using var sem = new SemaphoreSlim(64);
        var tasks = ips.Select(async ip =>
        {
            await sem.WaitAsync(ct);
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, 800);
                lock (result)
                {
                    result[ip] = reply.Status == IPStatus.Success;
                }
            }
            catch (Exception ex) when (ex is PingException or InvalidOperationException)
            {
                lock (result)
                {
                    result[ip] = false;
                }
            }
            finally
            {
                sem.Release();
            }
        });
        await Task.WhenAll(tasks);
        return result;
    }

    private static async Task ResolveHostnamesAsync(List<HostEntry> entries, CancellationToken ct)
    {
        var tasks = entries.Select(async e =>
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(1500);
                var host = await Dns.GetHostEntryAsync(e.Ip, timeout.Token);
                e.Hostname = host.HostName.Split('.')[0];
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                // 反向解析失败很常见（未在路由器登记名字），保持空
            }
        });
        await Task.WhenAll(tasks);
    }

    private static byte[] PrefixToMask(int prefix)
    {
        var mask = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            mask[i] = prefix >= (i + 1) * 8
                ? (byte)0xFF
                : prefix > i * 8 ? (byte)(0xFF << (8 - Math.Min(8, prefix - i * 8))) : (byte)0;
        }
        return mask;
    }

    private static uint ToUint(byte[] b) => (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);

    private static string FromUint(uint v) => $"{v >> 24 & 0xFF}.{v >> 16 & 0xFF}.{v >> 8 & 0xFF}.{v & 0xFF}";
}

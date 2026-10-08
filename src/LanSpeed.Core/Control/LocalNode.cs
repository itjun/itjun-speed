using System.Net.NetworkInformation;

namespace LanSpeed.Core.Control;

/// <summary>本机节点信息（id 持久化在 %LOCALAPPDATA%\LanSpeed\node-id.txt）。</summary>
public interface ILocalNode
{
    string Id { get; }

    string Name { get; }

    string Version { get; }

    string IperfVersion { get; }

    int CtrlPort { get; }

    /// <summary>允许被测开关（§9）；关闭后控制接口只保留 hello。</summary>
    bool Accept { get; set; }

    List<NodeAddrDto> Addrs();
}

public sealed class LocalNode : ILocalNode
{
    private readonly string _idFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanSpeed", "node-id.txt");

    public LocalNode(int ctrlPort)
    {
        CtrlPort = ctrlPort;
        Id = LoadOrCreateId();
        Name = Environment.MachineName;
        Version = typeof(LocalNode).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    }

    public string Id { get; }

    public string Name { get; }

    public string Version { get; }

    // 内置 iperf3 3.22 版本（未就绪时显示提示）
    public string IperfVersion => Runner.IperfLocator.TryVersion();

    public int CtrlPort { get; }

    /// <summary>允许被测开关（§9，默认开启）；关闭后控制接口只保留 hello。</summary>
    public bool Accept { get; set; } = true;

    public List<NodeAddrDto> Addrs()
    {
        var list = new List<NodeAddrDto>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }
            var props = nic.GetIPProperties();
            foreach (var addr in props.UnicastAddresses)
            {
                if (addr.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    continue;
                }
                int prefix = PrefixOf(addr);
                if (prefix <= 0)
                {
                    continue;
                }
                long speedMbps = nic.Speed / 1_000_000;
                if (speedMbps < 0 || speedMbps > 100_000)
                {
                    speedMbps = 0; // 未知或异常协商速率
                }
                list.Add(new NodeAddrDto(
                    addr.Address.ToString(),
                    prefix,
                    nic.Name,
                    speedMbps,
                    props.GatewayAddresses.Count > 0));
            }
        }
        return list;
    }

    private static int PrefixOf(UnicastIPAddressInformation addr)
    {
        // IPv4Mask 在 Windows 上返回子网掩码；转换成前缀长度
        var mask = addr.IPv4Mask;
        if (mask == null || mask.Equals(System.Net.IPAddress.None))
        {
            return 0;
        }
        int prefix = 0;
        foreach (var b in mask.GetAddressBytes())
        {
            prefix += System.Numerics.BitOperations.PopCount(b);
        }
        return prefix;
    }

    private string LoadOrCreateId()
    {
        try
        {
            if (File.Exists(_idFile))
            {
                var existing = File.ReadAllText(_idFile).Trim();
                if (existing.Length > 0)
                {
                    return existing;
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_idFile)!);
            var id = Guid.NewGuid().ToString("N");
            File.WriteAllText(_idFile, id);
            return id;
        }
        catch (IOException)
        {
            // 落盘失败时退化为进程内 ID（仅影响跨重启的身份连续性）
            return Guid.NewGuid().ToString("N");
        }
    }
}

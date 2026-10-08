using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;

namespace LanSpeed.Core.Net;

public sealed record NicInfo(string Ip, int Prefix, string Name, long SpeedMbps, bool DefaultRoute, IPAddress Broadcast);

/// <summary>网卡过滤（§4.4）：排除虚拟网卡、代理 TUN、蓝牙等；地址排除回环 / 169.254 / 198.18-19。</summary>
public static class NicFilter
{
    private static readonly string[] ExcludedKeywords =
    [
        "loopback", "lo0", "hyper-v", "vethernet", "wsl", "vmware", "vmnet", "virtualbox",
        "docker", "veth", "br-", "virbr", "tap-windows", "wintun", "wireguard", "tailscale",
        "zerotier", "openvpn", "npcap", "bluetooth", "utun", "tun0", "wg0",
    ];

    public static List<NicInfo> LocalNics()
    {
        var result = new List<NicInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }
            string hay = (nic.Name + " " + nic.Description).ToLowerInvariant();
            if (ExcludedKeywords.Any(hay.Contains))
            {
                continue;
            }
            var props = nic.GetIPProperties();
            bool defaultRoute = props.GatewayAddresses.Count > 0;
            foreach (var addr in props.UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }
                var mask = addr.IPv4Mask;
                if (mask == null)
                {
                    continue;
                }
                var ipBytes = addr.Address.GetAddressBytes();
                var maskBytes = mask.GetAddressBytes();
                // 169.254.0.0/16 链路本地与 198.18.0.0/15 代理 fake-ip 段排除
                if (ipBytes[0] == 169 && ipBytes[1] == 254 || ipBytes[0] == 198 && ipBytes[1] is 18 or 19)
                {
                    continue;
                }
                int prefix = 0;
                var broadcast = new byte[4];
                for (int i = 0; i < 4; i++)
                {
                    prefix += BitOperations.PopCount(maskBytes[i]);
                    broadcast[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                }
                if (prefix <= 0 || prefix >= 32)
                {
                    continue;
                }
                long speedMbps = nic.Speed / 1_000_000;
                if (speedMbps < 0 || speedMbps > 100_000)
                {
                    speedMbps = 0; // 未知或异常协商速率
                }
                result.Add(new NicInfo(addr.Address.ToString(), prefix, nic.Name, speedMbps, defaultRoute, new IPAddress(broadcast)));
            }
        }
        return result;
    }
}

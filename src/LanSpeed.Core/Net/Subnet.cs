using System.Net;
using System.Net.Sockets;

namespace LanSpeed.Core.Net;

public static class Subnet
{
    /// <summary>
    /// 两个 IPv4 地址是否同网段（附录 A.7）：都有合法前缀长度，且按较短的那个前缀取网络号后相等。
    /// prefix 为 0 或 /32 时不参与判断（视为不同网段）。
    /// </summary>
    public static bool SameSubnet(IPAddress? a, int prefixA, IPAddress? b, int prefixB)
    {
        if (a == null || b == null)
        {
            return false;
        }
        if (a.IsIPv4MappedToIPv6)
        {
            a = a.MapToIPv4();
        }
        if (b.IsIPv4MappedToIPv6)
        {
            b = b.MapToIPv4();
        }
        if (a.AddressFamily != AddressFamily.InterNetwork || b.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }
        // prefix 为 0（无掩码信息）或 /32（单主机地址）时不参与判断
        if (prefixA < 1 || prefixA > 31 || prefixB < 1 || prefixB > 31)
        {
            return false;
        }
        int prefix = Math.Min(prefixA, prefixB);
        var x = a.GetAddressBytes();
        var y = b.GetAddressBytes();
        int full = prefix / 8, rest = prefix % 8;
        for (int i = 0; i < full; i++)
        {
            if (x[i] != y[i])
            {
                return false;
            }
        }
        if (rest > 0)
        {
            byte mask = (byte)(0xFF << (8 - rest));
            if ((x[full] & mask) != (y[full] & mask))
            {
                return false;
            }
        }
        return true;
    }
}

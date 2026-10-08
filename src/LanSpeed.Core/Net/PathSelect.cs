using System.Net;

namespace LanSpeed.Core.Net;

/// <summary>单轮测速的路径选择（附录 A.7）：选 B 的目标地址与 A 的绑定地址。</summary>
public static class PathSelect
{
    public sealed record LanPath(string TargetIp, string BindIp, long LinkMbps);

    /// <summary>
    /// 在 A、B 双方的 lan 地址中找同网段组合：target = B 的地址，bindIp = A 的对应地址。
    /// 链路上限 = min(两端网卡协商速率)（任一为 0 时取另一个，都为 0 则未知）。M1 取第一个匹配。
    /// </summary>
    public static LanPath? SelectLanPath(IEnumerable<NetAddr> aAddrs, IEnumerable<NetAddr> bAddrs)
    {
        var aLans = aAddrs.Where(x => x.Kind == AddrKind.Lan).ToList();
        foreach (var b in bAddrs.Where(x => x.Kind == AddrKind.Lan))
        {
            foreach (var a in aLans)
            {
                if (Subnet.SameSubnet(a.Address, a.Prefix, b.Address, b.Prefix))
                {
                    return new LanPath(b.Ip, a.Ip, LinkLimit(a.SpeedMbps, b.SpeedMbps));
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 目标 IP 由用户直接给定（CLI test）：回环直接绑定回环；否则找与目标同网段的本地 lan 地址。
    /// </summary>
    public static LanPath? ForTarget(string targetIp, IEnumerable<NetAddr> localAddrs)
    {
        if (!IPAddress.TryParse(targetIp, out var target))
        {
            return null;
        }
        if (IPAddress.IsLoopback(target))
        {
            return new LanPath(targetIp, targetIp, 0);
        }
        foreach (var local in localAddrs.Where(x => x.Kind == AddrKind.Lan))
        {
            // 目标前缀未知，按本地地址前缀判断即可（同网段选择只为绑定本机地址）
            if (Subnet.SameSubnet(local.Address, local.Prefix, target, local.Prefix))
            {
                return new LanPath(targetIp, local.Ip, local.SpeedMbps);
            }
        }
        return null;
    }

    private static long LinkLimit(long a, long b)
    {
        if (a <= 0)
        {
            return b;
        }
        if (b <= 0)
        {
            return a;
        }
        return Math.Min(a, b);
    }
}

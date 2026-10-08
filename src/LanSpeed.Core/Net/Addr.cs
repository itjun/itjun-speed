using System.Net;
using System.Net.Sockets;

namespace LanSpeed.Core.Net;

/// <summary>地址分类（§4.4）：RFC1918 私网为 lan；100.64/10 为 overlay；其余为 wan。</summary>
public enum AddrKind
{
    Lan,
    Overlay,
    Wan,
}

public sealed record NetAddr(string Ip, int Prefix, string Iface = "", long SpeedMbps = 0, bool DefaultRoute = false)
{
    public IPAddress? Address
    {
        get
        {
            IPAddress.TryParse(Ip, out var addr);
            return addr;
        }
    }

    public AddrKind Kind => AddrClassify.Classify(Address);
}

public static class AddrClassify
{
    public static AddrKind Classify(IPAddress? ip)
    {
        if (ip == null)
        {
            return AddrKind.Wan;
        }
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }
        if (ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return AddrKind.Wan;
        }
        var b = ip.GetAddressBytes();
        return b[0] switch
        {
            10 => AddrKind.Lan,
            172 when b[1] >= 16 && b[1] <= 31 => AddrKind.Lan,
            192 when b[1] == 168 => AddrKind.Lan,
            100 when b[1] >= 64 && b[1] <= 127 => AddrKind.Overlay,
            _ => AddrKind.Wan,
        };
    }

    /// <summary>是否私有地址段（控制接口的白名单：RFC1918 + 回环，回环用于本机自测）。</summary>
    public static bool IsPrivateOrLoopback(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }
        return Classify(ip) == AddrKind.Lan;
    }
}

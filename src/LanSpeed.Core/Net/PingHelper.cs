using System.Net.NetworkInformation;

namespace LanSpeed.Core.Net;

/// <summary>ping 平均 RTT（§6.5：Windows 版 iperf3 拿不到可靠 TCP RTT，RTT 由 ping 提供）。</summary>
public static class PingHelper
{
    public static async Task<double> AverageRttMsAsync(string host, int count = 3, int timeoutMs = 1000, CancellationToken ct = default)
    {
        using var ping = new Ping();
        double total = 0;
        int n = 0;
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var reply = await ping.SendPingAsync(host, timeoutMs);
            if (reply.Status == IPStatus.Success)
            {
                total += reply.RoundtripTime;
                n++;
            }
        }
        return n > 0 ? total / n : 0;
    }
}

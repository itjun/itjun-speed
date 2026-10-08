using System.Globalization;

namespace LanSpeed.Core.Iperf;

/// <summary>iperf3 客户端参数组装（附录 A.3 的 Windows 版：无 -C，可加 -B 绑定同网段本机地址）。</summary>
public static class ClientArgs
{
    /// <param name="target">服务端 IP。</param>
    /// <param name="port">服务端端口。</param>
    /// <param name="p">已 Normalize 的测速参数。</param>
    /// <param name="f">方向映射。</param>
    /// <param name="bindIp">本机绑定地址（多网卡时保证走对网卡），null 时不绑定。</param>
    /// <param name="jsonStream">iperf3 支持 --json-stream 时用流式输出；否则回退 -J 经典输出（由 ClassicConverter 转换）。</param>
    public static IReadOnlyList<string> Build(string target, int port, Params p, Flow f, string? bindIp = null, bool jsonStream = true)
    {
        var inv = CultureInfo.InvariantCulture;
        var args = new List<string>
        {
            "-c", target,
            "-p", port.ToString(inv),
            "-t", p.Duration.ToString(inv),
            "-P", p.Parallel.ToString(inv),
            "-i", p.Interval.ToString(inv),
            "--connect-timeout", "3000",
        };
        args.AddRange(jsonStream ? ["--json-stream", "--forceflush"] : ["-J"]);
        if (p.Omit > 0)
        {
            args.Add("-O");
            args.Add(p.Omit.ToString(inv));
        }
        if (p.Protocol == "udp")
        {
            // iperf3 的 -b 按单条流计，这里把总带宽均分到每条流（bit/s 整数）
            string bw = "0";
            if (p.UDPBandwidthMbps > 0)
            {
                long per = (long)(p.UDPBandwidthMbps * 1e6 / p.Parallel);
                bw = per.ToString(inv);
            }
            args.Add("-u");
            args.Add("-b");
            args.Add(bw);
        }
        if (f.Bidir)
        {
            args.Add("--bidir");
        }
        else if (f.Reverse)
        {
            args.Add("-R");
        }
        if (p.WindowKB > 0)
        {
            args.Add("-w");
            args.Add(p.WindowKB.ToString(inv) + "K");
        }
        if (p.MSS > 0 && p.Protocol == "tcp")
        {
            args.Add("-M");
            args.Add(p.MSS.ToString(inv));
        }
        if (!string.IsNullOrEmpty(bindIp))
        {
            args.Add("-B");
            args.Add(bindIp);
        }
        return args;
    }

    /// <summary>连通探测命令（§6.4：传输固定 256KB 代替计时，整体 6 秒超时由调用方控制）。</summary>
    public static IReadOnlyList<string> Probe(string target, int port) =>
        ["-c", target, "-p", port.ToString(CultureInfo.InvariantCulture), "--connect-timeout", "1500", "-n", "256K", "-J"];
}

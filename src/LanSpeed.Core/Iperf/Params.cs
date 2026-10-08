namespace LanSpeed.Core.Iperf;

/// <summary>测速方向常量（相对用户选定的 A、B 两台主机）。</summary>
public static class Directions
{
    public const string Forward = "forward";
    public const string Reverse = "reverse";
    public const string Bidir = "bidir";
}

/// <summary>服务端所在侧。</summary>
public static class Sides
{
    public const string A = "a";
    public const string B = "b";
}

/// <summary>测速参数（用户可调项），规整规则见 docs/DESIGN.md 附录 A.1。</summary>
public sealed class Params
{
    public string Protocol { get; set; } = "tcp";
    public int Parallel { get; set; } = 4;
    public int Duration { get; set; } = 10;
    public string Direction { get; set; } = Directions.Forward;
    public double UDPBandwidthMbps { get; set; }
    public int Port { get; set; }
    public int Omit { get; set; }
    public int WindowKB { get; set; }
    public int MSS { get; set; }
    public double Interval { get; set; } = 1;

    public void Normalize()
    {
        Protocol = Protocol.Trim().ToLowerInvariant();
        if (Protocol != "udp") Protocol = "tcp";
        Parallel = ClampInt(Parallel, 1, 64, 4);
        Duration = ClampInt(Duration, 3, 300, 10);
        Direction = Direction switch
        {
            Directions.Forward or Directions.Reverse or Directions.Bidir => Direction,
            _ => Directions.Forward,
        };
        if (UDPBandwidthMbps < 0) UDPBandwidthMbps = 0;
        if (Port != 0 && (Port < 1024 || Port > 65535)) Port = 0;
        if (Omit < 0 || Omit > 10) Omit = 0;
        if (WindowKB < 0) WindowKB = 0;
        if (MSS < 0 || MSS > 9000) MSS = 0;
        if (Interval != 0.5) Interval = 1;
    }

    // v 为 0 时取默认值，否则夹紧到 [lo, hi]
    private static int ClampInt(int v, int lo, int hi, int def)
    {
        if (v == 0) return def;
        if (v < lo) return lo;
        if (v > hi) return hi;
        return v;
    }
}

namespace LanSpeed.Core.Iperf;

/// <summary>
/// 客户端视角到用户视角的方向映射（附录 A.2，最容易写错的部分）：
/// 用户选的方向相对 A、B；iperf3 只有“客户端→服务端”的概念。
/// </summary>
/// <param name="ClientIsA">客户端是否为 A 侧（即服务端在 B 侧）。</param>
/// <param name="Reverse">-R：服务端发、客户端收。</param>
/// <param name="Bidir">--bidir 双向。</param>
public readonly record struct Flow(bool ClientIsA, bool Reverse, bool Bidir)
{
    /// <summary>客户端→服务端方向是否就是 A→B。</summary>
    public bool C2sIsAB => ClientIsA;

    public static Flow New(string serverSide, string direction)
    {
        bool clientIsA = serverSide == Sides.B;
        bool reverse = false, bidir = false;
        switch (direction)
        {
            case Directions.Bidir:
                bidir = true;
                break;
            case Directions.Reverse: // 用户要 B→A
                reverse = clientIsA;
                break;
            default: // 用户要 A→B
                reverse = !clientIsA;
                break;
        }
        return new Flow(clientIsA, reverse, bidir);
    }
}

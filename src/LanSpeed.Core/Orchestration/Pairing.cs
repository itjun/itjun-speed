namespace LanSpeed.Core.Orchestration;

public enum GroupMode
{
    Star,
    Mesh,
}

public sealed record Pair(string A, string B);

/// <summary>分组配对（附录 A.9）：星形 N−1 轮双向同时；矩阵 N×(N−1) 轮单向。</summary>
public static class Pairing
{
    /// <summary>生成分组轮次；会按模式改写 <paramref name="p"/> 的方向（星形 bidir / 矩阵 forward）。</summary>
    public static IReadOnlyList<Pair> Build(GroupMode mode, IEnumerable<string> hosts, string? center, Iperf.Params p)
    {
        var list = hosts.Distinct().ToList();
        if (list.Count < 2)
        {
            throw new ArgumentException("分组至少需要 2 台主机");
        }
        var pairs = new List<Pair>();
        switch (mode)
        {
            case GroupMode.Star:
                if (string.IsNullOrEmpty(center) || !list.Contains(center))
                {
                    throw new ArgumentException("星形中心机必须在分组内");
                }
                p.Direction = Iperf.Directions.Bidir;
                foreach (var h in list)
                {
                    if (h != center)
                    {
                        pairs.Add(new Pair(center, h));
                    }
                }
                break;
            case GroupMode.Mesh:
                p.Direction = Iperf.Directions.Forward;
                foreach (var a in list)
                {
                    foreach (var b in list)
                    {
                        if (a != b)
                        {
                            pairs.Add(new Pair(a, b));
                        }
                    }
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
        return pairs;
    }
}

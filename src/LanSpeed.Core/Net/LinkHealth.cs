namespace LanSpeed.Core.Net;

/// <summary>链路健康分类：内网应以千兆为常态；低于千兆标为排查重点，不拦截开测。</summary>
public enum LinkClass
{
    /// <summary>协商速率 ≥ 1000 Mbps。</summary>
    GigabitPlus = 0,

    /// <summary>协商速率 &gt; 0 且 &lt; 1000 Mbps——重点排查对象。</summary>
    BelowGigabit = 1,

    /// <summary>协商速率未知（0 或无效）。</summary>
    Unknown = 2,
}

/// <summary>根据网卡协商速率（Mbps）判定链路健康；无 CanStart，不拦截测速。</summary>
public static class LinkHealth
{
    public const long GigabitMbps = 1000;

    public static LinkClass Classify(long speedMbps)
    {
        if (speedMbps <= 0)
        {
            return LinkClass.Unknown;
        }
        return speedMbps < GigabitMbps ? LinkClass.BelowGigabit : LinkClass.GigabitPlus;
    }

    /// <summary>路径取两端协商速率较小者再分类。</summary>
    public static LinkClass ClassifyPath(long speedAMbps, long speedBMbps)
    {
        long a = speedAMbps > 0 ? speedAMbps : 0;
        long b = speedBMbps > 0 ? speedBMbps : 0;
        if (a <= 0 && b <= 0)
        {
            return LinkClass.Unknown;
        }
        if (a <= 0)
        {
            return Classify(b);
        }
        if (b <= 0)
        {
            return Classify(a);
        }
        return Classify(Math.Min(a, b));
    }

    /// <summary>低于千兆即为重点排查对象。</summary>
    public static bool IsInvestigationTarget(long speedMbps) => Classify(speedMbps) == LinkClass.BelowGigabit;

    public static bool IsInvestigationTarget(LinkClass c) => c == LinkClass.BelowGigabit;

    public static string Label(LinkClass c) => c switch
    {
        LinkClass.GigabitPlus => "千兆及以上",
        LinkClass.BelowGigabit => "低于千兆",
        _ => "链路未知",
    };

    /// <summary>评价 notes / UI 警告文案；千兆及以上返回 null。</summary>
    public static string? InvestigationHint(long linkMbps)
    {
        return Classify(linkMbps) switch
        {
            LinkClass.BelowGigabit =>
                $"网卡协商仅 {linkMbps} Mbps（低于千兆），疑似百兆链路——应排查网线、交换机口协商与网卡驱动",
            LinkClass.Unknown =>
                "链路速率未知，建议确认网卡驱动与协商状态后再对照测速结果",
            _ => null,
        };
    }
}

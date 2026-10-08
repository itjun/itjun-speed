using LanSpeed.Core.Iperf;
using LanSpeed.Core.Net;

namespace LanSpeed.Core.Verdict;

public enum Grade
{
    Great = 0,
    Good = 1,
    Fair = 2,
    Poor = 3,
}

public sealed record Verdict(Grade Grade, string Title, IReadOnlyList<string> Notes)
{
    public string GradeLabel => Grade switch
    {
        Grade.Great => "很快",
        Grade.Good => "正常",
        Grade.Fair => "偏慢",
        _ => "很差",
    };
}

/// <summary>结论规则（附录 A.10，新设计、阈值待实测校准）：按较慢方向定级，后续规则只会把等级往差调。</summary>
public static class VerdictEvaluator
{
    public static Verdict Evaluate(Summary s, string protocol, long linkMbps, Params p)
    {
        var notes = new List<string>();
        Grade grade = Grade.Poor;

        // 参与定级的方向：单向测试只看承载流量的方向，双向取较慢者；数值为 0 视为该方向没数据
        var speeds = DirectionalSpeeds(s, p.Direction).Where(v => v > 0).ToList();
        if (speeds.Count == 0)
        {
            notes.Add("没有测到任何数据，可能是防火墙拦截或连接中途断开");
            return new Verdict(Grade.Poor, "未测得有效吞吐", notes);
        }
        double slow = speeds.Min();
        double fast = speeds.Max();

        // 2. 速度定级
        if (slow >= 12_000e6 && (linkMbps <= 0 || slow > linkMbps * 1e6 * 1.3))
        {
            grade = Grade.Great;
            notes.Add("数据没走网线，可能是同一宿主机上的虚拟机");
        }
        else if (linkMbps > 0)
        {
            double ratio = slow / (linkMbps * 1e6);
            grade = ratio >= 0.85 ? Grade.Great : ratio >= 0.6 ? Grade.Good : ratio >= 0.3 ? Grade.Fair : Grade.Poor;
        }
        else
        {
            grade = slow >= 900e6 ? Grade.Great : slow >= 500e6 ? Grade.Good : slow >= 100e6 ? Grade.Fair : Grade.Poor;
        }

        // 3. UDP 被目标带宽封顶
        if (protocol == "udp" && p.UDPBandwidthMbps > 0 && fast >= p.UDPBandwidthMbps * 1e6 * 0.9
            && (linkMbps <= 0 || p.UDPBandwidthMbps * 1e6 < linkMbps * 1e6 * 0.9))
        {
            notes.Add("结果被目标带宽封顶");
        }

        // 4. 双向差距
        if (p.Direction == Directions.Bidir && fast > 0 && slow / fast < 0.6)
        {
            notes.Add("两个方向速度差距较大");
            Cap(Grade.Good);
        }

        // 5. UDP 丢包与抖动
        if (protocol == "udp")
        {
            if (s.LostPct < 0.1)
            {
                notes.Add("丢包很低，链路很稳");
            }
            else if (s.LostPct < 1)
            {
                notes.Add("轻微丢包");
            }
            else if (s.LostPct < 5)
            {
                notes.Add($"丢包 {s.LostPct:F1}%，偏高");
                Cap(Grade.Good);
            }
            else
            {
                notes.Add($"丢包 {s.LostPct:F1}%，很高");
                Cap(Grade.Fair);
            }
            if (s.JitterMs >= 30)
            {
                notes.Add($"抖动 {s.JitterMs:F0} ms，很高");
                Cap(Grade.Fair);
            }
            else if (s.JitterMs >= 10)
            {
                notes.Add($"抖动 {s.JitterMs:F0} ms，略有起伏");
            }
        }

        // 6. TCP 重传率（次/秒）
        if (protocol == "tcp" && s.Seconds > 0)
        {
            double rate = s.Retransmits / s.Seconds;
            if (rate == 0)
            {
                notes.Add("没有重传，链路很稳");
            }
            else if (rate < 50)
            {
                notes.Add("重传正常");
            }
            else if (rate < 500)
            {
                notes.Add($"重传率 {rate:F0} 次/秒，偏高");
                Cap(Grade.Good);
            }
            else
            {
                notes.Add($"重传率 {rate:F0} 次/秒，很高");
                Cap(Grade.Fair);
            }
        }

        // 7. 局域网延迟
        if (s.RTTMs > 0)
        {
            if (s.RTTMs < 1)
            {
                notes.Add("延迟非常低");
            }
            else if (s.RTTMs < 5)
            {
                notes.Add("延迟正常");
            }
            else
            {
                notes.Add($"延迟 {s.RTTMs:F1} ms，偏高");
            }
        }

        // 8. CPU
        if (s.CPUClient >= 90 || s.CPUServer >= 90)
        {
            notes.Add("可能被 CPU 拖慢");
        }

        // 9. 链路健康：低于千兆 / 未知——排查提示（不改定级）
        string? linkHint = LinkHealth.InvestigationHint(linkMbps);
        if (linkHint != null)
        {
            notes.Add(linkHint);
        }

        // 10. 慢时的建议
        if (grade is Grade.Fair or Grade.Poor)
        {
            if (protocol == "tcp" && p.Parallel < 4)
            {
                notes.Add("建议把并行流调到 4–8");
            }
            else if (linkMbps <= 0)
            {
                notes.Add("链路速率未知，建议插网线后再测");
            }
            else if (!LinkHealth.IsInvestigationTarget(linkMbps))
            {
                notes.Add("建议检查网线（超五类及以上）、交换机端口、网卡协商速率");
            }
        }

        // 11. 标题
        double mbps = slow / 1e6;
        double mbPerSec = mbps / 8;
        double minutes = 10 * 1024 / mbPerSec / 60;
        string title = $"每秒能传约 {mbPerSec:F0} MB，一个 10 GB 的文件约 {minutes:F1} 分钟传完";

        return new Verdict(grade, title, notes);

        void Cap(Grade worstAllowed)
        {
            if (worstAllowed > grade)
            {
                grade = worstAllowed;
            }
        }
    }

    private static IEnumerable<double> DirectionalSpeeds(Summary s, string direction) =>
        direction switch
        {
            Directions.Reverse => new[] { s.BA },
            Directions.Bidir => new[] { s.AB, s.BA },
            _ => new[] { s.AB },
        };
}

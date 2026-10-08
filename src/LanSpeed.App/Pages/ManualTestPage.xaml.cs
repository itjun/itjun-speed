using System.Collections.ObjectModel;
using LanSpeed.App.Services;
using LanSpeed.Core.Control;
using LanSpeed.Core.History;
using LanSpeed.Core.Iperf;
using LanSpeed.Core.Net;
using LanSpeed.Core.Orchestration;
using LanSpeed.Core.Verdict;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;

namespace LanSpeed.App.Pages;

/// <summary>
/// 两机测试页：从扫描结果选择或手动录入 A、B 两台机器发起单对测速，
/// 结果以 1Panel 风格报表呈现（结论横幅、双色大数字卡、指标卡、逐秒速率曲线）。
/// A 选「本机」→ 本机打流；A、B 都是远程主机 → 本机仅作为发起机调度（流量不经本机）。
/// </summary>
public sealed partial class ManualTestPage : Page
{
    private const string LocalTag = "local";

    private readonly ObservableCollection<string> _samples = [];
    private readonly List<(double T, double Ab, double Ba)> _chart = [];

    public ManualTestPage()
    {
        InitializeComponent();
        SampleList.ItemsSource = _samples;
        Loaded += (_, _) => RebuildHosts();
        AppServices.Current.ScanCompleted += OnScanCompleted;
    }

    private void OnScanCompleted()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(RebuildHosts);
            return;
        }
        RebuildHosts();
    }

    /// <summary>下拉项 = 本机 + 最近扫描中「已安装」的主机；保留用户已输入的文本。</summary>
    private void RebuildHosts()
    {
        if (HostACombo is null || HostBCombo is null)
        {
            return; // InitializeComponent 期间事件早触发
        }
        string keepA = HostACombo.Text, keepB = HostBCombo.Text;
        HostACombo.Items.Clear();
        HostBCombo.Items.Clear();
        var addrs = AppServices.Current.Node.Addrs().Where(a => !a.Ip.StartsWith("169.254.")).ToList();
        var local = addrs.FirstOrDefault();
        HostACombo.Items.Add(new ComboBoxItem { Content = $"本机（{local?.Ip ?? "本机"}）", Tag = LocalTag });
        if (local != null)
        {
            HostBCombo.Items.Add(new ComboBoxItem { Content = $"{local.Ip}（本机）", Tag = local.Ip });
        }
        var localIps = addrs.Select(a => a.Ip).ToHashSet();
        var seen = new HashSet<string>(localIps);
        foreach (var h in AppServices.Current.LastScan)
        {
            // 本机已单独列出，扫描发现里的本机条目跳过
            if (h.Hello is null || !h.PortOk || localIps.Contains(h.Ip) || !seen.Add(h.Ip))
            {
                continue;
            }
            long link = AppServices.HostLinkMbps(h);
            string linkTag = LinkHealth.IsInvestigationTarget(link) ? " · 低于千兆" : string.Empty;
            string label = $"{h.Ip}（{h.Hello.Name}{(h.Hello.Accept ? string.Empty : "，拒绝被测")}{linkTag}）";
            HostACombo.Items.Add(new ComboBoxItem { Content = label, Tag = h.Ip });
            HostBCombo.Items.Add(new ComboBoxItem { Content = label, Tag = h.Ip });
        }
        // 已配对但本次未扫到的主机也列出，便于重启后继续测
        foreach (var p in AppServices.Current.PairedHosts.List())
        {
            string ip = p.LastIps.FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrEmpty(ip) || !seen.Add(ip))
            {
                continue;
            }
            string linkTag = LinkHealth.IsInvestigationTarget(p.LinkMbps) ? " · 低于千兆" : string.Empty;
            string label = $"{ip}（{p.Name} · 已配对{linkTag}）";
            HostACombo.Items.Add(new ComboBoxItem { Content = label, Tag = ip });
            HostBCombo.Items.Add(new ComboBoxItem { Content = label, Tag = ip });
        }
        HostACombo.Text = keepA;
        HostBCombo.Text = keepB;
        HostHint.Text = HostACombo.Items.Count > 1
            ? $"已列出扫描与已配对主机；也可以直接输入任意 IP（跨网段可用）。"
            : "尚未扫描到已安装主机：先到「主机」页扫描并「记住」，或直接输入对端 IP。";
        UpdateLinkWarn();
    }

    private void OnHostSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HostAPort is null)
        {
            return;
        }
        // A 选「本机」时不走网络，端口输入无意义
        bool isLocal = (HostACombo.SelectedItem as ComboBoxItem)?.Tag as string == LocalTag;
        HostAPort.IsEnabled = !isLocal;
        UpdateLinkWarn();
    }

    /// <summary>任一侧低于千兆则显示警告条（不拦截开测）。</summary>
    private void UpdateLinkWarn()
    {
        if (LinkWarnBar is null)
        {
            return;
        }
        long a = ResolveLinkMbps(HostACombo);
        long b = ResolveLinkMbps(HostBCombo);
        var cls = LinkHealth.ClassifyPath(a, b);
        if (cls == LinkClass.BelowGigabit)
        {
            long path = a > 0 && b > 0 ? Math.Min(a, b) : Math.Max(a, b);
            LinkWarnBar.Severity = InfoBarSeverity.Warning;
            LinkWarnBar.Title = "疑似百兆链路";
            LinkWarnBar.Message = LinkHealth.InvestigationHint(path)
                ?? "所选主机协商低于千兆，应排查后对照测速结果。";
            LinkWarnBar.IsOpen = true;
        }
        else if (cls == LinkClass.Unknown
                 && !string.IsNullOrWhiteSpace(HostACombo.Text)
                 && !string.IsNullOrWhiteSpace(HostBCombo.Text))
        {
            LinkWarnBar.Severity = InfoBarSeverity.Informational;
            LinkWarnBar.Title = "链路未知";
            LinkWarnBar.Message = "未能读取协商速率，测速后请对照结果确认端口协商。";
            LinkWarnBar.IsOpen = true;
        }
        else
        {
            LinkWarnBar.IsOpen = false;
            LinkWarnBar.Severity = InfoBarSeverity.Warning;
            LinkWarnBar.Title = "疑似百兆链路";
        }
    }

    private static long ResolveLinkMbps(ComboBox combo)
    {
        string? ip = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (ip == LocalTag || string.IsNullOrEmpty(ip))
        {
            if (ip == LocalTag)
            {
                var speeds = AppServices.Current.Node.Addrs().Select(a => a.SpeedMbps).Where(s => s > 0).ToList();
                return speeds.Count > 0 ? speeds.Min() : 0;
            }
            string text = combo.Text.Trim();
            if (text.StartsWith("本机", StringComparison.Ordinal))
            {
                var speeds = AppServices.Current.Node.Addrs().Select(a => a.SpeedMbps).Where(s => s > 0).ToList();
                return speeds.Count > 0 ? speeds.Min() : 0;
            }
            ip = text.Contains('（') ? text.Split('（')[0].Trim() : text;
        }
        if (string.IsNullOrEmpty(ip))
        {
            return 0;
        }
        var host = AppServices.Current.LastScan.FirstOrDefault(h => h.Ip == ip);
        if (host != null)
        {
            return AppServices.HostLinkMbps(host);
        }
        var paired = AppServices.Current.PairedHosts.List().FirstOrDefault(p => p.LastIps.Contains(ip));
        return paired?.LinkMbps ?? 0;
    }

    private static bool IsValidIp(string s) =>
        System.Net.IPAddress.TryParse(s, out var a)
        && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
        && a.ToString() == s;

    /// <summary>解析下拉选择或手输文本：返回 (是否本机, IP, 显示名)。</summary>
    private static (bool IsLocal, string Ip, string Label) Resolve(ComboBox combo, out string? error)
    {
        if (combo.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            if (tag == LocalTag)
            {
                error = null;
                return (true, "本机", "本机");
            }
            if (IsValidIp(tag))
            {
                error = null;
                return (false, tag, tag);
            }
        }
        string text = combo.Text.Trim();
        if (text.Length > 0 && text.StartsWith("本机", StringComparison.Ordinal))
        {
            error = null;
            return (true, "本机", "本机");
        }
        // 手输形如 "192.168.1.5（xxx）" 时取括号前
        string ip = text.Split('（')[0].Trim();
        if (IsValidIp(ip))
        {
            error = null;
            return (false, ip, ip);
        }
        error = $"「{text}」不是有效的 IP 或本机选项";
        return (false, ip, text);
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        var (aLocal, aIp, aLabel) = Resolve(HostACombo, out var errA);
        var (bLocal, bIp, bLabel) = Resolve(HostBCombo, out var errB);
        if (errA != null || errB != null)
        {
            ResetReport();
            GradeBanner.Visibility = Visibility.Visible;
            ShowBanner("输入有误", (errA ?? errB)!, isBad: true, sub: "请从下拉选择，或输入点分 IPv4（例如 192.168.210.222）。");
            return;
        }
        if (!aLocal && aIp == bIp)
        {
            ResetReport();
            GradeBanner.Visibility = Visibility.Visible;
            ShowBanner("无法测试", "A、B 不能是同一台机器。", isBad: true);
            return;
        }
        int aPort = (int)Math.Clamp(HostAPort.Value, 1024, 65535);
        int bPort = (int)Math.Clamp(HostBPort.Value, 1024, 65535);

        var p = new Params
        {
            Direction = (Direction.SelectedItem as ComboBoxItem)?.Tag as string ?? "forward",
            Duration = (int)Math.Clamp(DurationBox.Value, 3, 300),
            Parallel = (int)Math.Clamp(ParallelBox.Value, 1, 64),
            Protocol = UdpCheck.IsChecked == true ? "udp" : "tcp",
            UDPBandwidthMbps = Math.Max(0, BandwidthBox.Value),
        };
        p.Normalize();
        // 方向与流量的对应：正向=A→B、反向=B→A、双向=两者（无流量方向在报表里显示 —）
        bool abLive = p.Direction is Directions.Forward or Directions.Bidir;
        bool baLive = p.Direction is Directions.Reverse or Directions.Bidir;

        StartButton.IsEnabled = false;
        RunRing.IsActive = true;
        _samples.Clear();
        _chart.Clear();
        ResetReport();
        AbTitle.Text = $"A→B（{aLabel} → {bLabel}）";
        BaTitle.Text = $"B→A（{bLabel} → {aLabel}）";
        AbSub.Text = "测速中…";
        BaSub.Text = "测速中…";
        ResultText.Text = $"正在测速：{aLabel} → {bLabel}（{(p.Protocol == "udp" ? "UDP" : "TCP")} {p.Duration}s × {p.Parallel} 流，方向 {DirText(p.Direction)}）…";

        try
        {
            var svc = AppServices.Current;
            // A 为本机时直调；否则 A 也是远程节点（本机只做发起机调度）
            using INodeClient nodeA = aLocal ? new LocalNodeClient(svc.Ops) : new HttpNodeClient(aIp, aPort);
            using var nodeB = new HttpNodeClient(bIp, bPort);

            var progress = new Progress<Sample>(s =>
            {
                if (!DispatcherQueue.HasThreadAccess)
                {
                    DispatcherQueue.TryEnqueue(() => OnSample(s, abLive, baLive));
                    return;
                }
                OnSample(s, abLive, baLive);
            });

            var runner = new PairRunner();
            var result = await runner.RunAsync(nodeA, nodeB, p, targetIp: bIp, progress);

            if (result.Status == "done" && result.Summary is { } sum)
            {
                var v = VerdictEvaluator.Evaluate(sum, p.Protocol, result.LinkMbps, p);
                AppendHistory(aLabel, bLabel, p, sum, v.GradeLabel, v.Title);
                try
                {
                    ShowBanner(v.GradeLabel, v.Title, grade: v.Grade,
                        sub: $"{aLabel} → {bLabel} · {(p.Protocol == "udp" ? "UDP" : "TCP")} {p.Duration}s × {p.Parallel} 流 · 方向 {DirText(p.Direction)}");
                    FillBigCard(AbCardBorder, AbValue, AbUnit, AbSub, sum.AB, sum.PeakAB, abLive);
                    FillBigCard(BaCardBorder, BaValue, BaUnit, BaSub, sum.BA, sum.PeakBA, baLive);
                    PeakAb.Text = abLive ? $"{sum.PeakAB / 1e6:F1} Mbps" : "—";
                    PeakBa.Text = baLive ? $"{sum.PeakBA / 1e6:F1} Mbps" : "—";
                    Retrans.Text = $"{sum.Retransmits} 次";
                    double retrRate = sum.Seconds > 0 ? sum.Retransmits / sum.Seconds : 0;
                    Retrans.Foreground = retrRate >= 500 ? BadBrush() : retrRate >= 50 ? WarnBrush() : BaBrush();
                    Rtt.Text = sum.RTTMs > 0 ? $"{sum.RTTMs:F1} ms" : "—";
                    UdpStat.Text = sum.JitterMs > 0 || sum.LostPct > 0 ? $"{sum.JitterMs:F2} ms / {sum.LostPct:F2}%" : "—";
                    LinkCap.Text = result.LinkMbps > 0 ? $"{result.LinkMbps} Mbps" : "未知";
                    ResultText.Text = v.Notes.Count > 0 ? "建议：" + string.Join("；", v.Notes) : string.Empty;
                    FillChart(abLive, baLive);
                }
                catch (Exception ex)
                {
                    Services.AppServices.HostsTrace($"报表填充异常: {ex}");
                    ResultText.Text = $"结果显示异常：{ex.Message}";
                }
            }
            else
            {
                ShowBanner("测速失败", $"{result.Status}：{result.Reason}", isBad: true,
                    sub: "请确认 A、B 两端都已运行本工具（CLI：serve；默认端口 39301），且防火墙已放行。");
                AbSub.Text = "失败";
                BaSub.Text = "失败";
                AppendHistory(aLabel, bLabel, p, null, result.Status, result.Reason);
            }
        }
        catch (Exception ex)
        {
            ShowBanner("测速失败", ex.Message, isBad: true, sub: "请确认对端已运行本工具节点（CLI：serve），端口与防火墙正确。");
            AbSub.Text = "失败";
            BaSub.Text = "失败";
        }
        finally
        {
            StartButton.IsEnabled = true;
            RunRing.IsActive = false;
        }
    }

    private void OnSample(Sample s, bool abLive, bool baLive)
    {
        _chart.Add((s.T, s.AB, s.BA));
        _samples.Add($"[{s.T:F1}s]{(s.Omitted ? "（忽略段）" : "")}"
            + $" A→B {(abLive ? $"{s.AB / 1e6,8:F1}" : "       —")} Mbps"
            + $" │ B→A {(baLive ? $"{s.BA / 1e6,8:F1}" : "       —")} Mbps");
    }

    // ── 报表填充 ─────────────────────────────────────────────

    private void ResetReport()
    {
        GradeBanner.Visibility = Visibility.Collapsed;
        foreach (var tb in new[] { AbValue, BaValue, PeakAb, PeakBa, Retrans, Rtt, UdpStat, LinkCap })
        {
            tb.Text = "—";
        }
        AbUnit.Text = BaUnit.Text = string.Empty;
        AbTitle.Text = "A→B";
        BaTitle.Text = "B→A";
        AbSub.Text = BaSub.Text = "尚未测速";
        ResultText.Text = string.Empty;
        RateChart.Series = [];
    }

    private void ShowBanner(string badge, string title, Grade? grade = null, bool isBad = false, string? sub = null)
    {
        GradeBanner.Visibility = Visibility.Visible;
        GradeText.Text = badge;
        GradeTitle.Text = title;
        GradeSub.Text = sub ?? string.Empty;
        Brush badgeBrush = isBad ? BadBrush()
            : grade switch
            {
                Grade.Great => BaBrush(),
                Grade.Good => AbBrush(),
                Grade.Fair => WarnBrush(),
                Grade.Poor => BadBrush(),
                _ => MutedBrush(),
            };
        GradeBadge.Background = badgeBrush;
        GradeBanner.Background = isBad ? SoftBadBrush() : grade switch
        {
            Grade.Great => BaSoftBrush(),
            Grade.Good => AbSoftBrush(),
            _ => SoftWarnBrush(),
        };
    }

    private void FillBigCard(Border card, TextBlock value, TextBlock unit, TextBlock sub, double bps, double peak, bool live)
    {
        if (live)
        {
            card.Opacity = 1;
            value.Text = $"{bps / 1e6:F1}";
            unit.Text = "Mbps";
            sub.Text = $"峰值 {peak / 1e6:F1} Mbps";
        }
        else
        {
            card.Opacity = 0.55;
            value.Text = "—";
            unit.Text = string.Empty;
            sub.Text = "单向测试无流量";
        }
    }

    // ── 速率曲线（LiveCharts2） ────────────────────────────────

    private void FillChart(bool abLive, bool baLive)
    {
        if (_chart.Count < 2)
        {
            return;
        }
        double interval = _chart.Count > 1 ? _chart[1].T - _chart[0].T : 1;
        RateChart.Series =
        [
            new LineSeries<double>
            {
                Values = abLive ? _chart.Select(s => Math.Round(s.Ab / 1e6, 1)).ToArray() : [],
                Name = "A→B Mbps",
                Stroke = new SolidColorPaint(new SKColor(0, 122, 204)) { StrokeThickness = 2 },
                Fill = null,
                GeometrySize = 5,
                LineSmoothness = 0.2,
            },
            new LineSeries<double>
            {
                Values = baLive ? _chart.Select(s => Math.Round(s.Ba / 1e6, 1)).ToArray() : [],
                Name = "B→A Mbps",
                Stroke = new SolidColorPaint(new SKColor(0, 180, 42)) { StrokeThickness = 2 },
                Fill = null,
                GeometrySize = 5,
                LineSmoothness = 0.2,
            },
        ];
        RateChart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Bottom;
        RateChart.LegendTextSize = 12;
        RateChart.XAxes = [new Axis { Labeler = v => $"{v * interval:F0}s", MinStep = 1 }];
        RateChart.YAxes = [new Axis { MinLimit = 0, Name = "Mbps" }];
    }

    private static string DirText(string direction) => direction switch
    {
        Directions.Forward => "正向",
        Directions.Reverse => "反向",
        Directions.Bidir => "双向",
        _ => direction,
    };

    // 画刷访问器（对应 XAML 资源键）
    private Brush AbBrush() => (Brush)Resources["AbBrush"];
    private Brush BaBrush() => (Brush)Resources["BaBrush"];
    private Brush AbSoftBrush() => (Brush)Resources["AbSoftBrush"];
    private Brush BaSoftBrush() => (Brush)Resources["BaSoftBrush"];
    private Brush WarnBrush() => (Brush)Resources["WarnBrush"];
    private Brush BadBrush() => (Brush)Resources["BadBrush"];
    private Brush MutedBrush() => (Brush)Resources["MutedBrush"];
    private static Brush SoftBadBrush() => new SolidColorBrush(Windows.UI.Color.FromArgb(28, 245, 34, 45));
    private static Brush SoftWarnBrush() => new SolidColorBrush(Windows.UI.Color.FromArgb(28, 250, 140, 22));

    private void AppendHistory(string a, string b, Params p, Summary? s, string grade, string title)
    {
        try
        {
            AppServices.Current.History.Append(new HistoryRecord(
                Guid.NewGuid().ToString("N"), DateTimeOffset.Now, "pair", p.Protocol, p.Duration, p.Parallel, p.Direction,
                [
                    new HistoryPair(a, b, s != null ? "done" : "failed",
                        s?.AB ?? 0, s?.BA ?? 0, s?.RTTMs ?? 0, s?.Retransmits ?? 0,
                        s?.JitterMs ?? 0, s?.LostPct ?? 0, grade, title),
                ]));
        }
        catch (IOException)
        {
        }
    }
}

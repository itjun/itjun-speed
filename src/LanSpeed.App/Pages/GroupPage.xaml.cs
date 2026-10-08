using System.Collections.ObjectModel;
using LanSpeed.App.Services;
using LanSpeed.Core.Iperf;
using LanSpeed.Core.Net;
using LanSpeed.Core.Orchestration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LanSpeed.App.Pages;

/// <summary>轮次行视图模型。</summary>
public sealed class RoundRow
{
    public required string Index { get; init; }

    public required string PairText { get; init; }

    public string SpeedText { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public string GradeText { get; set; } = "…";

    public Brush GradeBrush { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.Gray);
}

/// <summary>分组页（§7/§8）：成员选择、星形/矩阵、逐轮进度。</summary>
public sealed partial class GroupPage : Page
{
    private readonly ObservableCollection<RoundRow> _rounds = [];
    private readonly List<CheckBox> _memberChecks = [];
    private CancellationTokenSource? _cts;

    public GroupPage()
    {
        InitializeComponent();
        RoundList.ItemsSource = _rounds;
        Loaded += (_, _) => RebuildMembers();
        AppServices.Current.ScanCompleted += OnScanCompleted;
    }

    private void OnScanCompleted()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(RebuildMembers);
            return;
        }
        RebuildMembers();
    }

    /// <summary>成员 = 最近扫描中「已安装·可测」的主机 + 本机。</summary>
    private void RebuildMembers()
    {
        MembersPanel.Children.Clear();
        _memberChecks.Clear();
        CenterCombo.Items.Clear();

        var localAddrs = AppServices.Current.Node.Addrs().Select(a => a.Ip).ToHashSet();
        foreach (var ip in localAddrs.Where(ip => !ip.StartsWith("169.254")))
        {
            AddMember(ip, "本机", @checked: true);
            CenterCombo.Items.Add(ip);
        }
        foreach (var h in AppServices.Current.LastScan)
        {
            if (h.Hello is { Accept: true } && h.PortOk && !localAddrs.Contains(h.Ip))
            {
                AddMember(h.Ip, h.Hello.Name, @checked: false);
                CenterCombo.Items.Add(h.Ip);
            }
        }
        if (CenterCombo.Items.Count > 0)
        {
            CenterCombo.SelectedIndex = 0;
        }
    }

    private void AddMember(string ip, string label, bool @checked)
    {
        var check = new CheckBox { Content = $"{ip}（{label}）", Tag = ip, IsChecked = @checked };
        check.Click += (_, _) => UpdateHint();
        MembersPanel.Children.Add(check);
        _memberChecks.Add(check);
        UpdateHint();
    }

    private void UpdateHint()
    {
        var members = SelectedMembers();
        int n = members.Count;
        MemberHint.Text = n < 2
            ? $"已选 {n} 台，至少需要 2 台。先在「主机」页扫描并确认对方已安装。"
            : $"已选 {n} 台；星形 {(n - 1)} 轮双向，矩阵 {n * (n - 1)} 轮单向。";

        if (LinkWarnBar is null)
        {
            return;
        }
        bool hasProblem = members.Any(ip =>
        {
            var host = AppServices.Current.LastScan.FirstOrDefault(h => h.Ip == ip);
            if (host != null)
            {
                return LinkHealth.IsInvestigationTarget(AppServices.HostLinkMbps(host));
            }
            var local = AppServices.Current.Node.Addrs().FirstOrDefault(a => a.Ip == ip);
            return local != null && LinkHealth.IsInvestigationTarget(local.SpeedMbps);
        });
        LinkWarnBar.IsOpen = hasProblem;
    }

    private List<string> SelectedMembers() =>
        _memberChecks.Where(c => c.IsChecked == true && c.Tag is string)
            .Select(c => (string)c.Tag)
            .ToList();

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        var members = SelectedMembers();
        if (members.Count < 2)
        {
            RunSummary.Text = "至少选择 2 台成员。";
            return;
        }
        var mode = ModeStar.IsChecked == true ? GroupMode.Star : GroupMode.Mesh;
        string? center = ModeStar.IsChecked == true && CenterCombo.SelectedItem is string c ? c : null;

        var p = new Params
        {
            Duration = (int)Math.Clamp(DurationBox.Value, 3, 300),
            Parallel = (int)Math.Clamp(ParallelBox.Value, 1, 64),
            Protocol = UdpCheck.IsChecked == true ? "udp" : "tcp",
            UDPBandwidthMbps = Math.Max(0, BandwidthBox.Value),
        };

        var pairs = Pairing.Build(mode, members, center, p);
        p.Normalize();
        RunSummary.Text = $"{(mode == GroupMode.Star ? "星形" : "矩阵")} {pairs.Count} 轮，预计 {TimeSpan.FromSeconds(pairs.Count * (p.Duration + 3)):mm\\分ss\\秒}（可随时停止，已完成轮次保留）";
        _rounds.Clear();
        for (int i = 0; i < pairs.Count; i++)
        {
            _rounds.Add(new RoundRow
            {
                Index = $"{i + 1}/{pairs.Count}",
                PairText = $"{pairs[i].A} → {pairs[i].B}",
            });
        }

        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        RunRing.IsActive = true;
        _cts = new CancellationTokenSource();

        var svc = AppServices.Current;
        var groupRunner = new GroupRunner(svc.Ops);
        var progress = new Progress<RoundResult>(r => UpdateRound(r, p));
        try
        {
            var result = await groupRunner.RunAsync(mode, members, center, p, progress, _cts.Token);
            svc.SetGroupResult(result, p);
            RunSummary.Text = $"完成 {result.DoneCount}/{result.Rounds.Count} 轮，结果见「结果」页与「历史」页。";
        }
        catch (Exception ex)
        {
            RunSummary.Text = $"分组测速失败：{ex.Message}";
        }
        finally
        {
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            RunRing.IsActive = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void UpdateRound(RoundResult r, Params p)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => UpdateRound(r, p));
            return;
        }
        var row = _rounds.FirstOrDefault(x => x.PairText == $"{r.Pair.A} → {r.Pair.B}");
        if (row == null)
        {
            return;
        }
        if (r.Status == "done" && r.Summary is { } s)
        {
            var grade = GradeOf(s, p);
            row.SpeedText = $"A→B {s.AB / 1e6:F1} │ B→A {s.BA / 1e6:F1} Mbps";
            row.GradeText = grade.label;
            row.GradeBrush = grade.brush;
        }
        else
        {
            row.SpeedText = r.Status;
            row.Note = r.Reason;
            if (r.Status != "running")
            {
                row.GradeText = "失败";
                row.GradeBrush = new SolidColorBrush(Microsoft.UI.Colors.DarkRed);
            }
        }
        var index = _rounds.IndexOf(row);
        _rounds[index] = row; // 触发 UI 更新（替换实例）
    }

    // 慢方向定级（A.10 规则 2 的速度阈值简化版，与 VerdictEvaluator 主判定一致）
    private (string label, Brush brush) GradeOf(Summary s, Params p)
    {
        var speeds = p.Direction == Directions.Bidir
            ? new[] { s.AB, s.BA }.Where(v => v > 0)
            : [p.Direction == Directions.Reverse ? s.BA : s.AB];
        var slow = speeds.DefaultIfEmpty(0).Min();
        return slow switch
        {
            >= 900e6 => ("很快", new SolidColorBrush(Microsoft.UI.Colors.Green)),
            >= 500e6 => ("正常", new SolidColorBrush(Microsoft.UI.Colors.Teal)),
            >= 100e6 => ("偏慢", new SolidColorBrush(Microsoft.UI.Colors.DarkOrange)),
            _ => ("很差", new SolidColorBrush(Microsoft.UI.Colors.Red)),
        };
    }

    private void OnStop(object sender, RoutedEventArgs e) => _cts?.Cancel();
}

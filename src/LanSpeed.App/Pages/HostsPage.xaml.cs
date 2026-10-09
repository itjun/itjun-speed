using System.Collections.ObjectModel;
using LanSpeed.App.Services;
using LanSpeed.Core.Control;
using LanSpeed.Core.Iperf;
using LanSpeed.Core.Net;
using LanSpeed.Core.Orchestration;
using LanSpeed.Core.Scan;
using LanSpeed.Core.Verdict;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LanSpeed.App.Pages;

/// <summary>主机行视图模型（含链路健康与软配对状态）。</summary>
public sealed class HostRow
{
    public required string Status { get; init; }

    public required Brush StatusBrush { get; init; }

    public required string Ip { get; init; }

    public string Hostname => string.IsNullOrEmpty(HostnameValue) ? "—" : HostnameValue!;

    public string? HostnameValue { get; init; }

    public string NodeName => Hello is { } h ? h.Name : "—";

    public string Version => Hello is { } h ? $"{h.Version} / iperf {h.Iperf}" : "—";

    public HostEntry? Entry { get; init; }

    public NodeHello? Hello => Entry?.Hello;

    public long LinkMbps { get; init; }

    public LinkClass LinkClass { get; init; }

    public string LinkLabel => LinkClass switch
    {
        LinkClass.BelowGigabit => LinkMbps > 0 ? $"{LinkMbps}M" : "低于千兆",
        LinkClass.Unknown => "未知",
        _ => LinkMbps >= 1000 ? $"{LinkMbps}M" : "千兆+",
    };

    public Brush LinkBadgeBrush => LinkClass switch
    {
        LinkClass.BelowGigabit => new SolidColorBrush(Colors.OrangeRed),
        LinkClass.Unknown => new SolidColorBrush(Colors.Gray),
        _ => new SolidColorBrush(Colors.SeaGreen),
    };

    public Brush RowBrush => LinkClass == LinkClass.BelowGigabit
        ? new SolidColorBrush(Windows.UI.Color.FromArgb(40, 255, 140, 0))
        : new SolidColorBrush(Colors.Transparent);

    public bool IsInvestigation => LinkHealth.IsInvestigationTarget(LinkClass);

    public bool Remembered { get; set; }

    public string PairLabel => Remembered ? "已配对" : (Hello is null ? "—" : "未配对");

    /// <summary>本地分组名；空表示未归入机柜。</summary>
    public string? GroupName { get; init; }

    /// <summary>本机也可放进分组，即使没有点过「记住」。</summary>
    public bool IsLocalPlaceable { get; init; }

    public string GroupLabel => string.IsNullOrEmpty(GroupName)
        ? (Remembered || IsLocalPlaceable ? "未分组" : "—")
        : GroupName!;

    public Visibility GroupBadgeVisibility => string.IsNullOrEmpty(GroupName) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility GroupPlainVisibility => string.IsNullOrEmpty(GroupName) ? Visibility.Visible : Visibility.Collapsed;

    public Brush GroupBadgeBrush => new SolidColorBrush(Colors.SteelBlue);

    public string PairButtonText => Remembered ? "取消记住" : "记住";

    public Visibility PairVisibility => Hello is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility TestableVisibility => Entry?.Hello is { Accept: true } && Entry.PortOk
        ? Visibility.Visible
        : Visibility.Collapsed;
}

/// <summary>主机页：扫描、软配对、低于千兆排查标记（不拦截开测）。</summary>
public sealed partial class HostsPage : Page
{
    private readonly ObservableCollection<HostRow> _rows = [];
    private List<HostRow> _allRows = [];
    private static DateTime _lastAutoScan = DateTime.MinValue;
    private bool _pageReady;

    public HostsPage()
    {
        InitializeComponent();
        HostList.ItemsSource = _rows;
        if (AppServices.Current.LastScan.Count > 0)
        {
            FillRows(AppServices.Current.LastScan);
        }
        else
        {
            FillPairedOffline();
        }
        UpdateEmptyHint();
        Loaded += (_, _) =>
        {
            _pageReady = true;
            MaybeAutoScan();
        };
    }

    private void MaybeAutoScan()
    {
        if (AutoScanToggle is { IsOn: true } && (DateTime.UtcNow - _lastAutoScan).TotalSeconds > 10)
        {
            _lastAutoScan = DateTime.UtcNow;
            _ = RunScanAsync();
        }
    }

    private void OnAutoScanToggled(object sender, RoutedEventArgs e)
    {
        if (!_pageReady)
        {
            return;
        }
        if (AutoScanToggle.IsOn && (DateTime.UtcNow - _lastAutoScan).TotalSeconds > 10)
        {
            _lastAutoScan = DateTime.UtcNow;
            _ = RunScanAsync();
        }
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!_pageReady)
        {
            return;
        }
        ApplyFilter();
    }

    private async void OnScan(object sender, RoutedEventArgs e) => await RunScanAsync();

    private async Task RunScanAsync()
    {
        if (ScanButton is null || ScanRing is null || ScanSummary is null)
        {
            return;
        }
        ScanButton.IsEnabled = false;
        ScanRing.IsActive = true;
        ScanSummary.Text = "正在扫描（发现 + ping / ARP / 端口探测）…";
        try
        {
            var hosts = await AppServices.Current.ScanAsync();
            FillRows(hosts);
            int installed = hosts.Count(h => h.Hello != null);
            int problems = _allRows.Count(r => r.IsInvestigation);
            ScanSummary.Text = $"共 {hosts.Count} 台在线，{installed} 台已安装；其中 {problems} 台低于千兆（排查重点）";
        }
        catch (Exception ex)
        {
            ScanSummary.Text = $"扫描失败：{ex.Message}";
        }
        finally
        {
            ScanRing.IsActive = false;
            ScanButton.IsEnabled = true;
        }
    }

    private void FillPairedOffline()
    {
        // 重启后尚无扫描：先展示已配对主机（离线灰显）
        var paired = AppServices.Current.PairedHosts.List();
        if (paired.Count == 0)
        {
            return;
        }
        var groups = GroupNamesByNode();
        _allRows = paired.Select(p => new HostRow
        {
            Status = "已配对·离线",
            StatusBrush = new SolidColorBrush(Colors.Gray),
            Ip = p.LastIps.FirstOrDefault() ?? "—",
            HostnameValue = p.Name,
            LinkMbps = p.LinkMbps,
            LinkClass = LinkHealth.Classify(p.LinkMbps),
            Remembered = true,
            GroupName = groups.GetValueOrDefault(p.NodeId),
            Entry = null,
        }).ToList();
        ApplyFilter();
    }

    private void FillRows(IEnumerable<HostEntry> hosts)
    {
        var pairedIds = AppServices.Current.PairedHosts.List().Select(p => p.NodeId).ToHashSet();
        var groups = GroupNamesByNode();
        string localId = AppServices.Current.Node.Id;
        var list = new List<HostRow>();
        foreach (var h in hosts)
        {
            long link = AppServices.HostLinkMbps(h);
            var cls = LinkHealth.Classify(link);
            bool remembered = h.Hello != null && pairedIds.Contains(h.Hello.Id);
            string? nodeId = h.Hello?.Id;
            if (nodeId == null && AppServices.Current.Node.Addrs().Any(a => a.Ip == h.Ip))
            {
                nodeId = localId;
            }
            list.Add(new HostRow
            {
                Status = h.Status,
                StatusBrush = StatusColor(h.Status),
                Ip = h.Ip,
                HostnameValue = h.Hostname,
                Entry = h,
                LinkMbps = link,
                LinkClass = cls,
                Remembered = remembered,
                IsLocalPlaceable = nodeId == localId,
                GroupName = nodeId != null ? groups.GetValueOrDefault(nodeId) : null,
            });
        }
        // 已配对置顶，其次排查对象，再按状态
        _allRows = list
            .OrderByDescending(r => r.Remembered)
            .ThenByDescending(r => r.IsInvestigation)
            .ThenBy(r => r.Status)
            .ThenBy(r => r.Ip)
            .ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _rows.Clear();
        bool onlyProblems = ProblemsOnlyCheck is { IsChecked: true };
        foreach (var r in _allRows.Where(r => !onlyProblems || r.IsInvestigation))
        {
            _rows.Add(r);
        }
        UpdateEmptyHint();
    }

    private void UpdateEmptyHint() => EmptyHint.Visibility = _rows.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    /// <summary>节点 ID → 本地分组名。未归入机柜的不在表里。</summary>
    private static Dictionary<string, string> GroupNamesByNode()
    {
        var svc = AppServices.Current;
        var names = svc.Groups.ListGroups().ToDictionary(g => g.Id, g => g.Name);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in svc.Groups.ListMembers())
        {
            if (names.TryGetValue(member.GroupId, out string? name))
            {
                map[member.NodeId] = name;
            }
        }
        return map;
    }

    private static Brush StatusColor(string status) => status switch
    {
        "已安装·可测" => new SolidColorBrush(Colors.Green),
        "已安装·拒绝被测" or "已安装·忙" => new SolidColorBrush(Colors.DarkOrange),
        "已安装·控制端口不通" => new SolidColorBrush(Colors.Red),
        _ => new SolidColorBrush(Colors.Gray),
    };

    private void OnTogglePair(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: HostRow row } || row.Hello is null)
        {
            return;
        }
        var store = AppServices.Current.PairedHosts;
        bool next = !row.Remembered;
        if (next)
        {
            store.Remember(
                row.Hello.Id,
                row.Hello.Name,
                row.Hello.CtrlPort != 0 ? row.Hello.CtrlPort : 39301,
                row.Hello.Ips.Select(a => a.Ip),
                row.LinkMbps);
        }
        else
        {
            store.Forget(row.Hello.Id);
            AppServices.Current.Groups.RemoveMember(row.Hello.Id);
        }
        // 重建行（x:Bind 默认 OneTime，需新实例才能刷新配对文案）
        int idx = _allRows.IndexOf(row);
        if (idx >= 0 && row.Entry != null)
        {
            _allRows[idx] = new HostRow
            {
                Status = row.Status,
                StatusBrush = row.StatusBrush,
                Ip = row.Ip,
                HostnameValue = row.HostnameValue,
                Entry = row.Entry,
                LinkMbps = row.LinkMbps,
                LinkClass = row.LinkClass,
                Remembered = next,
                IsLocalPlaceable = row.IsLocalPlaceable,
                GroupName = next ? row.GroupName : null,
            };
        }
        _allRows = _allRows
            .OrderByDescending(r => r.Remembered)
            .ThenByDescending(r => r.IsInvestigation)
            .ThenBy(r => r.Status)
            .ThenBy(r => r.Ip)
            .ToList();
        ApplyFilter();
    }

    private async void OnQuickTest(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string ip })
        {
            return;
        }
        var p = new Params { Duration = 5, Parallel = 4 };
        await RunTestAsync(ip, (int)AppServices.Current.Node.CtrlPort, p, (Button)sender);
    }

    private async Task RunTestAsync(string ip, int ctrlPort, Params p, Button? trigger)
    {
        if (trigger != null)
        {
            trigger.IsEnabled = false;
            trigger.Content = "测速中…";
        }
        try
        {
            var svc = AppServices.Current;
            using var nodeA = new LocalNodeClient(svc.Ops);
            using var nodeB = new HttpNodeClient(ip, ctrlPort);
            var runner = new PairRunner();
            var result = await runner.RunAsync(nodeA, nodeB, p, targetIp: ip);

            string title, text;
            if (result.Status == "done" && result.Summary is { } s)
            {
                var v = VerdictEvaluator.Evaluate(s, p.Protocol, result.LinkMbps, p);
                bool abLive = p.Direction is Directions.Forward or Directions.Bidir;
                bool baLive = p.Direction is Directions.Reverse or Directions.Bidir;
                title = $"与 {ip} 的测速结果";
                text = $"{(p.Protocol == "udp" ? "UDP" : "TCP")} {p.Duration}s × {p.Parallel} 流\n\n"
                     + $"A→B {(abLive ? $"{s.AB / 1e6:F1} Mbps" : "—")} │ B→A {(baLive ? $"{s.BA / 1e6:F1} Mbps" : "—")}\n"
                     + $"结论：{v.GradeLabel} —— {v.Title}";
                foreach (var note in v.Notes)
                {
                    text += $"\n · {note}";
                }
            }
            else
            {
                title = "测速失败";
                text = $"状态：{result.Status}\n原因：{result.Reason}";
            }
            await ShowDialogAsync(title, text);
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("测速失败", ex.Message);
        }
        finally
        {
            if (trigger != null)
            {
                trigger.IsEnabled = true;
                trigger.Content = "测速";
            }
        }
    }

    private async Task ShowDialogAsync(string title, string text)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = text,
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        _ = await dialog.ShowAsync();
    }
}

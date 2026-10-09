using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LanSpeed.App.Services;
using LanSpeed.Core.Control;
using LanSpeed.Core.Iperf;
using LanSpeed.Core.Net;
using LanSpeed.Core.Orchestration;
using LanSpeed.Core.Persistence;
using LanSpeed.Core.Scan;
using Microsoft.UI;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

    public Brush GradeBrush { get; set; } = new SolidColorBrush(Colors.Gray);
}

/// <summary>机柜里的一台主机（已记住，或本机）。</summary>
public sealed class CabinetHost
{
    public required string NodeId { get; init; }

    public required string Name { get; init; }

    public required string Ip { get; init; }

    public required bool Online { get; init; }

    public required bool Testable { get; init; }

    public required long LinkMbps { get; init; }

    public string Detail => Online ? (Ip.Length > 0 ? Ip : "在线") : (Ip.Length > 0 ? $"{Ip} 离线" : "离线");

    public Brush NameBrush => Online
        ? ThemeBrush("TextFillColorPrimaryBrush", Colors.Black)
        : ThemeBrush("TextFillColorDisabledBrush", Colors.Gray);

    internal static Brush ThemeBrush(string key, Color fallback)
    {
        if (Application.Current?.Resources.TryGetValue(key, out object? value) == true && value is Brush brush)
        {
            return brush;
        }
        return new SolidColorBrush(fallback);
    }
}

/// <summary>一个分组机柜。「未分组」的 GroupId 为空，不入库。</summary>
public sealed class Cabinet : INotifyPropertyChanged
{
    private bool _selected;

    public string? GroupId { get; init; }

    public required string Name { get; init; }

    public bool AllowReorder { get; init; }

    public List<CabinetHost> AllHosts { get; } = [];

    public ObservableCollection<CabinetHost> Hosts { get; } = [];

    public bool IsUngrouped => GroupId is null;

    public Visibility NamedVisibility => IsUngrouped ? Visibility.Collapsed : Visibility.Visible;

    public Visibility UngroupedVisibility => IsUngrouped ? Visibility.Visible : Visibility.Collapsed;

    public string Title => $"{Name}  {Hosts.Count}";

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }
            _selected = value;
            OnPropertyChanged(nameof(FrameBrush));
        }
    }

    public Brush FrameBrush => Selected
        ? CabinetHost.ThemeBrush("AccentFillColorDefaultBrush", Colors.DodgerBlue)
        : CabinetHost.ThemeBrush("CardStrokeColorDefaultBrush", Colors.Gray);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyTitle() => OnPropertyChanged(nameof(Title));

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>分组页（§7/§8）：本地机柜拖动调整，对选中的一柜做星形 / 矩阵测速。</summary>
public sealed partial class GroupPage : Page
{
    private readonly ObservableCollection<RoundRow> _rounds = [];
    private readonly ObservableCollection<Cabinet> _cabinets = [];
    private CancellationTokenSource? _cts;
    private bool _ready;
    private bool _filtering;
    private string? _dragNodeId;
    private string? _dragGroupId;
    private Cabinet? _selected;

    public GroupPage()
    {
        InitializeComponent();
        RoundList.ItemsSource = _rounds;
        CabinetRepeater.ItemsSource = _cabinets;
        Loaded += (_, _) =>
        {
            _ready = true;
            Rebuild();
        };
        Unloaded += (_, _) => AppServices.Current.ScanCompleted -= OnScanCompleted;
        AppServices.Current.ScanCompleted += OnScanCompleted;
    }

    private void OnScanCompleted()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(Rebuild);
            return;
        }
        Rebuild();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready)
        {
            Rebuild();
        }
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (_ready)
        {
            RefreshTestTargets();
        }
    }

    /// <summary>用已记住主机 + 本机重建机柜。搜索只影响柜内可见行，不改库里的顺序。</summary>
    private void Rebuild()
    {
        if (CabinetRepeater is null || GroupStats is null)
        {
            return;
        }
        string? keepId = _selected is { IsUngrouped: false } ? _selected.GroupId : null;
        bool keepUngrouped = _selected?.IsUngrouped == true;
        string query = SearchBox?.Text?.Trim() ?? string.Empty;
        _filtering = query.Length > 0;

        var svc = AppServices.Current;
        var groups = svc.Groups.ListGroups();
        var members = svc.Groups.ListMembers().ToList();
        var paired = svc.PairedHosts.List();
        string localId = svc.Node.Id;
        var known = paired.Select(p => p.NodeId).Append(localId).ToHashSet();
        foreach (var stale in members.Where(m => !known.Contains(m.NodeId)))
        {
            svc.Groups.RemoveMember(stale.NodeId);
        }
        members.RemoveAll(m => !known.Contains(m.NodeId));

        var hosts = new Dictionary<string, CabinetHost>(StringComparer.Ordinal);
        hosts[localId] = DescribeLocal(svc.Node);
        foreach (var p in paired)
        {
            if (p.NodeId == localId)
            {
                continue;
            }
            hosts[p.NodeId] = DescribePaired(p, svc.LastScan);
        }

        var next = new List<Cabinet>();
        var ungrouped = new Cabinet { Name = "未分组", AllowReorder = false };
        next.Add(ungrouped);
        var byId = new Dictionary<string, Cabinet>(StringComparer.Ordinal);
        foreach (var g in groups)
        {
            var cab = new Cabinet { GroupId = g.Id, Name = g.Name, AllowReorder = !_filtering };
            next.Add(cab);
            byId[g.Id] = cab;
        }

        var buckets = new Dictionary<Cabinet, List<(int Order, CabinetHost Host)>>();
        foreach (var cab in next)
        {
            buckets[cab] = [];
        }
        foreach (var (nodeId, host) in hosts)
        {
            if (!Match(host, query))
            {
                continue;
            }
            var member = members.FirstOrDefault(m => m.NodeId == nodeId);
            var cab = member != null && byId.TryGetValue(member.GroupId, out var named) ? named : ungrouped;
            int order = member?.SortOrder ?? 0;
            buckets[cab].Add((order, host));
            cab.AllHosts.Add(host);
        }
        foreach (var cab in next)
        {
            IEnumerable<CabinetHost> ordered = cab.IsUngrouped
                ? buckets[cab].Select(x => x.Host).OrderBy(h => h.NodeId == localId ? 0 : 1).ThenBy(h => h.Name, StringComparer.Ordinal)
                : buckets[cab].OrderBy(x => x.Order).Select(x => x.Host);
            if (!cab.IsUngrouped)
            {
                cab.AllHosts.Sort((a, b) =>
                {
                    int oa = members.First(m => m.NodeId == a.NodeId).SortOrder;
                    int ob = members.First(m => m.NodeId == b.NodeId).SortOrder;
                    return oa.CompareTo(ob);
                });
            }
            foreach (var host in ordered)
            {
                cab.Hosts.Add(host);
            }
        }

        // 未过滤时 AllHosts 与可见行一致；过滤时 AllHosts 仍要包含被藏起来的主机，测速和回写顺序才完整
        if (_filtering)
        {
            foreach (var cab in next)
            {
                cab.AllHosts.Clear();
            }
            foreach (var (nodeId, host) in hosts)
            {
                var member = members.FirstOrDefault(m => m.NodeId == nodeId);
                var cab = member != null && byId.TryGetValue(member.GroupId, out var named) ? named : ungrouped;
                cab.AllHosts.Add(host);
            }
            foreach (var cab in next.Where(c => !c.IsUngrouped))
            {
                cab.AllHosts.Sort((a, b) =>
                {
                    int oa = members.First(m => m.NodeId == a.NodeId).SortOrder;
                    int ob = members.First(m => m.NodeId == b.NodeId).SortOrder;
                    return oa.CompareTo(ob);
                });
            }
        }
        else
        {
            foreach (var cab in next)
            {
                cab.AllHosts.Clear();
                cab.AllHosts.AddRange(cab.Hosts);
            }
        }

        _cabinets.Clear();
        foreach (var cab in next)
        {
            _cabinets.Add(cab);
        }
        GroupStats.Text = $"已记住 {paired.Count} 台，{groups.Count} 个分组";

        _selected = keepUngrouped
            ? ungrouped
            : next.FirstOrDefault(c => c.GroupId == keepId)
              ?? next.FirstOrDefault(c => !c.IsUngrouped)
              ?? ungrouped;
        foreach (var cab in _cabinets)
        {
            cab.Selected = cab == _selected;
        }
        RefreshTestTargets();
    }

    private static bool Match(CabinetHost host, string query)
    {
        if (query.Length == 0)
        {
            return true;
        }
        return host.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
               || host.Ip.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static CabinetHost DescribeLocal(LocalNode node)
    {
        var addrs = node.Addrs().Where(a => !a.Ip.StartsWith("169.254.", StringComparison.Ordinal)).ToList();
        string ip = addrs.FirstOrDefault()?.Ip ?? string.Empty;
        var speeds = addrs.Select(a => a.SpeedMbps).Where(s => s > 0).ToList();
        long link = speeds.Count > 0 ? speeds.Min() : 0;
        return new CabinetHost
        {
            NodeId = node.Id,
            Name = $"本机（{node.Name}）",
            Ip = ip,
            Online = true,
            Testable = ip.Length > 0 && node.Accept,
            LinkMbps = link,
        };
    }

    private static CabinetHost DescribePaired(PairedHost paired, IReadOnlyList<HostEntry> scan)
    {
        var seen = scan.FirstOrDefault(h => h.Hello?.Id == paired.NodeId);
        if (seen?.Hello is { Accept: true } && seen.PortOk)
        {
            return new CabinetHost
            {
                NodeId = paired.NodeId,
                Name = seen.Hello.Name.Length > 0 ? seen.Hello.Name : paired.Name,
                Ip = seen.Ip,
                Online = true,
                Testable = true,
                LinkMbps = AppServices.HostLinkMbps(seen),
            };
        }
        if (seen != null)
        {
            return new CabinetHost
            {
                NodeId = paired.NodeId,
                Name = seen.Hello?.Name ?? paired.Name,
                Ip = seen.Ip,
                Online = true,
                Testable = false,
                LinkMbps = seen.Hello != null ? AppServices.HostLinkMbps(seen) : paired.LinkMbps,
            };
        }
        return new CabinetHost
        {
            NodeId = paired.NodeId,
            Name = paired.Name,
            Ip = paired.LastIps.FirstOrDefault() ?? string.Empty,
            Online = false,
            Testable = false,
            LinkMbps = paired.LinkMbps,
        };
    }

    private void RefreshTestTargets()
    {
        if (MemberHint is null || CenterCombo is null || LinkWarnBar is null || _selected is null)
        {
            return;
        }
        bool star = ModeStar.IsChecked == true;
        CenterCombo.IsEnabled = star;
        var targets = _selected.AllHosts.Where(h => h.Testable && h.Ip.Length > 0).ToList();
        string? previous = CenterCombo.SelectedItem is ComboBoxItem { Tag: string ip } ? ip : null;
        CenterCombo.Items.Clear();
        foreach (var t in targets)
        {
            var item = new ComboBoxItem { Content = $"{t.Ip}（{t.Name}）", Tag = t.Ip };
            CenterCombo.Items.Add(item);
            if (t.Ip == previous)
            {
                CenterCombo.SelectedItem = item;
            }
        }
        if (CenterCombo.SelectedItem is null && CenterCombo.Items.Count > 0)
        {
            CenterCombo.SelectedIndex = 0;
        }

        int n = targets.Count;
        int offline = _selected.AllHosts.Count(h => !h.Online);
        string body = n < 2
            ? "在线可测不足 2 台。先在「主机」页记住对方，并确认已安装且允许被测。"
            : $"在线可测 {n} 台；星形 {n - 1} 轮双向，矩阵 {n * (n - 1)} 轮单向。";
        string extra = offline > 0 ? $"离线 {offline} 台不参与。" : string.Empty;
        MemberHint.Text = $"「{_selected.Name}」{body}{extra}";
        LinkWarnBar.IsOpen = targets.Any(t => LinkHealth.IsInvestigationTarget(t.LinkMbps));
    }

    private void OnCabinetTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Cabinet cab })
        {
            Select(cab);
        }
    }

    private void Select(Cabinet cab)
    {
        _selected = cab;
        foreach (var item in _cabinets)
        {
            item.Selected = item == cab;
        }
        RefreshTestTargets();
    }

    private async void OnCreateGroup(object sender, RoutedEventArgs e)
    {
        var box = new TextBox { PlaceholderText = "例如 3楼-财务室" };
        if (await ConfirmAsync("新建分组", box, "创建") != ContentDialogResult.Primary)
        {
            return;
        }
        try
        {
            var created = AppServices.Current.Groups.Create(box.Text);
            Rebuild();
            var cab = _cabinets.FirstOrDefault(c => c.GroupId == created.Id);
            if (cab != null)
            {
                Select(cab);
            }
        }
        catch (ArgumentException ex)
        {
            await AlertAsync(ex.Message);
        }
    }

    private async void OnRename(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Cabinet { GroupId: { } id } cab })
        {
            return;
        }
        var box = new TextBox { Text = cab.Name };
        if (await ConfirmAsync("改名", box, "保存") != ContentDialogResult.Primary)
        {
            return;
        }
        try
        {
            AppServices.Current.Groups.Rename(id, box.Text);
            Rebuild();
        }
        catch (ArgumentException ex)
        {
            await AlertAsync(ex.Message);
        }
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Cabinet { GroupId: { } id, Name: var name } })
        {
            return;
        }
        var dialog = new ContentDialog
        {
            Title = "删除分组",
            Content = $"删除「{name}」后，里面的主机回到未分组。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }
        AppServices.Current.Groups.Delete(id);
        if (_selected?.GroupId == id)
        {
            _selected = null;
        }
        Rebuild();
    }

    private void OnCabinetDragStarting(object sender, DragStartingEventArgs e)
    {
        if (_filtering || sender is not FrameworkElement { Tag: Cabinet { GroupId: { } id } })
        {
            e.Cancel = true;
            return;
        }
        _dragNodeId = null;
        _dragGroupId = id;
        e.Data.SetText("group:" + id);
    }

    private void OnCabinetDragOver(object sender, DragEventArgs e)
    {
        bool groupOk = _dragGroupId != null
                       && sender is FrameworkElement { Tag: Cabinet { IsUngrouped: false } };
        bool hostOk = _dragNodeId != null && !_filtering;
        e.AcceptedOperation = groupOk || hostOk ? DataPackageOperation.Move : DataPackageOperation.None;
        e.Handled = true;
    }

    private void OnCabinetDrop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Cabinet target })
        {
            return;
        }
        if (_dragGroupId is { } gid && target.GroupId is not null && gid != target.GroupId)
        {
            _dragGroupId = null;
            ReorderCabinets(gid, target.GroupId);
            e.Handled = true;
            return;
        }
        if (_dragNodeId is { } nodeId && !_filtering)
        {
            MoveNode(nodeId, target);
            e.Handled = true;
        }
    }

    private void OnHostDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (sender is ListView { Tag: Cabinet cab })
        {
            Select(cab);
        }
        if (_filtering || e.Items.FirstOrDefault() is not CabinetHost host)
        {
            e.Cancel = true;
            return;
        }
        _dragGroupId = null;
        _dragNodeId = host.NodeId;
        e.Data.SetText(host.NodeId);
    }

    private void OnHostDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = _dragNodeId != null && !_filtering
            ? DataPackageOperation.Move
            : DataPackageOperation.None;
        e.Handled = true;
    }

    private void OnHostDrop(object sender, DragEventArgs e)
    {
        if (_filtering || _dragNodeId is not { } nodeId || sender is not ListView { Tag: Cabinet target })
        {
            return;
        }
        var source = FindByNode(nodeId);
        if (source == null || source == target)
        {
            return;
        }
        MoveNode(nodeId, target);
        e.Handled = true;
    }

    private void OnHostDragCompleted(object sender, DragItemsCompletedEventArgs e)
    {
        // 跨柜搬移已在 Drop 里写库并清掉 _dragNodeId。这里只处理同一柜内的重排。
        if (!_filtering
            && _dragNodeId is not null
            && sender is ListView { Tag: Cabinet { GroupId: { } gid } cab }
            && FindByNode(_dragNodeId) == cab)
        {
            cab.AllHosts.Clear();
            cab.AllHosts.AddRange(cab.Hosts);
            PersistOrder(cab, gid);
        }
        _dragNodeId = null;
        _dragGroupId = null;
    }

    private void MoveNode(string nodeId, Cabinet target)
    {
        var source = FindByNode(nodeId);
        if (source == null || source == target)
        {
            return;
        }
        var host = source.AllHosts.FirstOrDefault(h => h.NodeId == nodeId);
        if (host == null)
        {
            return;
        }
        source.AllHosts.Remove(host);
        source.Hosts.Remove(host);
        target.AllHosts.Add(host);
        if (Match(host, SearchBox?.Text?.Trim() ?? string.Empty))
        {
            target.Hosts.Add(host);
        }
        if (target.GroupId is null)
        {
            AppServices.Current.Groups.RemoveMember(nodeId);
        }
        else
        {
            AppServices.Current.Groups.MoveMember(nodeId, target.GroupId);
            PersistOrder(target, target.GroupId);
        }
        if (source.GroupId is not null)
        {
            PersistOrder(source, source.GroupId);
        }
        source.NotifyTitle();
        target.NotifyTitle();
        _dragNodeId = null;
        RefreshTestTargets();
    }

    private static void PersistOrder(Cabinet cab, string groupId)
    {
        AppServices.Current.Groups.ReorderMembers(groupId, cab.AllHosts.Select(h => h.NodeId).ToList());
    }

    private void ReorderCabinets(string draggedId, string targetId)
    {
        var ids = _cabinets.Where(c => c.GroupId != null).Select(c => c.GroupId!).ToList();
        ids.Remove(draggedId);
        int index = ids.IndexOf(targetId);
        if (index < 0)
        {
            ids.Add(draggedId);
        }
        else
        {
            ids.Insert(index, draggedId);
        }
        AppServices.Current.Groups.ReorderGroups(ids);
        Rebuild();
    }

    private Cabinet? FindByNode(string nodeId) =>
        _cabinets.FirstOrDefault(c => c.AllHosts.Any(h => h.NodeId == nodeId));

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            RunSummary.Text = "先点选一个分组。";
            return;
        }
        var targets = _selected.AllHosts.Where(h => h.Testable && h.Ip.Length > 0).ToList();
        var members = targets.Select(t => t.Ip).Distinct().ToList();
        if (members.Count < 2)
        {
            RunSummary.Text = $"「{_selected.Name}」在线可测不足 2 台。";
            return;
        }
        var mode = ModeStar.IsChecked == true ? GroupMode.Star : GroupMode.Mesh;
        string? center = ModeStar.IsChecked == true && CenterCombo.SelectedItem is ComboBoxItem { Tag: string c } ? c : null;

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
                row.GradeBrush = new SolidColorBrush(Colors.DarkRed);
            }
        }
        var index = _rounds.IndexOf(row);
        _rounds[index] = row;
    }

    private (string label, Brush brush) GradeOf(Summary s, Params p)
    {
        var speeds = p.Direction == Directions.Bidir
            ? new[] { s.AB, s.BA }.Where(v => v > 0)
            : [p.Direction == Directions.Reverse ? s.BA : s.AB];
        var slow = speeds.DefaultIfEmpty(0).Min();
        return slow switch
        {
            >= 900e6 => ("很快", new SolidColorBrush(Colors.Green)),
            >= 500e6 => ("正常", new SolidColorBrush(Colors.Teal)),
            >= 100e6 => ("偏慢", new SolidColorBrush(Colors.DarkOrange)),
            _ => ("很差", new SolidColorBrush(Colors.Red)),
        };
    }

    private void OnStop(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private async Task<ContentDialogResult> ConfirmAsync(string title, TextBox box, string primary)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = box,
            PrimaryButtonText = primary,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowAsync();
    }

    private async Task AlertAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "分组",
            Content = message,
            CloseButtonText = "关闭",
            XamlRoot = XamlRoot,
        };
        _ = await dialog.ShowAsync();
    }
}

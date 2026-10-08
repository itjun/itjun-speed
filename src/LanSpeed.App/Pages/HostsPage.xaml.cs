using System.Collections.ObjectModel;
using LanSpeed.App.Services;
using LanSpeed.Core.Control;
using LanSpeed.Core.Iperf;
using LanSpeed.Core.Orchestration;
using LanSpeed.Core.Verdict;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LanSpeed.App.Pages;

/// <summary>主机行视图模型。</summary>
public sealed class HostRow
{
    public required string Status { get; init; }

    public required Brush StatusBrush { get; init; }

    public required string Ip { get; init; }

    public string Hostname => string.IsNullOrEmpty(HostnameValue) ? "—" : HostnameValue!;

    public string? HostnameValue { get; init; }

    public string Mac => MacValue is { Length: > 0 } m ? m : "—";

    public string? MacValue { get; init; }

    public string NodeName => Hello is { } h ? h.Name : "—";

    public string Version => Hello is { } h ? $"{h.Version} / iperf {h.Iperf}" : "—";

    public Core.Scan.HostEntry? Entry { get; init; }

    public NodeHello? Hello => Entry?.Hello;

    public Visibility TestableVisibility => Entry?.Hello is { Accept: true } && Entry.PortOk
        ? Visibility.Visible
        : Visibility.Collapsed;
}

/// <summary>主机页（§8）：自动扫描、主机表、对已安装主机快速测速（本机 → 该机）。</summary>
public sealed partial class HostsPage : Page
{
    private readonly ObservableCollection<HostRow> _rows = [];
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
        UpdateEmptyHint();
        // Loaded 在全部控件创建完成后触发，此后才允许扫描（XAML 里 IsOn/IsChecked 默认值会在
        // InitializeComponent 中途触发 Toggled 事件，此时后声明的控件字段还是 null）
        Loaded += (_, _) =>
        {
            _pageReady = true;
            MaybeAutoScan();
        };
    }

    /// <summary>打开本页自动扫描一次（10 秒内不重复，避免频繁导航反复扫）。</summary>
    private void MaybeAutoScan()
    {
        DebugLog($"MaybeAutoScan: toggleNull={AutoScanToggle is null} isOn={AutoScanToggle?.IsOn} sinceLast={(DateTime.UtcNow - _lastAutoScan).TotalSeconds:F0}s");
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
            return; // InitializeComponent 期间 IsOn=True 触发的事件，忽略
        }
        DebugLog($"OnAutoScanToggled: isOn={AutoScanToggle.IsOn}");
        if (AutoScanToggle.IsOn && (DateTime.UtcNow - _lastAutoScan).TotalSeconds > 10)
        {
            _lastAutoScan = DateTime.UtcNow;
            _ = RunScanAsync();
        }
    }

    private static void DebugLog(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanSpeed", "ui-debug.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch (IOException)
        {
        }
    }

    private async void OnScan(object sender, RoutedEventArgs e) => await RunScanAsync();

    private async Task RunScanAsync()
    {
        if (ScanButton is null || ScanRing is null || ScanSummary is null)
        {
            return; // 页面尚未初始化完成
        }
        DebugLog("RunScanAsync: start");
        ScanButton.IsEnabled = false;
        ScanRing.IsActive = true;
        ScanSummary.Text = "正在扫描（发现 + ping / ARP / 端口探测）…";
        try
        {
            var hosts = await AppServices.Current.ScanAsync();
            FillRows(hosts);
            int installed = hosts.Count(h => h.Hello != null);
            ScanSummary.Text = $"共 {hosts.Count} 台在线，其中 {installed} 台已安装本工具";
            DebugLog($"RunScanAsync: done {hosts.Count} hosts, {installed} installed");
        }
        catch (Exception ex)
        {
            ScanSummary.Text = $"扫描失败：{ex.Message}";
            DebugLog($"RunScanAsync: failed {ex}");
        }
        finally
        {
            ScanRing.IsActive = false;
            ScanButton.IsEnabled = true;
        }
    }

    private void FillRows(IEnumerable<Core.Scan.HostEntry> hosts)
    {
        _rows.Clear();
        foreach (var h in hosts)
        {
            _rows.Add(new HostRow
            {
                Status = h.Status,
                StatusBrush = StatusColor(h.Status),
                Ip = h.Ip,
                HostnameValue = h.Hostname,
                MacValue = h.Mac,
                Entry = h,
            });
        }
        UpdateEmptyHint();
    }

    private void UpdateEmptyHint() => EmptyHint.Visibility = _rows.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    private static Brush StatusColor(string status) => status switch
    {
        "已安装·可测" => new SolidColorBrush(Microsoft.UI.Colors.Green),
        "已安装·拒绝被测" or "已安装·忙" => new SolidColorBrush(Microsoft.UI.Colors.DarkOrange),
        "已安装·控制端口不通" => new SolidColorBrush(Microsoft.UI.Colors.Red),
        _ => new SolidColorBrush(Microsoft.UI.Colors.Gray),
    };

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
                text = $"{(p.Protocol == "udp" ? "UDP" : "TCP")} {p.Duration}s × {p.Parallel} 流，方向 {(p.Direction == Directions.Reverse ? "反向" : p.Direction == Directions.Bidir ? "双向" : "正向")}\n\n"
                     + $"A→B（本机→对端）{(abLive ? $"{s.AB / 1e6:F1} Mbps" : "—（单向无流量）")}（峰值 {(abLive ? $"{s.PeakAB / 1e6:F1}" : "—")}）\n"
                     + $"B→A（对端→本机）{(baLive ? $"{s.BA / 1e6:F1} Mbps" : "—（单向无流量）")}（峰值 {(baLive ? $"{s.PeakBA / 1e6:F1}" : "—")}）\n"
                     + $"重传 {s.Retransmits} 次 │ RTT {(s.RTTMs > 0 ? $"{s.RTTMs:F1} ms" : "—")}"
                     + (s.JitterMs > 0 ? $" │ 抖动 {s.JitterMs:F2} ms" : string.Empty)
                     + (s.LostPct > 0 ? $" │ 丢包 {s.LostPct:F2}%" : string.Empty) + "\n\n"
                     + $"结论：{v.GradeLabel} —— {v.Title}";
                foreach (var note in v.Notes)
                {
                    text += $"\n · {note}";
                }
            }
            else
            {
                title = "测速失败";
                text = $"状态：{result.Status}\n原因：{result.Reason}\n\n请确认对端已运行本工具（CLI：serve 命令；默认端口 {ctrlPort}），且防火墙已放行。";
            }
            await ShowDialogAsync(title, text);
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("测速失败", $"{ex.Message}\n\n请确认对端已运行本工具节点（CLI：serve 命令）。");
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

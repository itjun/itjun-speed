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

/// <summary>主机页（§8）：扫描、主机表、对已安装主机快速单对测速。</summary>
public sealed partial class HostsPage : Page
{
    private readonly ObservableCollection<HostRow> _rows = [];

    public HostsPage()
    {
        InitializeComponent();
        HostList.ItemsSource = _rows;
        if (AppServices.Current.LastScan.Count > 0)
        {
            FillRows(AppServices.Current.LastScan);
        }
        UpdateEmptyHint();
    }

    private async void OnScan(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        ScanRing.IsActive = true;
        ScanSummary.Text = "正在扫描（发现 + ping / ARP / 端口探测）…";
        try
        {
            var hosts = await AppServices.Current.ScanAsync();
            FillRows(hosts);
            int installed = hosts.Count(h => h.Hello != null);
            ScanSummary.Text = $"共 {hosts.Count} 台在线，其中 {installed} 台已安装本工具";
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
        var button = (Button)sender;
        button.IsEnabled = false;
        button.Content = "测速中…";
        try
        {
            var svc = AppServices.Current;
            using var nodeA = new LocalNodeClient(svc.Ops);
            using var nodeB = new HttpNodeClient(ip, svc.Node.CtrlPort);
            var runner = new PairRunner();
            var p = new Params { Duration = 5, Parallel = 4 };
            var result = await runner.RunAsync(nodeA, nodeB, p, targetIp: ip);

            string text;
            if (result.Status == "done" && result.Summary is { } s)
            {
                var v = VerdictEvaluator.Evaluate(s, p.Protocol, result.LinkMbps, p);
                text = $"A→B {s.AB / 1e6:F1} Mbps │ B→A {s.BA / 1e6:F1} Mbps │ RTT {(s.RTTMs > 0 ? $"{s.RTTMs:F1} ms" : "—")}\n"
                     + $"结论：{v.GradeLabel} —— {v.Title}";
            }
            else
            {
                text = $"测速失败（{result.Status}）：{result.Reason}";
            }
            var dialog = new ContentDialog
            {
                Title = $"与 {ip} 的测速结果",
                Content = text,
                CloseButtonText = "关闭",
                XamlRoot = XamlRoot,
            };
            _ = await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "测速失败",
                Content = ex.Message,
                CloseButtonText = "关闭",
                XamlRoot = XamlRoot,
            };
            _ = await dialog.ShowAsync();
        }
        finally
        {
            button.IsEnabled = true;
            button.Content = "测速";
        }
    }
}

using System.Net.NetworkInformation;
using LanSpeed.Core.Control;
using LanSpeed.Core.Discovery;
using LanSpeed.Core.History;
using LanSpeed.Core.Iperf;
using LanSpeed.Core.Net;
using LanSpeed.Core.Orchestration;
using LanSpeed.Core.Persistence;
using LanSpeed.Core.Runner;
using LanSpeed.Core.Scan;

namespace LanSpeed.App.Services;

/// <summary>
/// App 内嵌节点服务（§3）：控制接口 + 发现应答与界面同进程常驻；
/// 界面页直接复用 Core 的扫描 / 分组 / 历史 / 软配对能力。
/// </summary>
public sealed class AppServices
{
    public static AppServices Current { get; } = new();

    /// <summary>应用正在整体退出（此时窗口关闭不再隐藏到托盘）。</summary>
    public static bool Exiting { get; set; }

    private AppServices()
    {
        Settings = AppSettings.Load();
        Db = new AppDb();
        PairedHosts = new PairedHostStore(Db);
        Groups = new HostGroupStore(Db);
        History = new HistoryStore(Db);
        Runner = new IperfRunner();
        Node = new LocalNode(Settings.CtrlPort) { Accept = Settings.AllowBeingTested };
        Ops = new NodeOps(Runner, Node);
        Server = new ControlServer(Ops);
        Discovery = new DiscoveryService(Ops.Hello);
    }

    public AppSettings Settings { get; }

    public AppDb Db { get; }

    public PairedHostStore PairedHosts { get; }

    public HostGroupStore Groups { get; }

    public HistoryStore History { get; }

    public IperfRunner Runner { get; }

    public LocalNode Node { get; }

    public NodeOps Ops { get; }

    public ControlServer Server { get; }

    public DiscoveryService Discovery { get; }

    /// <summary>最近一次扫描结果（分组页选成员用）。</summary>
    public List<HostEntry> LastScan { get; private set; } = [];

    /// <summary>最近一次分组测速结果（结果页展示）。</summary>
    public GroupResult? LastGroup { get; private set; }

    public Params LastGroupParams { get; private set; } = new();

    public event Action? ScanCompleted;

    public event Action? GroupCompleted;

    public async Task StartAsync()
    {
        HostsTrace("AppServices.StartAsync: enter");
        Scanner.Trace = m => HostsTrace(m);
        HostsTrace("AppServices.StartAsync: before server start");
        await Server.StartAsync(Settings.CtrlPort);
        HostsTrace("AppServices.StartAsync: server started");
        Discovery.Start();
        await Discovery.AnnounceAsync(force: true);
        HostsTrace("AppServices.StartAsync: discovery announced");
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
    }

    internal static void HostsTrace(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanSpeed", "ui-debug.log"),
                message + "\n");
        }
        catch (IOException)
        {
        }
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e) => _ = Discovery.AnnounceAsync();

    public async Task<List<HostEntry>> ScanAsync()
    {
        HostsTrace("AppServices.ScanAsync: enter");
        var scanner = new Scanner(Discovery);
        LastScan = await scanner.ScanAsync();
        RefreshPairedFromScan(LastScan);
        HostsTrace("AppServices.ScanAsync: done");
        ScanCompleted?.Invoke();
        return LastScan;
    }

    /// <summary>扫描见到已配对主机时刷新 IP / 链路；并返回按节点 ID 索引的配对表。</summary>
    public void RefreshPairedFromScan(IEnumerable<HostEntry> hosts)
    {
        foreach (var h in hosts)
        {
            if (h.Hello is null)
            {
                continue;
            }
            long link = h.Hello.Ips.Count > 0 ? h.Hello.Ips.Max(a => a.SpeedMbps) : 0;
            // 用该 IP 对应网卡速率；若多地址取最小正值更保守地标排查
            var speeds = h.Hello.Ips.Where(a => a.SpeedMbps > 0).Select(a => a.SpeedMbps).ToList();
            if (speeds.Count > 0)
            {
                link = speeds.Min();
            }
            PairedHosts.TouchSeen(
                h.Hello.Id,
                h.Hello.Name,
                h.Hello.CtrlPort != 0 ? h.Hello.CtrlPort : 39301,
                h.Hello.Ips.Select(a => a.Ip),
                link);
        }
    }

    public static long HostLinkMbps(HostEntry h)
    {
        if (h.Hello?.Ips is { Count: > 0 } ips)
        {
            var speeds = ips.Where(a => a.Ip == h.Ip || a.SpeedMbps > 0).Select(a => a.SpeedMbps).Where(s => s > 0).ToList();
            if (speeds.Count == 0)
            {
                speeds = ips.Select(a => a.SpeedMbps).Where(s => s > 0).ToList();
            }
            if (speeds.Count > 0)
            {
                // 优先取本 IP 对应地址的速率
                var match = ips.FirstOrDefault(a => a.Ip == h.Ip);
                if (match is { SpeedMbps: > 0 })
                {
                    return match.SpeedMbps;
                }
                return speeds.Min();
            }
        }
        return 0;
    }

    public static LinkClass HostLinkClass(HostEntry h) => LinkHealth.Classify(HostLinkMbps(h));

    public void SetGroupResult(GroupResult result, Params p)
    {
        LastGroup = result;
        LastGroupParams = p;
        GroupCompleted?.Invoke();
    }

    public async Task Shutdown()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        Runner.Dispose();
        Discovery.Dispose();
        await Server.DisposeAsync();
        Db.Dispose();
    }
}

using System.Net.NetworkInformation;
using LanSpeed.Core.Control;
using LanSpeed.Core.Discovery;
using LanSpeed.Core.History;
using LanSpeed.Core.Iperf;
using LanSpeed.Core.Orchestration;
using LanSpeed.Core.Runner;
using LanSpeed.Core.Scan;

namespace LanSpeed.App.Services;

/// <summary>
/// App 内嵌节点服务（§3）：控制接口 + 发现应答与界面同进程常驻；
/// 界面页直接复用 Core 的扫描 / 分组 / 历史能力。
/// </summary>
public sealed class AppServices
{
    public static AppServices Current { get; } = new();

    /// <summary>应用正在整体退出（此时窗口关闭不再隐藏到托盘）。</summary>
    public static bool Exiting { get; set; }

    private AppServices()
    {
        Settings = AppSettings.Load();
        Runner = new IperfRunner();
        Node = new LocalNode(Settings.CtrlPort) { Accept = Settings.AllowBeingTested };
        Ops = new NodeOps(Runner, Node);
        Server = new ControlServer(Ops);
        Discovery = new DiscoveryService(Ops.Hello);
    }

    public AppSettings Settings { get; }

    public IperfRunner Runner { get; }

    public LocalNode Node { get; }

    public NodeOps Ops { get; }

    public ControlServer Server { get; }

    public DiscoveryService Discovery { get; }

    public HistoryStore History { get; } = new();

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
        // 扫描阶段跟踪 → %LOCALAPPDATA%\LanSpeed\ui-debug.log（诊断用）
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
        HostsTrace("AppServices.ScanAsync: done");
        ScanCompleted?.Invoke();
        return LastScan;
    }

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
    }
}

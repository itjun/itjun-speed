using System.Globalization;
using System.Net.NetworkInformation;
using LanSpeed.Core.Control;
using LanSpeed.Core.Discovery;
using LanSpeed.Core.History;
using LanSpeed.Core.Iperf;
using LanSpeed.Core.Orchestration;
using LanSpeed.Core.Runner;
using LanSpeed.Core.Scan;
using LanSpeed.Core.Verdict;

namespace LanSpeed.Cli;

/// <summary>内网测速命令行（M1–M3：serve / test / scan / group / history）。</summary>
internal static class Program
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }
        try
        {
            return args[0] switch
            {
                "serve" => await ServeAsync(args[1..]),
                "test" => await TestAsync(args[1..]),
                "scan" => await ScanAsync(args[1..]),
                "group" => await GroupAsync(args[1..]),
                "history" => HistoryMain(args[1..]),
                _ => Unknown(args[0]),
            };
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("已取消。");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误：{ex.Message}");
            return 1;
        }
    }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"未知命令：{cmd}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            内网测速 CLI

            用法：
              serve [--port N]                          常驻节点：控制接口 + UDP 发现应答（默认 39301）
              scan [--wait 秒]                          扫描局域网：已安装节点 + 全部在线主机
              test <ip> [选项]                          以本机为 A、<ip> 为 B 单对测速
              group star --center <ip> <ip...> [选项]   星形分组测速（中心机 × 其余，双向）
              group mesh <ip...> [选项]                 矩阵分组测速（全部有序对，单向）
              history [--export 文件.csv]               查看历史 / 导出 CSV
            测速选项（test 与 group 共用）：
              --ctrl-port N   对端控制端口（默认 39301）
              -t N            时长秒（3–300，默认 10）
              -P N            并行流数（1–64，默认 4）
              -u              UDP（默认 TCP）
              -R | --bidir    方向：反向 / 双向（group 按模式固定方向，勿用）
              -b MBPS         UDP 总目标带宽
              -p N            服务端端口（默认 5201–5210 自动）
              -O N            忽略开头秒数（0–10）
              -w NKB          TCP 窗口 KB
              -M N            TCP MSS
              -i 0.5|1        采样间隔秒（默认 1）
            """);
    }

    private static async Task<int> ServeAsync(string[] args)
    {
        int port = 39301;
        foreach (var (flag, value) in ParseFlags(args))
        {
            if (flag == "--port")
            {
                port = int.Parse(value, Inv);
            }
            else
            {
                throw new ArgumentException($"serve 未知参数：{flag}");
            }
        }

        using var runner = new IperfRunner();
        var ops = new NodeOps(runner, new LocalNode(port));
        await using var server = new ControlServer(ops);
        using var discovery = new DiscoveryService(ops.Hello);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        await server.StartAsync(port, cts.Token);
        discovery.Start();
        await discovery.AnnounceAsync(force: true);
        // 网卡变化（IP 变动）时重新广播 hello（§4.1；DiscoveryService 内部限频）
        void OnNetworkChanged(object? sender, EventArgs e) => _ = discovery.AnnounceAsync();
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        cts.Token.Register(() => NetworkChange.NetworkAddressChanged -= OnNetworkChanged);

        var node = ops.Hello();
        Console.WriteLine($"内网测速节点已启动：{node.Name}（{node.Id[..8]}…）控制端口 {port}，iperf {node.Iperf}");
        foreach (var nic in LanSpeed.Core.Net.NicFilter.LocalNics().Select(n => $"{n.Ip}/{n.Prefix}（{n.Name}）"))
        {
            Console.WriteLine($"  网卡：{nic}");
        }
        Console.WriteLine("按 Ctrl+C 退出。");
        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("正在停止…");
        }
        return 0;
    }

    private static async Task<int> ScanAsync(string[] args)
    {
        double wait = 1.5;
        foreach (var (flag, value) in ParseFlags(args))
        {
            if (flag == "--wait")
            {
                wait = double.Parse(value, Inv);
            }
            else
            {
                throw new ArgumentException($"scan 未知参数：{flag}");
            }
        }

        using var runner = new IperfRunner();
        var ops = new NodeOps(runner, new LocalNode(39301));
        using var discovery = new DiscoveryService(ops.Hello);
        var scanner = new Scanner(discovery);
        Console.WriteLine($"正在扫描（发现 {wait.ToString(Inv)}s + ping/ARP/端口探测）…");
        var hosts = await scanner.ScanAsync(TimeSpan.FromSeconds(wait));

        Console.WriteLine();
        Console.WriteLine($"{"状态",-14} {"IP",-17} {"主机名",-16} {"MAC",-19} {"节点名",-16} 版本");
        Console.WriteLine(new string('─', 92));
        foreach (var h in hosts)
        {
            string name = h.Hello?.Name ?? "—";
            string ver = h.Hello != null ? $"{h.Hello.Version}/iperf{h.Hello.Iperf}" : "—";
            Console.WriteLine($"{h.Status,-16} {h.Ip,-17} {Trunc(h.Hostname ?? "—", 16),-16} {h.Mac ?? "—",-19} {Trunc(name, 16),-16} {ver}");
        }
        Console.WriteLine();
        Console.WriteLine($"共 {hosts.Count} 台：{string.Join("，", hosts.GroupBy(h => h.Status).Select(g => $"{g.Key} {g.Count()} 台"))}");
        return 0;
    }

    private static async Task<int> TestAsync(string[] args)
    {
        var (opts, p, ip) = ParseTestArgs(args);
        if (ip == null)
        {
            throw new ArgumentException("test 需要目标 IP，例如：test 192.168.1.23 -t 10 -P 4");
        }

        using var runner = new IperfRunner();
        var ops = new NodeOps(runner, new LocalNode(opts.CtrlPort));
        using var nodeA = new LocalNodeClient(ops);
        using var nodeB = new HttpNodeClient(ip, opts.CtrlPort);

        var hello = await nodeB.HelloAsync();
        p.Normalize();
        Console.WriteLine($"目标节点：{hello.Name}（内网测速 {hello.Version}，iperf {hello.Iperf}）");
        Console.WriteLine($"参数：{(p.Protocol == "udp" ? "UDP" : "TCP")} {p.Duration}s × {p.Parallel} 流，方向 {p.Direction}，服务端端口 {(p.Port == 0 ? "自动" : p.Port.ToString(Inv))}");

        var progress = new SyncProgress<Sample>(s =>
        {
            string flag = s.Omitted ? "（忽略段）" : "";
            Console.WriteLine($"[{s.T.ToString("F1", Inv)}s]{flag} A→B {Mbps(s.AB),10} Mbps │ B→A {Mbps(s.BA),10} Mbps");
        });

        var pairRunner = new PairRunner();
        var result = await pairRunner.RunAsync(nodeA, nodeB, p, targetIp: ip, progress);

        if (result.Status != "done" || result.Summary == null)
        {
            Console.Error.WriteLine($"测速失败（{result.Status}）：{result.Reason}");
            return 1;
        }
        PrintSummary(result.Summary, result.LinkMbps, p);

        var verdict = VerdictEvaluator.Evaluate(result.Summary, p.Protocol, result.LinkMbps, p);
        Console.WriteLine($"结论：{verdict.GradeLabel} —— {verdict.Title}");
        foreach (var note in verdict.Notes)
        {
            Console.WriteLine($"  · {note}");
        }

        AppendHistory("single", p,
        [
            new HistoryPair("本机", hello.Name, result.Status, result.Summary.AB, result.Summary.BA,
                result.Summary.RTTMs, result.Summary.Retransmits, result.Summary.JitterMs, result.Summary.LostPct,
                verdict.GradeLabel, verdict.Title),
        ]);
        return 0;
    }

    private static async Task<int> GroupAsync(string[] args)
    {
        var positional = new List<string>();
        string? center = null;
        var p = new Params();
        int ctrlPort = 39301;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"缺少 {a} 的值");
            switch (a)
            {
                case "--center":
                    center = Next();
                    break;
                case "--ctrl-port":
                    ctrlPort = int.Parse(Next(), Inv);
                    break;
                case "-t":
                    p.Duration = int.Parse(Next(), Inv);
                    break;
                case "-P":
                    p.Parallel = int.Parse(Next(), Inv);
                    break;
                case "-u":
                    p.Protocol = "udp";
                    break;
                case "-b":
                    p.UDPBandwidthMbps = double.Parse(Next(), Inv);
                    break;
                case "-i":
                    p.Interval = double.Parse(Next(), Inv);
                    break;
                default:
                    if (a.StartsWith('-'))
                    {
                        throw new ArgumentException($"group 未知参数：{a}");
                    }
                    positional.Add(a);
                    break;
            }
        }
        GroupMode mode = positional.FirstOrDefault() switch
        {
            "star" => GroupMode.Star,
            "mesh" => GroupMode.Mesh,
            _ => throw new ArgumentException("group 需要 star 或 mesh，例如：group star --center 192.168.1.10 192.168.1.10 192.168.1.23"),
        };
        var members = positional.Skip(1).Where(IsValidHostIp).Distinct().ToList();
        if (members.Count < 2)
        {
            throw new ArgumentException("group 至少需要 2 个成员 IP");
        }
        if (mode == GroupMode.Star && center == null)
        {
            center = members[0];
            Console.WriteLine($"未指定 --center，默认使用 {center} 为中心机");
        }

        using var runner = new IperfRunner();
        var ops = new NodeOps(runner, new LocalNode(ctrlPort));
        var groupRunner = new GroupRunner(ops);

        var pairs = Pairing.Build(mode, members, center, p).ToList();
        p.Normalize();
        Console.WriteLine($"分组测速（{(mode == GroupMode.Star ? "星形" : "矩阵")}）：{members.Count} 台，{pairs.Count} 轮，"
            + $"{(p.Protocol == "udp" ? "UDP" : "TCP")} {p.Duration}s × {p.Parallel} 流，方向 {p.Direction}");
        Console.WriteLine($"预计耗时约 {TimeSpan.FromSeconds(pairs.Count * (p.Duration + p.Omit + 3)).ToString(@"mm\-ss")}（可 Ctrl+C 停止，已完成轮次保留）");
        Console.WriteLine();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("正在停止（当前轮次完成后结束）…");
        };

        var progress = new SyncProgress<RoundResult>(r =>
        {
            int index = pairs.FindIndex(x => x.A == r.Pair.A && x.B == r.Pair.B) + 1;
            if (r.Status == "done" && r.Summary != null)
            {
                Console.WriteLine($"[{index}/{pairs.Count}] {r.Pair.A} → {r.Pair.B}：A→B {Mbps(r.Summary.AB)} Mbps │ B→A {Mbps(r.Summary.BA)} Mbps");
            }
            else
            {
                Console.WriteLine($"[{index}/{pairs.Count}] {r.Pair.A} → {r.Pair.B}：{r.Status} {r.Reason}");
            }
        });

        var result = await groupRunner.RunAsync(mode, members, center, p, progress, cts.Token);

        Console.WriteLine();
        Console.WriteLine("── 分组结果 " + new string('─', 40));
        var historyPairs = new List<HistoryPair>();
        foreach (var r in result.Rounds)
        {
            if (r.Status == "done" && r.Summary != null)
            {
                var v = VerdictEvaluator.Evaluate(r.Summary, p.Protocol, 0, p);
                Console.WriteLine($"{r.Pair.A,-17} → {r.Pair.B,-17} A→B {Mbps(r.Summary.AB),9} │ B→A {Mbps(r.Summary.BA),9} Mbps │ {v.GradeLabel}");
                historyPairs.Add(new HistoryPair(r.Pair.A, r.Pair.B, r.Status, r.Summary.AB, r.Summary.BA,
                    r.Summary.RTTMs, r.Summary.Retransmits, r.Summary.JitterMs, r.Summary.LostPct, v.GradeLabel, v.Title));
            }
            else
            {
                Console.WriteLine($"{r.Pair.A,-17} → {r.Pair.B,-17} {r.Status} {r.Reason}");
                historyPairs.Add(new HistoryPair(r.Pair.A, r.Pair.B, r.Status, 0, 0, 0, 0, 0, 0, r.Status, r.Reason));
            }
        }
        Console.WriteLine($"完成 {result.DoneCount}/{result.Rounds.Count} 轮。");
        AppendHistory(mode == GroupMode.Star ? "star" : "mesh", p, historyPairs);
        return result.DoneCount == result.Rounds.Count ? 0 : 1;
    }

    private static int HistoryMain(string[] args)
    {
        string? export = null;
        foreach (var (flag, value) in ParseFlags(args))
        {
            if (flag == "--export")
            {
                export = value;
            }
            else
            {
                throw new ArgumentException($"history 未知参数：{flag}");
            }
        }
        var store = new HistoryStore();
        if (export != null)
        {
            store.WriteCsv(export);
            Console.WriteLine($"已导出到 {export}");
            return 0;
        }
        var records = store.Load();
        if (records.Count == 0)
        {
            Console.WriteLine("暂无历史记录。");
            return 0;
        }
        foreach (var r in records)
        {
            Console.WriteLine($"{r.Time.LocalDateTime:yyyy-MM-dd HH:mm:ss}  {r.Mode,-6} {r.Protocol,-4} {r.Duration}s×{r.Parallel} {r.Direction,-7} {r.Pairs.Count} 对");
        }
        return 0;
    }

    private sealed record TestOpts(int CtrlPort);

    private static (TestOpts Opts, Params P, string? Ip) ParseTestArgs(string[] args)
    {
        string? ip = null;
        int ctrlPort = 39301;
        var p = new Params();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"缺少 {a} 的值");
            switch (a)
            {
                case "--ctrl-port":
                    ctrlPort = int.Parse(Next(), Inv);
                    break;
                case "-t":
                    p.Duration = int.Parse(Next(), Inv);
                    break;
                case "-P":
                    p.Parallel = int.Parse(Next(), Inv);
                    break;
                case "-u":
                    p.Protocol = "udp";
                    break;
                case "-R":
                    p.Direction = Directions.Reverse;
                    break;
                case "--bidir":
                    p.Direction = Directions.Bidir;
                    break;
                case "-b":
                    p.UDPBandwidthMbps = double.Parse(Next(), Inv);
                    break;
                case "-p":
                    p.Port = int.Parse(Next(), Inv);
                    break;
                case "-O":
                    p.Omit = int.Parse(Next(), Inv);
                    break;
                case "-w":
                    p.WindowKB = int.Parse(Next(), Inv);
                    break;
                case "-M":
                    p.MSS = int.Parse(Next(), Inv);
                    break;
                case "-i":
                    p.Interval = double.Parse(Next(), Inv);
                    break;
                default:
                    if (ip == null && IsValidHostIp(a))
                    {
                        ip = a;
                    }
                    else
                    {
                        throw new ArgumentException($"test 未知参数或无效 IP：{a}");
                    }
                    break;
            }
        }
        return (new TestOpts(ctrlPort), p, ip);
    }

    private static IEnumerable<(string Flag, string Value)> ParseFlags(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith('-'))
            {
                continue;
            }
            string flag = args[i];
            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            {
                yield return (flag, args[++i]);
            }
            else
            {
                throw new ArgumentException($"参数 {flag} 缺少值");
            }
        }
    }

    private static void PrintSummary(Summary s, long linkMbps, Params p)
    {
        Console.WriteLine();
        Console.WriteLine("── 结果 " + new string('─', 40));
        Console.WriteLine($"A→B 平均 {Mbps(s.AB),10} Mbps（峰值 {Mbps(s.PeakAB),10}）");
        Console.WriteLine($"B→A 平均 {Mbps(s.BA),10} Mbps（峰值 {Mbps(s.PeakBA),10}）");
        var extras = new List<string> { $"重传 {s.Retransmits} 次" };
        extras.Add(s.RTTMs > 0 ? $"RTT {s.RTTMs.ToString("F1", Inv)} ms" : "RTT —");
        if (s.JitterMs > 0)
        {
            extras.Add($"抖动 {s.JitterMs.ToString("F2", Inv)} ms");
        }
        if (s.LostPct > 0)
        {
            extras.Add($"丢包 {s.LostPct.ToString("F2", Inv)}%");
        }
        Console.WriteLine(string.Join(" │ ", extras));
        if (s.CPUClient > 0 || s.CPUServer > 0)
        {
            Console.WriteLine($"CPU 本机 {s.CPUClient.ToString("F1", Inv)}% │ 对端 {s.CPUServer.ToString("F1", Inv)}%");
        }
        if (linkMbps > 0)
        {
            Console.WriteLine($"链路上限 {linkMbps.ToString(Inv)} Mbps");
        }
        Console.WriteLine($"时长 {s.Seconds.ToString("F1", Inv)} 秒");
    }

    private static void AppendHistory(string mode, Params p, List<HistoryPair> pairs)
    {
        try
        {
            new HistoryStore().Append(new HistoryRecord(
                Guid.NewGuid().ToString("N"), DateTimeOffset.Now, mode, p.Protocol, p.Duration, p.Parallel, p.Direction, pairs));
        }
        catch (IOException)
        {
            // 历史落盘失败不影响测速结果
            Console.WriteLine("（历史记录保存失败：磁盘写入异常）");
        }
    }

    // Progress<T> 的 Report 异步投递会打乱输出顺序，CLI 需要同步回调
    private sealed class SyncProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }

    // 只接受完整的点分 IPv4（拒绝 "4" 这类可被 IPAddress.TryParse 解析成 0.0.0.4 的简写）
    private static bool IsValidHostIp(string s) =>
        System.Net.IPAddress.TryParse(s, out var addr)
        && addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
        && addr.ToString() == s;

    private static string Trunc(string s, int len) => s.Length <= len ? s : s[..(len - 1)] + "…";

    private static string Mbps(double bps) => (bps / 1e6).ToString("F1", Inv);
}

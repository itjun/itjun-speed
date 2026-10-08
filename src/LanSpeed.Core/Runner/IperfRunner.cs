using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text; // Encoding

namespace LanSpeed.Core.Runner;

/// <summary>
/// iperf3 子进程管理（§6.1）：服务端端口自动选择、客户端流式输出、硬性存活上限。
/// 仅 Windows：内置资源 + Job Object + 防睡眠；固定 --json-stream。
/// </summary>
public sealed class IperfRunner : IIperfRunner
{
    private static readonly int[] AutoPorts = Enumerable.Range(5201, 10).ToArray();

    private readonly string _exePath;
    private readonly SemaphoreSlim _serverSlot = new(1, 1);
    private readonly SemaphoreSlim _clientSlot = new(1, 1);
    private readonly object _gate = new();

    private Process? _server;
    private CancellationTokenSource? _serverKill;
    private CancellationTokenRegistration _serverKillReg;
    private Process? _client;

    public IperfRunner()
    {
        _exePath = IperfLocator.Current.ExePath;
    }

    public bool ServerRunning => Volatile.Read(ref _server) is { HasExited: false };

    public bool ClientRunning => Volatile.Read(ref _client) is { HasExited: false };

    public async Task<int> StartServerAsync(int port, int maxSeconds, CancellationToken ct = default)
    {
        await _serverSlot.WaitAsync(ct);
        try
        {
            if (ServerRunning)
            {
                throw new BusyException("iperf3 服务端已运行");
            }
            ClearDeadServer();
            var ports = port != 0 ? [port] : AutoPorts;
            foreach (var candidate in ports)
            {
                var proc = Start(["-s", "-p", candidate.ToString(CultureInfo.InvariantCulture)]);
                // 启动后等 400ms，进程仍存活即视为成功（§6.1）
                await Task.Delay(400, ct);
                if (!proc.HasExited)
                {
                    var kill = new CancellationTokenSource(TimeSpan.FromSeconds(maxSeconds));
                    // 到期杀进程并清槽位；不清的话节点会永远报 busy（实测踩过的坑）
                    var reg = kill.Token.Register(() =>
                    {
                        Kill(proc);
                        lock (_gate)
                        {
                            if (ReferenceEquals(_server, proc))
                            {
                                _server = null;
                                _serverKill = null;
                                _serverKillReg = default;
                            }
                        }
                    });
                    lock (_gate)
                    {
                        _server = proc;
                        _serverKill = kill;
                        _serverKillReg = reg;
                    }
                    return candidate;
                }
                proc.Dispose();
            }
            throw new InvalidOperationException("5201–5210 端口均无法启动 iperf3 服务端（端口被占用？）");
        }
        finally
        {
            _serverSlot.Release();
        }
    }

    // 上一个服务端进程已退出（maxSeconds 到期或异常退出）时清理残留状态
    private void ClearDeadServer()
    {
        Process? dead = null;
        lock (_gate)
        {
            if (_server is { HasExited: true })
            {
                dead = _server;
                _server = null;
                _serverKillReg.Dispose();
                _serverKill = null;
            }
        }
        dead?.Dispose();
    }

    public Task StopServerAsync()
    {
        _serverSlot.Wait();
        try
        {
            Process? proc;
            lock (_gate)
            {
                proc = _server;
                _server = null;
                _serverKillReg.Dispose();
                _serverKill?.Cancel();
                _serverKill?.Dispose();
                _serverKill = null;
            }
            if (proc != null)
            {
                Kill(proc);
                proc.Dispose();
            }
            return Task.CompletedTask;
        }
        finally
        {
            _serverSlot.Release();
        }
    }

    public async IAsyncEnumerable<string> RunClientStreamAsync(
        IReadOnlyList<string> args, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await _clientSlot.WaitAsync(ct);
        Process? proc = null;
        IDisposable? sleep = null;
        try
        {
            if (_client != null)
            {
                throw new BusyException("iperf3 客户端已运行");
            }
            proc = Start(args);
            _client = proc;
            sleep = PreventSleep();
            // stderr 单独排空，避免管道写满导致卡死；错误信息以 stdout 的 error 事件为准
            _ = proc.StandardError.ReadToEndAsync();
            var stdout = proc.StandardOutput;
            while (await stdout.ReadLineAsync(ct) is { } line)
            {
                yield return line;
            }
            await proc.WaitForExitAsync(ct);
        }
        finally
        {
            CleanupClient(proc, sleep);
            _clientSlot.Release();
        }
    }

    public async Task<string> RunProbeAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        await _clientSlot.WaitAsync(ct);
        Process? proc = null;
        try
        {
            if (_client != null)
            {
                throw new BusyException("iperf3 客户端已运行");
            }
            proc = Start(args);
            _client = proc;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(6)); // 探测整体 6 秒超时（§6.4）
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await proc.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Kill(proc);
            }
            string output;
            try
            {
                output = await stdoutTask;
            }
            catch (OperationCanceledException)
            {
                output = string.Empty;
            }
            try
            {
                await stderrTask;
            }
            catch (OperationCanceledException)
            {
                // stderr 仅用于排空
            }
            return output;
        }
        finally
        {
            CleanupClient(proc, null);
            _clientSlot.Release();
        }
    }

    public Task StopClientAsync()
    {
        _clientSlot.Wait();
        try
        {
            var proc = _client;
            _client = null;
            if (proc != null && !proc.HasExited)
            {
                Kill(proc);
            }
            return Task.CompletedTask;
        }
        finally
        {
            _clientSlot.Release();
        }
    }

    private Process Start(IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _exePath,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(_exePath))!,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        var proc = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 iperf3");
        JobObject.Assign(proc);
        return proc;
    }

    private static IDisposable PreventSleep() => ExecutionState.PreventSleep();

    private void CleanupClient(Process? proc, IDisposable? sleep)
    {
        if (proc != null)
        {
            if (!proc.HasExited)
            {
                Kill(proc);
            }
            try
            {
                proc.Dispose();
            }
            catch (InvalidOperationException)
            {
                // 进程已退出时 Dispose 可能竞争，忽略
            }
        }
        sleep?.Dispose();
        if (_client == proc)
        {
            _client = null;
        }
    }

    private static void Kill(Process proc)
    {
        try
        {
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // 已退出
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 已退出或句柄失效
        }
    }

    public void Dispose()
    {
        StopServerAsync().GetAwaiter().GetResult();
        StopClientAsync().GetAwaiter().GetResult();
        _serverSlot.Dispose();
        _clientSlot.Dispose();
    }
}

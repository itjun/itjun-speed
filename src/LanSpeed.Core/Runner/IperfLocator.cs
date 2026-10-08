using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LanSpeed.Core.Runner;

public sealed record IperfEngine(string ExePath, string Version, bool SupportsJsonStream);

/// <summary>定位 iperf3 可执行文件：Windows 用内置资源，Linux/macOS 用系统安装的 iperf3。</summary>
public static partial class IperfLocator
{
    private static IperfEngine? _cached;

    /// <summary>未安装 iperf3 时抛出带安装提示的异常（LocalNode 等只想展示版本时用 TryVersion）。</summary>
    public static IperfEngine Current => _cached ??= Locate();

    public static string TryVersion()
    {
        try
        {
            return Current.Version;
        }
        catch (InvalidOperationException)
        {
            return "未安装";
        }
    }

    private static IperfEngine Locate()
    {
        if (OperatingSystem.IsWindows())
        {
            // 内置 ar51an 3.22 构建，支持 --json-stream
            return new IperfEngine(IperfAssets.EnsureExtracted(), IperfAssets.Version, SupportsJsonStream: true);
        }
        return LocateSystem();
    }

    private static IperfEngine LocateSystem()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var dirs = path.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append("/usr/bin")
            .Append("/usr/local/bin")
            .Append("/opt/homebrew/bin")
            .Distinct();
        foreach (var dir in dirs)
        {
            string candidate = Path.Combine(dir, "iperf3");
            if (File.Exists(candidate))
            {
                // 发行版自带的 iperf3 可能没有 --json-stream（如 Ubuntu 24.04 的 3.16），运行时检测
                bool jsonStream = QueryHelp(candidate).Contains("--json-stream");
                return new IperfEngine(candidate, QueryVersion(candidate), jsonStream);
            }
        }
        throw new InvalidOperationException(
            "未找到 iperf3。请先安装：Ubuntu/Debian 执行 sudo apt-get install -y iperf3，macOS 执行 brew install iperf3");
    }

    private static string QueryVersion(string exe)
    {
        string output = Run(exe, "--version");
        var match = VersionRegex().Match(output);
        return match.Success ? match.Groups[1].Value : "unknown";
    }

    private static string QueryHelp(string exe) => Run(exe, "--help");

    private static string Run(string exe, string arg)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                ArgumentList = { arg },
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            return proc is null ? string.Empty : proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    [GeneratedRegex(@"iperf3?\s+v?(\d+\.\d+)")]
    private static partial Regex VersionRegex();
}

using System.Text.RegularExpressions;

namespace LanSpeed.Core.Runner;

public sealed record IperfEngine(string ExePath, string Version, bool SupportsJsonStream);

/// <summary>定位内置 iperf3（仅 Windows，固定支持 --json-stream）。</summary>
public static partial class IperfLocator
{
    private static IperfEngine? _cached;

    /// <summary>未就绪时抛出带提示的异常（LocalNode 等只想展示版本时用 TryVersion）。</summary>
    public static IperfEngine Current => _cached ??= Locate();

    public static string TryVersion()
    {
        try
        {
            return Current.Version;
        }
        catch (InvalidOperationException)
        {
            return "未就绪";
        }
    }

    private static IperfEngine Locate()
    {
        // 内置 ar51an 3.22 构建，支持 --json-stream
        return new IperfEngine(IperfAssets.EnsureExtracted(), IperfAssets.Version, SupportsJsonStream: true);
    }

    [GeneratedRegex(@"iperf3?\s+v?(\d+\.\d+)")]
    private static partial Regex VersionRegex();
}

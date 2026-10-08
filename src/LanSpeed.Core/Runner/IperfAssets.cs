using System.Security.Cryptography;

namespace LanSpeed.Core.Runner;

/// <summary>
/// iperf3 资源管理（Windows，§6.1）：安装目录 assets → %LOCALAPPDATA%\LanSpeed\iperf3\{版本}\，
/// 逐文件 SHA256 校验，失败用 .tmp+rename 重写。iperf3.exe 与 cygwin1.dll 必须在同一目录。
/// 仅在 Windows 上调用（见 IperfLocator）。
/// </summary>
public static class IperfAssets
{
    public const string Version = "3.22";

    /// <summary>确保 iperf3 已落盘并通过校验，返回 iperf3.exe 完整路径。</summary>
    public static string EnsureExtracted()
    {
        string target = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanSpeed", "iperf3", Version);
        string exe = Path.Combine(target, "iperf3.exe");

        if (Verify(target))
        {
            return exe;
        }
        string? source = FindSource();
        if (source == null)
        {
            throw new InvalidOperationException(
                "找不到内置 iperf3 资源（assets/iperf3/win64）。可用环境变量 LANSPEED_IPERF_DIR 指定目录。");
        }
        Copy(source, target);
        if (!Verify(target))
        {
            throw new InvalidDataException("iperf3 资源复制后校验失败");
        }
        return exe;
    }

    private static string? FindSource()
    {
        string? env = Environment.GetEnvironmentVariable("LANSPEED_IPERF_DIR");
        if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, "iperf3.exe")))
        {
            return env;
        }
        // 从输出目录向上找仓库内的 assets（未打包场景）
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            string cand = Path.Combine(dir, "assets", "iperf3", "win64");
            if (File.Exists(Path.Combine(cand, "iperf3.exe")))
            {
                return cand;
            }
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        return null;
    }

    private static bool Verify(string dir)
    {
        string sums = Path.Combine(dir, "SHA256SUMS");
        if (!File.Exists(sums) || !File.Exists(Path.Combine(dir, "iperf3.exe")))
        {
            return false;
        }
        bool any = false;
        foreach (var line in File.ReadAllLines(sums))
        {
            var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                continue;
            }
            string expected = parts[0].Trim();
            // 路径可能带 win64/ 前缀，取文件名
            string name = Path.GetFileName(parts[1].Trim().Replace('/', Path.DirectorySeparatorChar));
            string file = Path.Combine(dir, name);
            if (!File.Exists(file))
            {
                return false;
            }
            string actual = Convert.ToHexString(HashFile(file));
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            any = true;
        }
        return any;
    }

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            string dest = Path.Combine(target, Path.GetFileName(file));
            string tmp = dest + ".tmp";
            File.Copy(file, tmp, overwrite: true);
            if (File.Exists(dest))
            {
                File.Delete(dest);
            }
            File.Move(tmp, dest);
        }
    }

    private static byte[] HashFile(string file)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(file);
        return sha.ComputeHash(stream);
    }
}

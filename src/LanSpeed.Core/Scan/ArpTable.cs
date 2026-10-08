using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;

namespace LanSpeed.Core.Scan;

/// <summary>ARP 邻居表（§4.2）：Windows 解析 arp -a，补上不回 ping 的主机并取 MAC。</summary>
public static partial class ArpTable
{
    /// <summary>读取本机 ARP 表：IP → 规范化 MAC（冒号小写）。</summary>
    public static async Task<Dictionary<string, string>> ReadAsync()
    {
        string output = await RunAsync("arp", "-a");
        return ParseArpA(output);
    }

    /// <summary>Windows arp -a 输出：`  192.168.1.1        a0-bc-xx-xx-xx-xx     dynamic`（中英文系统同样适用，按列匹配）。</summary>
    public static Dictionary<string, string> ParseArpA(string output)
    {
        var map = new Dictionary<string, string>();
        foreach (var line in output.Split('\n'))
        {
            var match = ArpLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }
            AddIfUnicast(map, match.Groups[1].Value, match.Groups[2].Value.Replace('-', ':').ToLowerInvariant());
        }
        return map;
    }

    // 排除组播 / 广播地址与组播 MAC（MAC 格式由正则保证）
    private static void AddIfUnicast(Dictionary<string, string> map, string ip, string mac)
    {
        if (!IPAddress.TryParse(ip, out var addr))
        {
            return;
        }
        var b = addr.GetAddressBytes();
        if (b[0] >= 224 && b[0] <= 239 || b[0] == 255)
        {
            return;
        }
        if (mac.StartsWith("01:00:5e") || mac.StartsWith("33:33") || mac == "ff:ff:ff:ff:ff:ff")
        {
            return;
        }
        map[ip] = mac;
    }

    private static async Task<string> RunAsync(string file, string arg)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                ArgumentList = { arg },
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return string.Empty;
            }
            return await proc.StandardOutput.ReadToEndAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    [GeneratedRegex(@"(\d{1,3}(?:\.\d{1,3}){3})\s+((?:[0-9a-fA-F]{2}[-:]){5}[0-9a-fA-F]{2})")]
    private static partial Regex ArpLineRegex();
}

using System.Diagnostics;
using LanSpeed.Core.Runner;

namespace LanSpeed.App.Services;

/// <summary>防火墙与网络自检、一键放行（§10）。</summary>
public static class FirewallHelper
{
    public sealed record CheckResult(bool AnyFwEnabled, bool HasRuleForApp, bool IsPrivateNetwork, string Summary)
    {
        public bool AllOk => !AnyFwEnabled || HasRuleForApp;
    }

    /// <summary>检查：防火墙是否启用、是否有本程序与 iperf3 的入站规则、当前网络是否「专用」。</summary>
    public static CheckResult Check()
    {
        bool fwEnabled = false;
        bool hasRule = false;
        bool isPrivate = true;
        string? fwError = null;
        string? netError = null;
        try
        {
            Type? fwType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (fwType is null)
            {
                fwError = "防火墙状态读取失败：系统未注册防火墙策略组件。";
            }
            else
            {
                dynamic fw = Activator.CreateInstance(fwType)!;
                fwEnabled = fw.FirewallEnabled[1] /* NET_FW_PROFILE_PRIVATE */ || fw.FirewallEnabled[0] /* PUBLIC */;
                foreach (dynamic rule in fw.Rules)
                {
                    try
                    {
                        string name = rule.Name is string s ? s : string.Empty;
                        string exe = rule.ApplicationFullPath is string p ? p : string.Empty;
                        if (name.Contains("内网测速", StringComparison.Ordinal)
                            || exe.Equals(Process.GetCurrentProcess().MainModule?.FileName, StringComparison.OrdinalIgnoreCase))
                        {
                            hasRule = true;
                            break;
                        }
                    }
                    catch (Exception ex) when (ex is Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or UnauthorizedAccessException)
                    {
                        // 个别系统规则的属性读取受限，跳过
                    }
                }
            }
        }
        catch (Exception ex)
        {
            fwError = $"防火墙状态读取失败：{ex.Message}";
        }

        try
        {
            // INetworkListManager 没有 ProgID，按 CLSID 创建（§10）。
            Type? nlmType = Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"));
            if (nlmType is null)
            {
                netError = "网络类型读取失败：系统未注册网络列表组件。";
            }
            else
            {
                dynamic nlm = Activator.CreateInstance(nlmType)!;
                bool anyPrivate = false, anyPublic = false;
                // 1 = NLM_ENUM_NETWORK_CONNECTED，只看当前已连接的网络。
                foreach (dynamic net in nlm.GetNetworks(1))
                {
                    int category = (int)net.GetCategory(); // 0=public 1=private 2=domain
                    if (category == 0)
                    {
                        anyPublic = true;
                    }
                    else
                    {
                        anyPrivate = true;
                    }
                }

                isPrivate = !anyPublic || anyPrivate;
            }
        }
        catch (Exception ex)
        {
            netError = $"网络类型读取失败：{ex.Message}";
        }

        string fwPart = fwError ?? $"防火墙{(fwEnabled ? "启用" : "关闭")}；入站规则{(hasRule ? "已放行" : "未放行")}";
        string netPart = netError ?? $"网络{(isPrivate ? "专用（可测）" : "公用（可能被拦截）")}";
        string advice = fwError is null && fwEnabled && !hasRule ? " 建议点击「一键放行」。" : string.Empty;
        return new CheckResult(fwEnabled, hasRule, isPrivate, $"{fwPart}；{netPart}。{advice}");
    }

    /// <summary>一键放行：以管理员身份运行 netsh 为本程序与 iperf3 添加入站规则（会弹 UAC）。</summary>
    public static void FixWithUac()
    {
        string exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "LanSpeed.App.exe";
        string iperf = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanSpeed", "iperf3", IperfAssets.Version, "iperf3.exe");
        string cmd =
            $"advfirewall firewall add rule name=\"内网测速-程序\" dir=in action=allow program=\"{exe}\" enable=yes & " +
            $"advfirewall firewall add rule name=\"内网测速-发现\" dir=in action=allow protocol=UDP localport=39300 enable=yes & " +
            $"advfirewall firewall add rule name=\"内网测速-控制\" dir=in action=allow protocol=TCP localport=39301 enable=yes & " +
            $"advfirewall firewall add rule name=\"内网测速-打流\" dir=in action=allow protocol=ANY localport=5201-5210 enable=yes" +
            (File.Exists(iperf) ? $" & advfirewall firewall add rule name=\"内网测速-iperf3\" dir=in action=allow program=\"{iperf}\" enable=yes" : string.Empty);
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = cmd,
            Verb = "runas", // UAC 提权
            UseShellExecute = true,
        };
        using var _ = Process.Start(psi);
    }
}

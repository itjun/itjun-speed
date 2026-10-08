using System.Net.Http.Json;
using LanSpeed.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LanSpeed.App.Pages;

/// <summary>设置页（§8/§10）：允许被测、端口、防火墙自检与一键放行、开机自启、检查更新。</summary>
public sealed partial class SettingsPage : Page
{
    private bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        var s = AppServices.Current.Settings;
        AcceptSwitch.IsOn = s.AllowBeingTested;
        PortBox.Value = s.CtrlPort;
        AutoStartSwitch.IsOn = AutoStartHelper.IsEnabled();
        VersionText.Text = $"内网测速 {AppServices.Current.Node.Version}（iperf {AppServices.Current.Node.IperfVersion}）";
        _loading = false;
    }

    private void OnAcceptToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var svc = AppServices.Current;
        svc.Node.Accept = AcceptSwitch.IsOn;
        svc.Settings.AllowBeingTested = AcceptSwitch.IsOn;
        svc.Settings.Save();
    }

    private void OnPortChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading)
        {
            return;
        }
        AppServices.Current.Settings.CtrlPort = (int)Math.Clamp(PortBox.Value, 1024, 65535);
        AppServices.Current.Settings.Save();
    }

    private void OnFirewallCheck(object sender, RoutedEventArgs e)
    {
        var r = FirewallHelper.Check();
        FirewallText.Text = r.Summary;
    }

    private void OnFirewallFix(object sender, RoutedEventArgs e)
    {
        FirewallHelper.FixWithUac();
        FirewallText.Text = "已请求管理员放行（UAC 确认后即刻生效；netsh 将为程序与端口添加「内网测速」入站规则）。";
    }

    private void OnAutoStartToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        try
        {
            AutoStartHelper.SetEnabled(AutoStartSwitch.IsOn);
            AppServices.Current.Settings.AutoStart = AutoStartSwitch.IsOn;
            AppServices.Current.Settings.Save();
        }
        catch (Exception ex)
        {
            FirewallText.Text = $"设置开机自启失败：{ex.Message}";
        }
    }

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        var url = AppServices.Current.Settings.UpdateCheckUrl;
        var current = AppServices.Current.Node.Version;
        if (string.IsNullOrWhiteSpace(url))
        {
            UpdateText.Text = $"当前版本 {current}；未配置更新源（settings.json 的 updateCheckUrl）。";
            return;
        }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var info = await http.GetFromJsonAsync<UpdateInfo>(url);
            UpdateText.Text = info?.Version is { } v
                ? (v.Equals(current, StringComparison.Ordinal) ? $"已是最新版本 {current}。" : $"发现新版本 {v}（当前 {current}）。")
                : "更新源响应无效。";
        }
        catch (Exception ex)
        {
            UpdateText.Text = $"检查更新失败：{ex.Message}";
        }
    }

    private sealed class UpdateInfo
    {
        public string? Version { get; set; }
    }
}

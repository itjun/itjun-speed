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
        if (AppUpdateFlow.Last is not null)
        {
            UpdateText.Text = AppUpdateFlow.Describe(AppUpdateFlow.Last);
        }
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
        UpdateText.Text = "正在检查更新…";
        await AppUpdateFlow.PromptAsync(XamlRoot, interactive: true);
        UpdateText.Text = AppUpdateFlow.Describe(AppUpdateFlow.Last);
    }
}

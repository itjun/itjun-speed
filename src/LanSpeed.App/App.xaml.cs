using LanSpeed.App.Services;
using Microsoft.UI.Xaml;

namespace LanSpeed.App;

/// <summary>「内网测速」应用程序入口：启动主窗口与内嵌节点服务（§3：Generic Host 思路，节点服务与界面同进程）。</summary>
public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    public static MainWindow? MainWnd { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        MainWnd = _window;
        _window.Activate();
        _ = AppServices.Current.StartAsync();
    }

    public static void ExitApp()
    {
        Services.AppServices.Exiting = true;
        AppServices.Current.Shutdown().GetAwaiter().GetResult();
        Current.Exit();
    }
}

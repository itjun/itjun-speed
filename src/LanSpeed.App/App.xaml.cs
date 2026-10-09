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
        // UI 线程未处理异常（含 async void）落盘，便于排障；不吞异常，仍走默认崩溃路径
        UnhandledException += (_, e) =>
        {
            try
            {
                var line = $"{DateTime.Now:HH:mm:ss.fff} UnhandledException: {e.Message}\n{e.Exception}\n";
                File.AppendAllText(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanSpeed", "ui-debug.log"),
                    line);
            }
            catch { /* 日志失败不影响原异常 */ }
        };
    }

    public static MainWindow? MainWnd { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        MainWnd = _window;
        _window.Activate();
        SingleInstance.Listen(_window);
        _ = AppServices.Current.StartAsync();
    }

    public static void ExitApp()
    {
        Services.AppServices.Exiting = true;
        AppServices.Current.Shutdown().GetAwaiter().GetResult();
        Current.Exit();
    }
}

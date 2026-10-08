using H.NotifyIcon;
using LanSpeed.App.Pages;
using LanSpeed.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace LanSpeed.App;

/// <summary>主窗口：NavigationView 五页 + 托盘；关闭窗口隐藏到托盘，退出走托盘菜单（§8）。</summary>
public sealed partial class MainWindow : Window
{
    private bool _wasTested;
    private readonly DispatcherTimer _trayTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public MainWindow()
    {
        InitializeComponent();
        Title = "内网测速";
        AppWindow.Resize(new SizeInt32(1180, 760));
        AppWindow.Closing += (_, e) =>
        {
            if (!AppServices.Exiting)
            {
                e.Cancel = true;
                AppWindow.Hide();
            }
        };
        ContentFrame.Navigate(typeof(HostsPage));

        Tray.IconSource = new BitmapImage(new Uri("ms-appx:///Assets/app.ico"));
        Tray.DoubleClickCommand = new ShowWindowCommand(this);
        TrayAccept.IsChecked = AppServices.Current.Node.Accept;
        _trayTimer.Tick += (_, _) => UpdateTrayState();
        _trayTimer.Start();
    }

    private void UpdateTrayState()
    {
        var svc = AppServices.Current;
        bool busy = svc.Runner.ClientRunning || svc.Runner.ServerRunning;
        Tray.ToolTipText = busy ? "内网测速：正在测速" : "内网测速";
        // 被测时弹系统通知（§8/§9）：状态从空闲转为忙时提示一次
        if (busy && !_wasTested)
        {
            Tray.ShowNotification("内网测速", "本机正在被测速，可从托盘菜单「中止当前测速」一键停止。");
        }
        _wasTested = busy;
    }

    private void OnNavSelection(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            Type page = tag switch
            {
                "group" => typeof(GroupPage),
                "results" => typeof(ResultsPage),
                "history" => typeof(HistoryPage),
                "settings" => typeof(SettingsPage),
                _ => typeof(HostsPage),
            };
            if (ContentFrame.CurrentSourcePageType != page)
            {
                ContentFrame.Navigate(page);
            }
        }
    }

    private void OnTrayDoubleClick(object sender, RoutedEventArgs e) => ShowMainWindow();

    private sealed class ShowWindowCommand(MainWindow window) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => window.ShowMainWindow();
    }

    private void OnShowWindow(object sender, RoutedEventArgs e) => ShowMainWindow();

    public void ShowMainWindow()
    {
        AppWindow.Show();
        Activate();
    }

    private void OnTrayAcceptToggle(object sender, RoutedEventArgs e)
    {
        var svc = AppServices.Current;
        svc.Node.Accept = TrayAccept.IsChecked;
        svc.Settings.AllowBeingTested = TrayAccept.IsChecked;
        svc.Settings.Save();
    }

    private async void OnStopCurrent(object sender, RoutedEventArgs e)
    {
        await AppServices.Current.Runner.StopClientAsync();
        await AppServices.Current.Runner.StopServerAsync();
    }

    private void OnExitApp(object sender, RoutedEventArgs e) => App.ExitApp();
}

using System.Drawing;
using System.Runtime.InteropServices;
using H.NotifyIcon;
using LanSpeed.App.Pages;
using LanSpeed.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace LanSpeed.App;

/// <summary>主窗口：NavigationView 五页 + 托盘；关闭窗口隐藏到托盘，退出走托盘菜单（§8）。</summary>
public sealed partial class MainWindow : Window
{
    private readonly DispatcherTimer _trayTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public MainWindow()
    {
        InitializeComponent();
        Title = "内网测速";
        // WinUI 3 桌面窗口类不注册图标（WinAppSDK 已知行为）：不 SetIcon 则标题栏/任务栏/Alt-Tab 全部空白
        AppWindow.SetIcon(IcoPath("app.ico"));
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

        // 托盘：IconSource 的异步转换链（ms-appx URI → 文件流 → HICON）会静默失败，改为同步直读赋 Icon
        // Id 换为固定新 GUID：默认 Id 由进程路径推导，多轮强杀后 explorer 可能缓存了同 GUID 的坏条目
        Tray.Id = new Guid("8F1E0A6C-3D24-4B7E-9A58-C5E21B7F0436");
        var trayIcon = LoadTrayIcon("app.ico");
        TrayLog($"trayIcon loaded: {(trayIcon is null ? "null" : $"{trayIcon.Width}x{trayIcon.Height} handle={trayIcon.Handle}")}");
        Tray.Icon = trayIcon;
        _trayIconFile = "app.ico"; // 与上方赋值同步，避免托盘定时器首次 tick 重复重载
        Tray.DoubleClickCommand = new ShowWindowCommand(this);
        if (Content is FrameworkElement root)
        {
            root.Loaded += async (_, _) =>
            {
                await Task.Delay(1500);
                if (!Tray.IsCreated)
                {
                    Tray.ForceCreate();
                }
                _trayIconFile = "app.ico";
                _trayTip = "内网测速";
                ReRegisterTray(_trayIconFile, _trayTip);
                TrayLog($"after-load: created={Tray.IsCreated}");
            };
        }
        TrayAccept.IsChecked = AppServices.Current.Node.Accept;
        _trayTimer.Tick += (_, _) => UpdateTrayState();
        _trayTimer.Start();

        // 侧栏宽度：恢复上次记忆值，拖拽条跟随窗格右缘
        if (AppServices.Current.Settings.PaneWidth is >= PaneMin and <= PaneMax)
        {
            Nav.OpenPaneLength = AppServices.Current.Settings.PaneWidth;
        }
        Nav.PaneOpening += (_, _) => SyncResizer();
        Nav.PaneClosing += (_, _) => SyncResizer();
        Nav.SizeChanged += (_, _) => SyncResizer();
        Activated += (_, _) => SyncResizer();
        if (Content is FrameworkElement content)
        {
            content.Loaded += (_, _) => SyncResizer();
        }
    }

    private const double PaneMin = 160;
    private const double PaneDefault = 190;
    private const double PaneMax = 420;

    /// <summary>拖拽条跟随窗格右缘；窗格收起/紧凑模式时隐藏。</summary>
    private void SyncResizer()
    {
        bool expanded = Nav.DisplayMode == NavigationViewDisplayMode.Expanded && Nav.IsPaneOpen;
        PaneResizer.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        if (expanded)
        {
            PaneResizer.Margin = new Thickness(Nav.OpenPaneLength - 4, 48, 0, 24);
        }
    }

    private void OnResizerPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        // ProtectedCursor 在当前 SDK 为保护级：以悬停高亮代替光标切换
    }

    private void OnResizerPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
    }

    private void OnResizerDragDelta(object sender, Microsoft.UI.Xaml.Controls.Primitives.DragDeltaEventArgs e)
    {
        Nav.OpenPaneLength = Math.Clamp(Nav.OpenPaneLength + e.HorizontalChange, PaneMin, PaneMax);
        SyncResizer();
        // 直接保存：拖拽结束事件在部分输入路径下不触发，逐次落盘代价可忽略
        AppServices.Current.Settings.PaneWidth = Nav.OpenPaneLength;
        AppServices.Current.Settings.Save();
    }

    private void OnResizerDragCompleted(object sender, Microsoft.UI.Xaml.Controls.Primitives.DragCompletedEventArgs e)
    {
        AppServices.Current.Settings.PaneWidth = Nav.OpenPaneLength;
        AppServices.Current.Settings.Save();
    }

    private void OnResizerDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        Nav.OpenPaneLength = PaneDefault;
        AppServices.Current.Settings.PaneWidth = PaneDefault;
        AppServices.Current.Settings.Save();
        SyncResizer();
    }

    private void UpdateTrayState()
    {
        var svc = AppServices.Current;
        bool busy = svc.Runner.ClientRunning || svc.Runner.ServerRunning;
        // 托盘状态图标：常态速度表盘 / 测速中绿徽章双箭头（§8 图标区分状态）
        var file = busy ? "app-busy.ico" : "app.ico";
        var tip = busy ? "内网测速：正在测速" : "内网测速";
        if (_trayIconFile != file || _trayTip != tip)
        {
            TrayLog($"state-change: busy={busy} file={file}");
            _trayIconFile = file;
            _trayTip = tip;
            ReRegisterTray(file, tip);
        }
    }

    private string _trayIconFile = string.Empty;
    private string _trayTip = string.Empty;

    private static string IcoPath(string file) => Path.Combine(AppContext.BaseDirectory, "Assets", file);

    /// <summary>排障日志：与 ui-debug.log 同文件，诊断托盘/图标链路。</summary>
    private static void TrayLog(string message)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanSpeed", "ui-debug.log");
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} Tray: {message}\n");
        }
        catch { /* 日志失败忽略 */ }
    }

    private const int SM_CXSMICON = 49;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    private const uint NIM_ADD = 0, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_GUID = 0x10;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr instance, string fileName, uint type, int cx, int cy, uint load);

    /// <summary>库的注册固定携带 NIS_HIDDEN 状态且本机 Win10 explorer 的 NIM_MODIFY（含库的 ToolTip 更新）
    /// 返回成功但实际移除图标：图标/提示的一切变更都按「DELETE + 无 NIF_STATE 重注册」完成
    /// （沿用库的消息窗口与回调 1024 + SETVERSION(4)，右键菜单/事件不受影响）。</summary>
    private void ReRegisterTray(string file, string tip)
    {
        try
        {
            int size = GetSystemMetrics(SM_CXSMICON);
            var handle = LoadImageW(IntPtr.Zero, IcoPath(file), 1 /*IMAGE_ICON*/, size, size, 0x10 /*LR_LOADFROMFILE*/);
            if (handle == IntPtr.Zero)
            {
                TrayLog($"re-register skipped: LoadImage({file}) failed");
                return;
            }
            var data = new NOTIFYICONDATAW
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = Tray.TrayIcon.WindowHandle,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_GUID,
                uCallbackMessage = 1024,
                hIcon = handle,
                szTip = tip,
                guidItem = Tray.Id,
            };
            Shell_NotifyIconW(NIM_DELETE, ref data);
            if (!Shell_NotifyIconW(NIM_ADD, ref data))
            {
                TrayLog($"re-register add failed: lastError={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
                return;
            }
            data.uFlags = NIF_GUID;
            data.uVersion = 4;
            if (!Shell_NotifyIconW(NIM_SETVERSION, ref data))
            {
                TrayLog("re-register setversion failed");
            }
            TrayLog($"re-register ok: {file} tip={tip}");
        }
        catch (Exception ex)
        {
            TrayLog($"re-register failed: {ex.Message}");
        }
    }

    /// <summary>同步加载托盘图标：优先按系统小图标尺寸取条目，取不到时回退默认条目。</summary>
    private static Icon? LoadTrayIcon(string file)
    {
        try
        {
            using var fs = File.OpenRead(IcoPath(file));
            int size = GetSystemMetrics(SM_CXSMICON);
            if (size > 0)
            {
                try { return new Icon(fs, size, size); }
                catch (ArgumentException) { fs.Position = 0; }
            }
            fs.Position = 0;
            return new Icon(fs);
        }
        catch (Exception ex)
        {
            TrayLog($"LoadTrayIcon({file}) 失败: {ex.Message}");
            return null;
        }
    }

    private void OnNavSelection(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            Type page = tag switch
            {
                "manual" => typeof(ManualTestPage),
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

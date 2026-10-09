using System.Runtime.InteropServices;
using Microsoft.Windows.AppLifecycle;

namespace LanSpeed.App;

/// <summary>同一 Windows 登录会话只运行一个进程。再次启动唤起已有窗口后退出。</summary>
internal static class SingleInstance
{
    private const string MutexName = @"Local\LanSpeed.SingleInstance";
    private const string ShowEventName = @"Local\LanSpeed.ShowMainWindow";
    private const string InstanceKey = "LanSpeed.Main";

    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;

    /// <summary>成为唯一实例则返回 true。已有实例时通知它显示主窗口并返回 false。</summary>
    public static bool TryAcquire()
    {
        if (!TryOwnMutex())
        {
            SignalExisting();
            return false;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        TryRegisterActivation();
        return true;
    }

    /// <summary>在 UI 线程就绪后监听「显示主窗口」信号。</summary>
    public static void Listen(MainWindow window)
    {
        var queue = window.DispatcherQueue;
        var thread = new Thread(() =>
        {
            var show = _showEvent;
            if (show is null)
            {
                return;
            }
            while (show.WaitOne())
            {
                if (Services.AppServices.Exiting)
                {
                    break;
                }
                queue.TryEnqueue(window.ShowMainWindow);
            }
        })
        {
            IsBackground = true,
            Name = "LanSpeed.ShowMainWindow",
        };
        thread.Start();
    }

    private static bool TryOwnMutex()
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out var createdNew);
            if (createdNew)
            {
                return true;
            }

            try
            {
                // 上次进程崩溃会遗弃互斥量：接管后继续启动，避免锁死
                if (_mutex.WaitOne(TimeSpan.Zero))
                {
                    return true;
                }
            }
            catch (AbandonedMutexException)
            {
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Log($"互斥量不可用，仍允许启动: {ex.Message}");
            return true;
        }
    }

    private static void SignalExisting()
    {
        AllowSetForegroundWindow(-1);
        for (var i = 0; i < 25; i++)
        {
            try
            {
                using var show = EventWaitHandle.OpenExisting(ShowEventName);
                show.Set();
                break;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(100);
            }
        }

        try
        {
            var key = AppInstance.FindOrRegisterForKey(InstanceKey);
            if (key.IsCurrent)
            {
                return;
            }
            var args = AppInstance.GetCurrent().GetActivatedEventArgs();
            using var done = new ManualResetEventSlim(false);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    key.RedirectActivationToAsync(args).AsTask().Wait(TimeSpan.FromSeconds(2));
                }
                catch (Exception ex)
                {
                    Log($"激活转发失败: {ex.Message}");
                }
                finally
                {
                    done.Set();
                }
            });
            done.Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            Log($"查找已有实例失败: {ex.Message}");
        }
    }

    private static void TryRegisterActivation()
    {
        try
        {
            var key = AppInstance.FindOrRegisterForKey(InstanceKey);
            if (!key.IsCurrent)
            {
                return;
            }
            key.Activated += (_, _) =>
            {
                var window = App.MainWnd;
                if (window is null)
                {
                    return;
                }
                window.DispatcherQueue.TryEnqueue(window.ShowMainWindow);
            };
        }
        catch (Exception ex)
        {
            Log($"单实例注册失败，仍靠互斥量拦截: {ex.Message}");
        }
    }

    private static void Log(string message)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanSpeed", "ui-debug.log");
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} SingleInstance: {message}\n");
        }
        catch
        {
            // 日志失败不影响启动
        }
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
}

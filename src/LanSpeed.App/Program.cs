using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace LanSpeed.App;

/// <summary>自定义入口：先占住本会话唯一实例，再启动 WinUI（§2 运行方式）。</summary>
public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        PriSidecar.Ensure();
        if (!SingleInstance.TryAcquire())
        {
            return;
        }

        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}

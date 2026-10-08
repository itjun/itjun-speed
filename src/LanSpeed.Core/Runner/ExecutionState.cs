using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LanSpeed.Core.Runner;

/// <summary>
/// SetThreadExecutionState 阻止系统睡眠（§6.1）。仅 Windows。
/// ES_CONTINUOUS 只作用于调用线程，因此在专用线程上持有状态，Dispose 时同线程恢复。
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ExecutionState
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;

    public static IDisposable PreventSleep()
    {
        var done = new ManualResetEvent(false);
        var thread = new Thread(() =>
        {
            SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
            done.WaitOne();
            SetThreadExecutionState(ES_CONTINUOUS);
        })
        {
            IsBackground = true,
            Name = "lanspeed-keep-awake",
        };
        thread.Start();
        return new RestoreScope(done);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    private sealed class RestoreScope(ManualResetEvent done) : IDisposable
    {
        public void Dispose() => done.Set();
    }
}

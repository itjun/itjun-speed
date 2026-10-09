using System.Diagnostics;

namespace LanSpeed.App.Services;

/// <summary>延迟启动已下载的 exe。当前进程仍占着单实例锁，必须先退出。</summary>
internal static class UpdateInstaller
{
    public static void Launch(string exePath)
    {
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException("找不到已下载的程序。", exePath);
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c ping 127.0.0.1 -n 3 >nul & start \"\" \"" + exePath + "\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("无法启动已下载的程序。");
        }
    }
}

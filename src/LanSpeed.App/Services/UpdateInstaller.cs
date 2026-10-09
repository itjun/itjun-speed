using System.Diagnostics;

namespace LanSpeed.App.Services;

/// <summary>把已下载的 MSIX 交给系统「应用安装程序」，不经过控制接口。</summary>
internal static class UpdateInstaller
{
    public static void Launch(string msixPath)
    {
        if (!File.Exists(msixPath))
        {
            throw new FileNotFoundException("找不到已下载的安装包。", msixPath);
        }

        try
        {
            Process.Start(new ProcessStartInfo(msixPath) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("无法打开安装包。请确认已安装「应用安装程序」。");
        }
    }
}

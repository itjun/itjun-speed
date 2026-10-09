using System.Reflection;
using System.Runtime.InteropServices;

namespace LanSpeed.App;

/// <summary>
/// WinUI 从 exe 旁边读取同名 .pri。单文件发布把 pri 嵌进 exe，启动时释放到旁边。
/// </summary>
internal static class PriSidecar
{
    public const string ResourceName = "LanSpeed.App.pri";

    public static void Ensure()
    {
        using Stream? bundled = typeof(PriSidecar).Assembly.GetManifestResourceStream(ResourceName);
        if (bundled is null)
        {
            return;
        }

        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            Fail("找不到当前程序路径，无法释放界面资源。");
            return;
        }

        using var memory = new MemoryStream();
        bundled.CopyTo(memory);
        byte[] bytes = memory.ToArray();
        string priPath = Path.ChangeExtension(exe, ".pri");
        if (SameFile(priPath, bytes))
        {
            return;
        }

        try
        {
            string temporary = priPath + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, priPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail("无法在程序旁边写出界面资源（" + Path.GetFileName(priPath) + "）。请把 exe 放到可写的文件夹后再运行。");
        }
    }

    private static bool SameFile(string path, byte[] bytes)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            byte[] existing = File.ReadAllBytes(path);
            return existing.AsSpan().SequenceEqual(bytes);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static void Fail(string message)
    {
        _ = MessageBoxW(IntPtr.Zero, message, "内网测速", 0x00000010);
        throw new InvalidOperationException(message);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}

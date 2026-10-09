using System.Diagnostics;
using System.Text;

namespace LanSpeed.Core.Tests.Update;

public class PublishScriptTests
{
    [Fact]
    public async Task CheckOnly_AcceptsRepoVersions()
    {
        (int code, string text) = await Run("-CheckOnly");
        Assert.True(code == 0, text);
        Assert.Contains("lanspeed-ok", text);
        Assert.Contains("amd64", text);
    }

    [Fact]
    public async Task CheckOnly_RejectsMinimumAboveRelease()
    {
        (int code, string text) = await Run("-CheckOnly", "-MinVersion", "9.0.0");
        Assert.NotEqual(0, code);
        Assert.Contains("lanspeed-min-too-high", text);
    }

    [Fact]
    public async Task CheckOnly_RejectsProductVersionThatDoesNotMatchMsix()
    {
        (int code, string text) = await Run("-CheckOnly", "-Version", "0.0.1");
        Assert.NotEqual(0, code);
        Assert.Contains("lanspeed-version-mismatch", text);
    }

    private static async Task<(int Code, string Text)> Run(params string[] args)
    {
        string root = RepoRoot();
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(Path.Combine(root, "packaging", "publish.ps1"));
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 powershell。");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        bool exited = process.WaitForExit(20000);
        string text = await stdout + await stderr;
        Assert.True(exited, text);
        return (process.ExitCode, text);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LanSpeed.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("找不到仓库根目录。");
    }
}

namespace LanSpeed.Core.Update;

/// <summary>把受信任的 amd64 exe 下载到本地更新目录。</summary>
public static class UpdateDownloader
{
    public const long MaxBytes = 400L * 1024 * 1024;

    public static async Task<string> DownloadAsync(
        HttpClient http,
        string url,
        string fileName,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!GitHubReleaseParser.IsTrustedPackageUrl(url))
        {
            throw new InvalidOperationException("程序地址不在 itjun/itjun-speed 的 amd64 发布范围内。");
        }

        if (!GitHubReleaseParser.IsAmd64PackageName(fileName))
        {
            throw new InvalidOperationException("程序文件名必须是 amd64 的 .exe。");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("LanSpeed");
        using HttpResponseMessage response = await http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"下载程序失败（{(int)response.StatusCode}）。");
        }

        if (response.Content.Headers.ContentLength is > MaxBytes)
        {
            throw new InvalidOperationException("程序超过大小限制。");
        }

        Directory.CreateDirectory(destinationDirectory);
        string path = Path.Combine(destinationDirectory, fileName);
        string temporary = path + ".partial";
        try
        {
            await using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    int read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                    if (total > MaxBytes)
                    {
                        throw new InvalidOperationException("程序超过大小限制。");
                    }

                    await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporary, path);
            return path;
        }
        catch
        {
            if (File.Exists(temporary))
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (IOException)
                {
                }
            }

            throw;
        }
    }
}

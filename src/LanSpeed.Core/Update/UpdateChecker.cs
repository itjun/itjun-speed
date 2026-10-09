using System.Net;
using System.Net.Http.Headers;

namespace LanSpeed.Core.Update;

/// <summary>一次检查的结果。Error 有值表示没能做出判定；Offer 为空表示尚无 Release。</summary>
public sealed record UpdateCheckResult(string? Error, UpdateOffer? Offer)
{
    public bool Failed => Error is not null;
}

/// <summary>读取 GitHub latest Release，并按 §10.1 判定强制或可选更新。</summary>
public sealed class UpdateChecker
{
    private readonly HttpClient _http;

    public UpdateChecker(HttpClient http) => _http = http;

    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, string? feedOverride, CancellationToken cancellationToken = default)
    {
        if (!AppVersion.TryParse(currentVersion, out AppVersion current))
        {
            return new UpdateCheckResult("无法解析当前版本号。", null);
        }

        if (!TryFeed(feedOverride, out Uri? feed, out string? feedError))
        {
            return new UpdateCheckResult(feedError, null);
        }

        HttpResponseMessage response;
        try
        {
            response = await SendAsync(feed, githubJson: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new UpdateCheckResult("无法连接 GitHub 发布源。", null);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(null, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult($"发布源返回 {(int)response.StatusCode}。", null);
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!GitHubReleaseParser.TryParseRelease(body, out ParsedRelease? release, out string? error) || release is null)
            {
                return new UpdateCheckResult(error ?? "发布信息无效。", null);
            }

            UpdateManifest? manifest = null;
            ReleaseAsset? manifestAsset = release.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, GitHubReleaseParser.ManifestName, StringComparison.OrdinalIgnoreCase));
            if (manifestAsset is not null)
            {
                if (!GitHubReleaseParser.IsTrustedManifestUrl(manifestAsset.DownloadUrl))
                {
                    return new UpdateCheckResult("update.json 的地址不受信任。", null);
                }

                (UpdateManifest? parsedManifest, UpdateCheckResult? manifestError) =
                    await ReadManifestAsync(manifestAsset.DownloadUrl, cancellationToken).ConfigureAwait(false);
                if (manifestError is not null)
                {
                    return manifestError;
                }

                manifest = parsedManifest;
            }

            try
            {
                return new UpdateCheckResult(null, UpdatePolicy.Evaluate(current, release, manifest));
            }
            catch (InvalidOperationException ex)
            {
                return new UpdateCheckResult(ex.Message, null);
            }
        }
    }

    private async Task<(UpdateManifest? Manifest, UpdateCheckResult? Error)> ReadManifestAsync(string url, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await SendAsync(new Uri(url), githubJson: false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (null, new UpdateCheckResult("无法下载 update.json。", null));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return (null, new UpdateCheckResult("无法下载 update.json。", null));
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!GitHubReleaseParser.TryParseManifest(json, out UpdateManifest? manifest, out string? error) || manifest is null)
            {
                return (null, new UpdateCheckResult(error ?? "update.json 无效。", null));
            }

            return (manifest, null);
        }
    }

    private static bool TryFeed(string? feedOverride, out Uri feed, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(feedOverride))
        {
            feed = UpdateSources.LatestReleaseUri;
            return true;
        }

        if (!Uri.TryCreate(feedOverride.Trim(), UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            feed = UpdateSources.LatestReleaseUri;
            error = "更新源必须是 https 地址。";
            return false;
        }

        feed = uri;
        return true;
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, bool githubJson, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("LanSpeed");
        if (githubJson)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        }

        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }
}

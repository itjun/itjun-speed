using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LanSpeed.Core.Update;

/// <summary>GitHub Release 里的一个资产。</summary>
public sealed record ReleaseAsset(string Name, string DownloadUrl);

/// <summary>从 GitHub Release JSON 解析出的发布。</summary>
public sealed record ParsedRelease(
    AppVersion Version,
    AppVersion? MinimumFromBody,
    IReadOnlyList<ReleaseAsset> Assets,
    string? Notes);

/// <summary>随发布上传的 update.json。arch 只能是 amd64。</summary>
public sealed record UpdateManifest(AppVersion Version, AppVersion Minimum, string File);

/// <summary>解析 GitHub Release 与 update.json，并只接受本仓库的 amd64 安装包地址。</summary>
public static partial class GitHubReleaseParser
{
    public const string ManifestName = "update.json";

    public static bool TryParseRelease(string json, out ParsedRelease? release, out string? error)
    {
        release = null;
        error = null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("tag_name", out JsonElement tagEl) || tagEl.ValueKind != JsonValueKind.String)
            {
                error = "发布信息缺少 tag_name。";
                return false;
            }

            if (!AppVersion.TryParse(tagEl.GetString(), out AppVersion version))
            {
                error = "发布标签不是有效版本号。";
                return false;
            }

            string? body = root.TryGetProperty("body", out JsonElement bodyEl) && bodyEl.ValueKind == JsonValueKind.String
                ? bodyEl.GetString()
                : null;
            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out JsonElement assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assetsEl.EnumerateArray())
                {
                    if (!asset.TryGetProperty("name", out JsonElement nameEl) || nameEl.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    if (!asset.TryGetProperty("browser_download_url", out JsonElement urlEl) || urlEl.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string? name = nameEl.GetString();
                    string? url = urlEl.GetString();
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                    {
                        continue;
                    }

                    assets.Add(new ReleaseAsset(name, url));
                }
            }

            release = new ParsedRelease(version, ParseMinimumFromBody(body), assets, NotesWithoutMinimum(body));
            return true;
        }
        catch (JsonException)
        {
            error = "发布信息不是有效的 JSON。";
            return false;
        }
    }

    public static bool TryParseManifest(string json, out UpdateManifest? manifest, out string? error)
    {
        manifest = null;
        error = null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (!TryString(root, "version", out string? versionText) || !AppVersion.TryParse(versionText, out AppVersion version))
            {
                error = "update.json 缺少有效的 version。";
                return false;
            }

            if (!TryString(root, "minVersion", out string? minText) || !AppVersion.TryParse(minText, out AppVersion minimum))
            {
                error = "update.json 缺少有效的 minVersion。";
                return false;
            }

            if (!TryString(root, "arch", out string? arch) || !IsAmd64Arch(arch))
            {
                error = "update.json 的 arch 必须是 amd64。";
                return false;
            }

            if (!TryString(root, "file", out string? file) || !IsAmd64PackageName(file))
            {
                error = "update.json 的 file 必须是 amd64 的 .msix。";
                return false;
            }

            manifest = new UpdateManifest(version, minimum, file);
            return true;
        }
        catch (JsonException)
        {
            error = "update.json 不是有效的 JSON。";
            return false;
        }
    }

    /// <summary>没有 update.json 时，在 amd64 包里优先 win-x64 文件名。</summary>
    public static ReleaseAsset? SelectAmd64Package(IReadOnlyList<ReleaseAsset> assets) =>
        assets
            .Where(asset => IsAmd64PackageName(asset.Name) && IsTrustedPackageUrl(asset.DownloadUrl))
            .OrderBy(asset => Rank(asset.Name))
            .ThenBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    public static bool IsAmd64PackageName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string file = name.Replace('\\', '/');
        if (file.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        string lower = file.ToLowerInvariant();
        if (!lower.EndsWith(".msix", StringComparison.Ordinal))
        {
            return false;
        }

        if (lower.Contains("arm", StringComparison.Ordinal))
        {
            return false;
        }

        return lower.Contains("win-x64", StringComparison.Ordinal)
            || lower.Contains("amd64", StringComparison.Ordinal)
            || lower.Contains("_x64", StringComparison.Ordinal)
            || lower.Contains("-x64", StringComparison.Ordinal);
    }

    public static bool IsAmd64Arch(string? arch)
    {
        if (string.IsNullOrWhiteSpace(arch))
        {
            return false;
        }

        string value = arch.Trim().ToLowerInvariant();
        return value is "amd64" or "x64" or "win-x64";
    }

    /// <summary>只接受本仓库 Releases 下载地址上的 amd64 msix，拒绝查询串和其他主机。</summary>
    public static bool IsTrustedPackageUrl(string? url) => IsTrustedAssetUrl(url, IsAmd64PackageName);

    public static bool IsTrustedManifestUrl(string? url) =>
        IsTrustedAssetUrl(url, name => string.Equals(name, ManifestName, StringComparison.OrdinalIgnoreCase));

    public static AppVersion? ParseMinimumFromBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        Match match = MinimumLine().Match(body);
        if (!match.Success || !AppVersion.TryParse(match.Groups[1].Value, out AppVersion version))
        {
            return null;
        }

        return version;
    }

    private static bool IsTrustedAssetUrl(string? url, Func<string, bool> fileNameOk)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 6)
        {
            return false;
        }

        if (!parts[0].Equals(UpdateSources.Owner, StringComparison.OrdinalIgnoreCase)
            || !parts[1].Equals(UpdateSources.Repo, StringComparison.OrdinalIgnoreCase)
            || !parts[2].Equals("releases", StringComparison.OrdinalIgnoreCase)
            || !parts[3].Equals("download", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(parts[4]))
        {
            return false;
        }

        return fileNameOk(Uri.UnescapeDataString(parts[5]));
    }

    private static string? NotesWithoutMinimum(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        string notes = MinimumLine().Replace(body, string.Empty).Trim();
        return notes.Length == 0 ? null : notes;
    }

    private static int Rank(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.Contains("win-x64", StringComparison.Ordinal))
        {
            return 0;
        }

        if (lower.Contains("amd64", StringComparison.Ordinal))
        {
            return 1;
        }

        return 2;
    }

    private static bool TryString(JsonElement root, string name, [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out JsonElement element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    [GeneratedRegex(@"(?m)^\s*minVersion\s*:\s*([vV]?\d+(?:\.\d+){1,3})\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex MinimumLine();
}

/// <summary>官方发布源。下载地址必须落在这个仓库。</summary>
public static class UpdateSources
{
    public const string Owner = "itjun";

    public const string Repo = "itjun-speed";

    public static readonly Uri LatestReleaseUri = new("https://api.github.com/repos/itjun/itjun-speed/releases/latest");
}

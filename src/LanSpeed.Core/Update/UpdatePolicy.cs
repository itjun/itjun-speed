namespace LanSpeed.Core.Update;

/// <summary>更新要求。低于最低版本为强制，否则在有新版本时为可选。</summary>
public enum UpdateRequirement
{
    /// <summary>已是发布版本，或比发布版本更新。</summary>
    None = 0,

    /// <summary>有新版本，且当前版本不低于最低版本。</summary>
    Optional = 1,

    /// <summary>当前版本低于最低版本，必须更新后才能继续使用。</summary>
    Required = 2,
}

/// <summary>一次发布里可供安装的结果。DownloadUrl 为空表示没有可装的更高版本 amd64 包。</summary>
public sealed record UpdateOffer(
    UpdateRequirement Requirement,
    AppVersion Current,
    AppVersion Latest,
    AppVersion? Minimum,
    string? DownloadUrl,
    string? FileName,
    string? Notes);

/// <summary>版本政策（§10.1）。字符串相等不算版本比较。</summary>
public static class UpdatePolicy
{
    public static UpdateRequirement Decide(AppVersion current, AppVersion latest, AppVersion? minimum)
    {
        if (minimum is { } floor && current < floor)
        {
            return UpdateRequirement.Required;
        }

        if (latest > current)
        {
            return UpdateRequirement.Optional;
        }

        return UpdateRequirement.None;
    }

    /// <summary>
    /// 有 update.json 时以其 minVersion 与 file 为准；否则用发行说明里的最低版本，并在资产里自选 amd64 包。
    /// 只有最新版本高于当前、且包地址受信任时才给出下载地址。
    /// </summary>
    public static UpdateOffer Evaluate(AppVersion current, ParsedRelease release, UpdateManifest? manifest)
    {
        if (manifest is not null && manifest.Version != release.Version)
        {
            throw new InvalidOperationException("update.json 的 version 与发布标签不一致。");
        }

        AppVersion? minimum = manifest is not null ? manifest.Minimum : release.MinimumFromBody;
        ReleaseAsset? package = SelectPackage(release, manifest);
        bool installable = package is not null && release.Version > current;
        return new UpdateOffer(
            Decide(current, release.Version, minimum),
            current,
            release.Version,
            minimum,
            installable ? package!.DownloadUrl : null,
            installable ? package!.Name : null,
            release.Notes);
    }

    private static ReleaseAsset? SelectPackage(ParsedRelease release, UpdateManifest? manifest)
    {
        if (manifest is not null)
        {
            return release.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, manifest.File, StringComparison.OrdinalIgnoreCase)
                && GitHubReleaseParser.IsAmd64PackageName(asset.Name)
                && GitHubReleaseParser.IsTrustedPackageUrl(asset.DownloadUrl));
        }

        return GitHubReleaseParser.SelectAmd64Package(release.Assets);
    }
}

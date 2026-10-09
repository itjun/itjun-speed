using System.Net;
using System.Text;
using LanSpeed.Core.Update;

namespace LanSpeed.Core.Tests.Update;

public class GitHubReleaseParserTests
{
    [Fact]
    public void ReleaseJson_ReadsMinimumAndDropsArm()
    {
        const string json = """
            {
              "tag_name": "v0.3.0",
              "body": "minVersion: 0.2.0\r\n\r\n修复扫描。",
              "assets": [
                {
                  "name": "LanSpeed-0.3.0-win-arm64.exe",
                  "browser_download_url": "https://github.com/itjun/itjun-speed/releases/download/v0.3.0/LanSpeed-0.3.0-win-arm64.exe"
                },
                {
                  "name": "LanSpeed.App_0.3.0.0_x64.exe",
                  "browser_download_url": "https://github.com/itjun/itjun-speed/releases/download/v0.3.0/LanSpeed.App_0.3.0.0_x64.exe"
                },
                {
                  "name": "update.json",
                  "browser_download_url": "https://github.com/itjun/itjun-speed/releases/download/v0.3.0/update.json"
                }
              ]
            }
            """;

        Assert.True(GitHubReleaseParser.TryParseRelease(json, out ParsedRelease? release, out string? error), error);
        Assert.NotNull(release);
        Assert.Equal(new AppVersion(0, 3, 0), release.Version);
        Assert.Equal(new AppVersion(0, 2, 0), release.MinimumFromBody);
        Assert.Equal("修复扫描。", release.Notes);
        ReleaseAsset? package = GitHubReleaseParser.SelectAmd64Package(release.Assets);
        Assert.NotNull(package);
        Assert.Equal("LanSpeed.App_0.3.0.0_x64.exe", package.Name);
    }

    [Fact]
    public void Manifest_RequiresAmd64()
    {
        const string json = """
            {"version":"0.3.0","minVersion":"0.2.0","arch":"amd64","file":"LanSpeed-0.3.0-win-x64.exe"}
            """;
        Assert.True(GitHubReleaseParser.TryParseManifest(json, out UpdateManifest? manifest, out string? error), error);
        Assert.Equal(new AppVersion(0, 2, 0), manifest!.Minimum);

        const string arm = """
            {"version":"0.3.0","minVersion":"0.2.0","arch":"arm64","file":"LanSpeed-0.3.0-win-arm64.exe"}
            """;
        Assert.False(GitHubReleaseParser.TryParseManifest(arm, out _, out string? armError));
        Assert.Contains("amd64", armError);
    }

    [Theory]
    [InlineData("https://github.com/itjun/itjun-speed/releases/download/v0.2.0/LanSpeed-0.2.0-win-x64.exe", true)]
    [InlineData("https://github.com/itjun/itjun-speed/releases/download/v0.2.0/LanSpeed.App_0.2.0.0_x64.exe", true)]
    [InlineData("https://github.com/itjun/itjun-speed/releases/download/v0.2.0/LanSpeed-0.2.0-win-arm64.exe", false)]
    [InlineData("https://github.com/other/itjun-speed/releases/download/v0.2.0/LanSpeed-0.2.0-win-x64.exe", false)]
    [InlineData("http://github.com/itjun/itjun-speed/releases/download/v0.2.0/LanSpeed-0.2.0-win-x64.exe", false)]
    [InlineData("https://github.com/itjun/itjun-speed/releases/download/v0.2.0/LanSpeed-0.2.0-win-x64.exe?raw=1", false)]
    [InlineData("https://example.com/LanSpeed-0.2.0-win-x64.exe", false)]
    public void PackageUrl_OnlyOfficialAmd64(string url, bool trusted) =>
        Assert.Equal(trusted, GitHubReleaseParser.IsTrustedPackageUrl(url));

    [Fact]
    public async Task Checker_OptionalWhenAtMinimum_RequiredWhenBelow()
    {
        UpdateOffer optional = await Check("0.2.0", Manifest("0.2.0"));
        Assert.Equal(UpdateRequirement.Optional, optional.Requirement);
        Assert.EndsWith("LanSpeed-0.3.0-win-x64.exe", optional.DownloadUrl, StringComparison.Ordinal);

        UpdateOffer required = await Check("0.1.0", Manifest("0.2.0"));
        Assert.Equal(UpdateRequirement.Required, required.Requirement);
        Assert.NotNull(required.DownloadUrl);
    }

    [Fact]
    public async Task Checker_ManifestMismatch_IsAnError()
    {
        using HttpClient http = Client(_ => Json("""{"version":"9.0.0","minVersion":"0.1.0","arch":"amd64","file":"LanSpeed-0.3.0-win-x64.exe"}"""));
        var result = await new UpdateChecker(http).CheckAsync("0.2.0", feedOverride: null);
        Assert.True(result.Failed);
        Assert.Contains("不一致", result.Error);
    }

    [Fact]
    public async Task Checker_NotFound_DoesNotForce()
    {
        var handler = new QueueHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);
        var result = await new UpdateChecker(http).CheckAsync("0.1.0", feedOverride: null);
        Assert.False(result.Failed);
        Assert.Null(result.Offer);
    }

    [Fact]
    public async Task Checker_HttpFeedRejected()
    {
        using HttpClient http = Client(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var result = await new UpdateChecker(http).CheckAsync("0.2.0", "http://example.com/releases/latest");
        Assert.Contains("https", result.Error);
    }

    [Fact]
    public async Task Downloader_RejectsUntrustedUrl_WithoutSending()
    {
        var handler = new QueueHandler();
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => UpdateDownloader.DownloadAsync(
            http,
            "https://example.com/LanSpeed-0.2.0-win-x64.exe",
            "LanSpeed-0.2.0-win-x64.exe",
            Path.GetTempPath()));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Downloader_WritesAmd64Package()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lanspeed-update-test-" + Guid.NewGuid().ToString("n"));
        var handler = new QueueHandler();
        handler.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("msix"u8.ToArray()),
            };
            return response;
        });
        using var http = new HttpClient(handler);
        string path = await UpdateDownloader.DownloadAsync(
            http,
            "https://github.com/itjun/itjun-speed/releases/download/v0.2.0/LanSpeed-0.2.0-win-x64.exe",
            "LanSpeed-0.2.0-win-x64.exe",
            dir);
        try
        {
            Assert.Equal("msix", await File.ReadAllTextAsync(path));
            Assert.False(File.Exists(path + ".partial"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task<UpdateOffer> Check(string current, string manifestJson)
    {
        using HttpClient http = Client(_ => Json(manifestJson));
        UpdateCheckResult result = await new UpdateChecker(http).CheckAsync(current, feedOverride: null);
        Assert.False(result.Failed, result.Error);
        Assert.NotNull(result.Offer);
        return result.Offer;
    }

    private static string Manifest(string minimum) =>
        $$"""{"version":"0.3.0","minVersion":"{{minimum}}","arch":"amd64","file":"LanSpeed-0.3.0-win-x64.exe"}""";

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> manifest)
    {
        var handler = new QueueHandler();
        handler.Enqueue(_ => Json(ReleaseJson()));
        handler.Enqueue(manifest);
        return new HttpClient(handler);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static string ReleaseJson() => """
        {
          "tag_name": "v0.3.0",
          "body": "minVersion: 0.0.1",
          "assets": [
            {
              "name": "LanSpeed-0.3.0-win-arm64.exe",
              "browser_download_url": "https://github.com/itjun/itjun-speed/releases/download/v0.3.0/LanSpeed-0.3.0-win-arm64.exe"
            },
            {
              "name": "LanSpeed-0.3.0-win-x64.exe",
              "browser_download_url": "https://github.com/itjun/itjun-speed/releases/download/v0.3.0/LanSpeed-0.3.0-win-x64.exe"
            },
            {
              "name": "update.json",
              "browser_download_url": "https://github.com/itjun/itjun-speed/releases/download/v0.3.0/update.json"
            }
          ]
        }
        """;

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public List<Uri> Requests { get; } = [];

        public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> response) => _responses.Enqueue(response);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(_responses.Dequeue()(request));
        }
    }
}

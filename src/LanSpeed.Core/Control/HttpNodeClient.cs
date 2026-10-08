using System.Net.Http.Json;
using System.Text.Json;
using LanSpeed.Core.Runner;

namespace LanSpeed.Core.Control;

/// <summary>HTTP 远程节点客户端：client/run 以流式响应逐行读取（§5）。</summary>
public sealed class HttpNodeClient : INodeClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public HttpNodeClient(string ip, int ctrlPort, int timeoutSeconds = 15)
    {
        Endpoint = $"http://{ip}:{ctrlPort}";
        _http = new HttpClient
        {
            BaseAddress = new Uri(Endpoint + "/"),
            // 流式响应不能设整体超时，逐请求用 CTS 控制
            Timeout = Timeout.InfiniteTimeSpan,
        };
        DefaultTimeout = TimeSpan.FromSeconds(timeoutSeconds);
    }

    private TimeSpan DefaultTimeout { get; }

    public string Endpoint { get; }

    public async Task<NodeHello> HelloAsync(CancellationToken ct = default) =>
        await GetAsync<NodeHello>("v1/hello", ct) ?? throw new IOException("hello 无响应");

    public async Task<List<NodeAddrDto>> AddrsAsync(CancellationToken ct = default) =>
        await GetAsync<List<NodeAddrDto>>("v1/addrs", ct) ?? throw new IOException("addrs 无响应");

    public async Task<ServerStartResponse> ServerStartAsync(int port, int maxSeconds, CancellationToken ct = default)
    {
        using var resp = await PostAsync("v1/server/start", new ServerStartRequest(port, maxSeconds), ct);
        return await resp.Content.ReadFromJsonAsync<ServerStartResponse>(JsonOpts, ct)
               ?? throw new IOException("server/start 无响应");
    }

    public async Task ServerStopAsync(string handle, CancellationToken ct = default)
    {
        using var _ = await PostAsync("v1/server/stop", new ServerStopRequest(handle), ct);
    }

    public async IAsyncEnumerable<string> ClientRunAsync(
        ClientRunRequest req, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // 覆盖时长上限 + 忽略秒 + 余量；正常情况下流结束早于超时
        timeout.CancelAfter(TimeSpan.FromSeconds(req.Params.Duration + req.Params.Omit + 60));
        using var resp = await PostAsync("v1/client/run", req, timeout.Token);
        var stream = await resp.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            yield return line;
        }
    }

    public async Task ClientStopAsync(CancellationToken ct = default)
    {
        using var _ = await PostAsync("v1/client/stop", "{}", ct);
    }

    public async Task<ProbeResponse> ProbeAsync(string target, int port, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        using var resp = await PostAsync("v1/probe", new ProbeRequest(target, port), cts.Token);
        return await resp.Content.ReadFromJsonAsync<ProbeResponse>(JsonOpts, cts.Token)
               ?? throw new IOException("probe 无响应");
    }

    public async Task<PingResponse> PingAsync(string target, CancellationToken ct = default)
    {
        using var resp = await PostAsync("v1/ping", new PingRequest(target), ct);
        return await resp.Content.ReadFromJsonAsync<PingResponse>(JsonOpts, ct)
               ?? throw new IOException("ping 无响应");
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DefaultTimeout);
        using var resp = await _http.GetAsync(path, cts.Token);
        EnsureSuccess(resp, path);
        return await resp.Content.ReadFromJsonAsync<T>(JsonOpts, cts.Token);
    }

    private async Task<HttpResponseMessage> PostAsync<T>(string path, T body, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync(path, body, JsonOpts, ct);
        EnsureSuccess(resp, path);
        return resp;
    }

    // 409 busy 转为 BusyException；其余非 2xx 带上响应体，避免被静默解析成默认值（如 port=0）
    private static void EnsureSuccess(HttpResponseMessage resp, string path)
    {
        if (resp.IsSuccessStatusCode)
        {
            return;
        }
        int code = (int)resp.StatusCode;
        string text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        resp.Dispose();
        if (code == 409)
        {
            throw new BusyException($"节点忙（{path}）：已有测速在运行");
        }
        throw new HttpRequestException($"HTTP {code}（{path}）：{text}");
    }

    public void Dispose() => _http.Dispose();
}

using LanSpeed.Core.Net;
using LanSpeed.Core.Runner;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LanSpeed.Core.Control;

/// <summary>控制接口（§5）：Kestrel Minimal API，TCP 39301。不使用 HttpListener（需要管理员 urlacl）。</summary>
public sealed class ControlServer(NodeOps ops) : IAsyncDisposable
{
    private WebApplication? _app;

    public async Task StartAsync(int port, CancellationToken ct = default)
    {
        if (_app != null)
        {
            throw new InvalidOperationException("控制接口已在运行");
        }
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(port));
        builder.Logging.ClearProviders();
        var app = builder.Build();

        // 只接受私有地址段（含回环，供本机自测）的请求，其余 403
        app.Use(async (ctx, next) =>
        {
            var remote = ctx.Connection.RemoteIpAddress;
            if (remote is null || !AddrClassify.IsPrivateOrLoopback(remote))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsync("forbidden: only private networks", ct);
                return;
            }
            // 「允许被测」关闭后只保留 hello，其余 403（§9）
            if (!ops.Accept && !ctx.Request.Path.StartsWithSegments("/v1/hello"))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsync("forbidden: node is not accepting tests", ct);
                return;
            }
            await next();
        });

        app.MapGet("/v1/hello", () => Results.Json(ops.Hello()));

        app.MapGet("/v1/addrs", () => Results.Json(ops.Hello().Ips));

        app.MapPost("/v1/server/start", async (ServerStartRequest req) =>
        {
            try
            {
                int port = await ops.ServerStartAsync(req.Port, req.MaxSeconds);
                return Results.Json(new ServerStartResponse(port, Guid.NewGuid().ToString("N")));
            }
            catch (BusyException)
            {
                return Results.Conflict(new { error = "busy" });
            }
        });

        app.MapPost("/v1/server/stop", async (ServerStopRequest _) =>
        {
            await ops.ServerStopAsync();
            return Results.Ok(new { });
        });

        app.MapPost("/v1/client/run", async (ClientRunRequest req, HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            try
            {
                // HTTP 连接断开（RequestAborted）时立即杀掉 iperf3 客户端
                await foreach (var line in ops.ClientRunAsync(req, ctx.RequestAborted))
                {
                    await ctx.Response.WriteAsync(line + "\n", ctx.RequestAborted);
                }
            }
            catch (BusyException)
            {
                ctx.Response.StatusCode = StatusCodes.Status409Conflict;
                await ctx.Response.WriteAsync("{\"error\":\"busy\"}");
            }
            catch (OperationCanceledException)
            {
                // 连接中断：流到此为止
            }
        });

        app.MapPost("/v1/client/stop", async () =>
        {
            await ops.ClientStopAsync();
            return Results.Ok(new { });
        });

        app.MapPost("/v1/probe", async (ProbeRequest req) =>
            Results.Json(await ops.ProbeAsync(req.Target, req.Port)));

        app.MapPost("/v1/ping", async (PingRequest req) =>
            Results.Json(await ops.PingAsync(req.Target)));

        await app.StartAsync(ct);
        _app = app;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
    }
}

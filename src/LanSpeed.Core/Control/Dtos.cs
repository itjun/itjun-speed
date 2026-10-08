using System.Text.Json.Serialization;

namespace LanSpeed.Core.Control;

// 控制接口（§5）与发现应答（§4.1）共用的 DTO。

/// <summary>节点信息；字段名与 §4.1 发现应答保持一致。</summary>
public sealed record NodeHello(
    [property: JsonPropertyName("t")] string T,
    [property: JsonPropertyName("v")] int V,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("iperf")] string Iperf,
    [property: JsonPropertyName("ips")] List<NodeAddrDto> Ips,
    [property: JsonPropertyName("ctrlPort")] int CtrlPort,
    [property: JsonPropertyName("accept")] bool Accept,
    [property: JsonPropertyName("busy")] bool Busy);

/// <summary>网卡地址：IP、前缀、网卡名、协商速率（bit/s）、是否默认路由网卡。</summary>
public sealed record NodeAddrDto(
    [property: JsonPropertyName("ip")] string Ip,
    [property: JsonPropertyName("prefix")] int Prefix,
    [property: JsonPropertyName("iface")] string Iface,
    [property: JsonPropertyName("speedMbps")] long SpeedMbps,
    [property: JsonPropertyName("defaultRoute")] bool DefaultRoute);

public sealed record ServerStartRequest(int Port, int MaxSeconds);

public sealed record ServerStartResponse(int Port, string Handle);

public sealed record ServerStopRequest(string Handle);

public sealed record FlowDto(string ServerSide, string Direction);

public sealed record ClientRunRequest(string Target, int Port, string? BindIp, Iperf.Params Params, FlowDto Flow);

public sealed record ProbeRequest(string Target, int Port);

public sealed record ProbeResponse(bool Ok, double RttMs, string Reason);

public sealed record PingRequest(string Target);

public sealed record PingResponse(double RttMs);

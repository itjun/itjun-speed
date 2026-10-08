using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanSpeed.Core.Iperf;

/// <summary>
/// 把旧版 iperf3 的 -J 经典输出（单个 JSON 文档：start/intervals/end）转换为 --json-stream 事件行，
/// 使不支持 --json-stream 的发行版（如 Ubuntu 24.04 的 3.16）也能走同一条解析路径。
/// 转换在节点本地完成，对发起机透明（代价是没有逐秒实时输出，结束才一次性拿到）。
/// </summary>
public static class ClassicConverter
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static IEnumerable<string> ToJsonStreamLines(string classicJson)
    {
        ClassicDoc? doc;
        try
        {
            doc = JsonSerializer.Deserialize<ClassicDoc>(classicJson, JsonOpts);
        }
        catch (JsonException)
        {
            yield break;
        }
        if (doc == null)
        {
            yield break;
        }
        if (!string.IsNullOrEmpty(doc.Error))
        {
            yield return JsonSerializer.Serialize(new StreamEvent("error", doc.Error));
        }
        foreach (var interval in doc.Intervals ?? [])
        {
            yield return JsonSerializer.Serialize(new StreamEvent("interval", interval));
        }
        if (doc.End != null)
        {
            yield return JsonSerializer.Serialize(new StreamEvent("end", doc.End));
        }
    }

    private sealed record StreamEvent(
        [property: JsonPropertyName("event")] string Event,
        [property: JsonPropertyName("data")] object Data);

    private sealed class ClassicDoc
    {
        [JsonPropertyName("intervals")]
        public List<IntervalData>? Intervals { get; set; }

        [JsonPropertyName("end")]
        public EndData? End { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}

using System.Text.Json;
using LanSpeed.Core.Verdict;

namespace LanSpeed.Core.History;

public sealed record HistoryPair(
    string A,
    string B,
    string Status,
    double Ab,
    double Ba,
    double RttMs,
    int Retransmits,
    double JitterMs,
    double LostPct,
    string Grade,
    string Title);

public sealed record HistoryRecord(
    string Id,
    DateTimeOffset Time,
    string Mode,
    string Protocol,
    int Duration,
    int Parallel,
    string Direction,
    List<HistoryPair> Pairs);

/// <summary>本地历史（§8 历史页的存储层）：单 JSON 文件、新在前、上限 200、.tmp+rename 原子写。</summary>
public sealed class HistoryStore(string? path = null)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private string PathValue => path ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanSpeed", "history.json");

    public List<HistoryRecord> Load()
    {
        try
        {
            if (!File.Exists(PathValue))
            {
                return [];
            }
            return JsonSerializer.Deserialize<List<HistoryRecord>>(File.ReadAllText(PathValue), JsonOpts) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Append(HistoryRecord record)
    {
        var records = Load();
        records.Insert(0, record);
        if (records.Count > 200)
        {
            records.RemoveRange(200, records.Count - 200);
        }
        WriteAll(records);
    }

    public void Delete(string id)
    {
        var records = Load();
        if (records.RemoveAll(r => r.Id == id) > 0)
        {
            WriteAll(records);
        }
    }

    private void WriteAll(List<HistoryRecord> records)
    {
        string full = PathValue;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        string tmp = full + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(records, JsonOpts));
        if (File.Exists(full))
        {
            File.Delete(full);
        }
        File.Move(tmp, full);
    }

    public void WriteCsv(string csvPath)
    {
        using var writer = new StreamWriter(csvPath);
        writer.WriteLine("时间,模式,协议,时长秒,并行流,方向,A,B,状态,A→B Mbps,B→A Mbps,RTT ms,重传,抖动 ms,丢包%,结论,标题");
        foreach (var r in Load())
        {
            foreach (var pair in r.Pairs)
            {
                writer.WriteLine(string.Join(',',
                    Invariant(r.Time.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)),
                    r.Mode, r.Protocol, r.Duration, r.Parallel, r.Direction,
                    pair.A, pair.B, pair.Status,
                    Invariant(Math.Round(pair.Ab / 1e6, 1)), Invariant(Math.Round(pair.Ba / 1e6, 1)),
                    Invariant(Math.Round(pair.RttMs, 2)),
                    pair.Retransmits, Invariant(Math.Round(pair.JitterMs, 2)), Invariant(Math.Round(pair.LostPct, 2)),
                    pair.Grade, QuoteCsv(pair.Title)));
            }
        }
    }

    private static string Invariant<T>(T value) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{value}");

    private static string QuoteCsv(string text) => $"\"{text.Replace("\"", "\"\"")}\"";
}

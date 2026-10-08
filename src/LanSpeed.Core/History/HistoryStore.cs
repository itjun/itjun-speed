using System.Text.Json;
using LanSpeed.Core.Persistence;
using Microsoft.Data.Sqlite;

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

/// <summary>本地历史（SQLite）：新在前、上限 100、CSV 导出。</summary>
public sealed class HistoryStore
{
    public const int MaxRecords = 100;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly AppDb _db;

    public HistoryStore(string? dbPath = null) => _db = new AppDb(dbPath);

    public HistoryStore(AppDb db) => _db = db;

    public List<HistoryRecord> Load()
    {
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT payload FROM history ORDER BY time DESC LIMIT $lim";
            cmd.Parameters.AddWithValue("$lim", MaxRecords);
            var list = new List<HistoryRecord>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var record = JsonSerializer.Deserialize<HistoryRecord>(reader.GetString(0), JsonOpts);
                if (record != null)
                {
                    list.Add(record);
                }
            }
            return list;
        }
        catch (Exception ex) when (ex is IOException or JsonException or SqliteException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Append(HistoryRecord record)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR REPLACE INTO history (id, time, mode, protocol, duration, parallel, direction, payload)
                VALUES ($id, $time, $mode, $proto, $dur, $par, $dir, $payload)
                """;
            cmd.Parameters.AddWithValue("$id", record.Id);
            cmd.Parameters.AddWithValue("$time", record.Time.ToUniversalTime().ToString("O"));
            cmd.Parameters.AddWithValue("$mode", record.Mode);
            cmd.Parameters.AddWithValue("$proto", record.Protocol);
            cmd.Parameters.AddWithValue("$dur", record.Duration);
            cmd.Parameters.AddWithValue("$par", record.Parallel);
            cmd.Parameters.AddWithValue("$dir", record.Direction);
            cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(record, JsonOpts));
            cmd.ExecuteNonQuery();
        }
        using (var trim = conn.CreateCommand())
        {
            trim.Transaction = tx;
            trim.CommandText = """
                DELETE FROM history WHERE id NOT IN (
                  SELECT id FROM history ORDER BY time DESC LIMIT $lim
                )
                """;
            trim.Parameters.AddWithValue("$lim", MaxRecords);
            trim.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void Delete(string id)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM history WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
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

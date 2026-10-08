using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LanSpeed.Core.Persistence;

/// <summary>软配对主机：按节点 ID 记住，重启后按 ID 刷新 IP。</summary>
public sealed record PairedHost(
    string NodeId,
    string Name,
    int CtrlPort,
    IReadOnlyList<string> LastIps,
    long LinkMbps,
    DateTimeOffset RememberedAt,
    DateTimeOffset LastSeenAt);

public sealed class PairedHostStore(AppDb db)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<PairedHost> List()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT node_id, name, ctrl_port, last_ips, link_mbps, remembered_at, last_seen_at
            FROM paired_hosts
            ORDER BY last_seen_at DESC
            """;
        var list = new List<PairedHost>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(Read(reader));
        }
        return list;
    }

    public PairedHost? Get(string nodeId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT node_id, name, ctrl_port, last_ips, link_mbps, remembered_at, last_seen_at
            FROM paired_hosts WHERE node_id = $id
            """;
        cmd.Parameters.AddWithValue("$id", nodeId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public bool IsRemembered(string nodeId) => Get(nodeId) != null;

    /// <summary>记住或更新已配对主机（upsert）。</summary>
    public void Remember(string nodeId, string name, int ctrlPort, IEnumerable<string> ips, long linkMbps)
    {
        var now = DateTimeOffset.UtcNow;
        string ipsJson = JsonSerializer.Serialize(ips.ToList(), JsonOpts);
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO paired_hosts (node_id, name, ctrl_port, last_ips, link_mbps, remembered_at, last_seen_at)
            VALUES ($id, $name, $port, $ips, $link, $rem, $seen)
            ON CONFLICT(node_id) DO UPDATE SET
              name = excluded.name,
              ctrl_port = excluded.ctrl_port,
              last_ips = excluded.last_ips,
              link_mbps = excluded.link_mbps,
              last_seen_at = excluded.last_seen_at
            """;
        cmd.Parameters.AddWithValue("$id", nodeId);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$port", ctrlPort);
        cmd.Parameters.AddWithValue("$ips", ipsJson);
        cmd.Parameters.AddWithValue("$link", linkMbps);
        cmd.Parameters.AddWithValue("$rem", now.ToString("O"));
        cmd.Parameters.AddWithValue("$seen", now.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>扫描见到已配对主机时刷新 IP / 名称 / 链路。</summary>
    public void TouchSeen(string nodeId, string name, int ctrlPort, IEnumerable<string> ips, long linkMbps)
    {
        if (!IsRemembered(nodeId))
        {
            return;
        }
        Remember(nodeId, name, ctrlPort, ips, linkMbps);
    }

    public void Forget(string nodeId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM paired_hosts WHERE node_id = $id";
        cmd.Parameters.AddWithValue("$id", nodeId);
        cmd.ExecuteNonQuery();
    }

    private static PairedHost Read(SqliteDataReader reader)
    {
        string ipsJson = reader.GetString(3);
        var ips = JsonSerializer.Deserialize<List<string>>(ipsJson, JsonOpts) ?? [];
        return new PairedHost(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            ips,
            reader.GetInt64(4),
            DateTimeOffset.Parse(reader.GetString(5)),
            DateTimeOffset.Parse(reader.GetString(6)));
    }
}

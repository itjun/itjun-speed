using Microsoft.Data.Sqlite;

namespace LanSpeed.Core.Persistence;

/// <summary>本地分组（一层机柜）。名称自定，顺序持久化。</summary>
public sealed record HostGroup(string Id, string Name, int SortOrder);

/// <summary>分组成员。一台主机（节点 ID）只属于一个分组。</summary>
public sealed record HostGroupMember(string NodeId, string GroupId, int SortOrder);

/// <summary>发起机本地分组：新建、改名、删除、排序，以及按节点 ID 移入移出。</summary>
public sealed class HostGroupStore(AppDb db)
{
    public IReadOnlyList<HostGroup> ListGroups()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, sort_order FROM host_groups
            ORDER BY sort_order, name
            """;
        var list = new List<HostGroup>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new HostGroup(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        }
        return list;
    }

    public IReadOnlyList<HostGroupMember> ListMembers()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT node_id, group_id, sort_order FROM host_group_members
            ORDER BY group_id, sort_order
            """;
        var list = new List<HostGroupMember>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new HostGroupMember(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        }
        return list;
    }

    /// <summary>新建分组。空名或重名抛 <see cref="ArgumentException"/>。</summary>
    public HostGroup Create(string name)
    {
        string trimmed = NormalizeName(name);
        using var conn = db.Open();
        EnsureUniqueName(conn, trimmed, exceptId: null);
        int order = NextGroupOrder(conn);
        string id = Guid.NewGuid().ToString("N");
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO host_groups (id, name, sort_order) VALUES ($id, $name, $order)
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", trimmed);
        cmd.Parameters.AddWithValue("$order", order);
        cmd.ExecuteNonQuery();
        return new HostGroup(id, trimmed, order);
    }

    /// <summary>改名。空名、重名或分组不存在抛 <see cref="ArgumentException"/>。</summary>
    public void Rename(string id, string name)
    {
        string trimmed = NormalizeName(name);
        using var conn = db.Open();
        if (!GroupExists(conn, id))
        {
            throw new ArgumentException("分组不存在");
        }
        EnsureUniqueName(conn, trimmed, exceptId: id);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE host_groups SET name = $name WHERE id = $id";
        cmd.Parameters.AddWithValue("$name", trimmed);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>删除分组。成员行一并删除，主机回到未分组。</summary>
    public void Delete(string id)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        using (var members = conn.CreateCommand())
        {
            members.Transaction = tx;
            members.CommandText = "DELETE FROM host_group_members WHERE group_id = $id";
            members.Parameters.AddWithValue("$id", id);
            members.ExecuteNonQuery();
        }
        using (var group = conn.CreateCommand())
        {
            group.Transaction = tx;
            group.CommandText = "DELETE FROM host_groups WHERE id = $id";
            group.Parameters.AddWithValue("$id", id);
            group.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>按给定 id 顺序重写分组 sort_order（0 起）。</summary>
    public void ReorderGroups(IReadOnlyList<string> orderedIds)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        for (int i = 0; i < orderedIds.Count; i++)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE host_groups SET sort_order = $order WHERE id = $id";
            cmd.Parameters.AddWithValue("$order", i);
            cmd.Parameters.AddWithValue("$id", orderedIds[i]);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>把主机移入分组（追加到末尾）。<paramref name="groupId"/> 为 null 时移出，回到未分组。已在目标分组则保持原顺序。</summary>
    public void MoveMember(string nodeId, string? groupId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            throw new ArgumentException("节点 ID 不能为空");
        }
        using var conn = db.Open();
        if (groupId is null)
        {
            RemoveMember(conn, nodeId);
            return;
        }
        if (!GroupExists(conn, groupId))
        {
            throw new ArgumentException("分组不存在");
        }
        string? current = CurrentGroup(conn, nodeId);
        if (current == groupId)
        {
            return;
        }
        int order = NextMemberOrder(conn, groupId);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO host_group_members (node_id, group_id, sort_order)
            VALUES ($node, $group, $order)
            ON CONFLICT(node_id) DO UPDATE SET
              group_id = excluded.group_id,
              sort_order = excluded.sort_order
            """;
        cmd.Parameters.AddWithValue("$node", nodeId);
        cmd.Parameters.AddWithValue("$group", groupId);
        cmd.Parameters.AddWithValue("$order", order);
        cmd.ExecuteNonQuery();
    }

    /// <summary>取消记住或移出分组时去掉成员行。</summary>
    public void RemoveMember(string nodeId)
    {
        using var conn = db.Open();
        RemoveMember(conn, nodeId);
    }

    /// <summary>按给定节点顺序重写该分组内的 sort_order。不在该分组的 id 忽略。</summary>
    public void ReorderMembers(string groupId, IReadOnlyList<string> orderedNodeIds)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        for (int i = 0; i < orderedNodeIds.Count; i++)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE host_group_members SET sort_order = $order
                WHERE node_id = $node AND group_id = $group
                """;
            cmd.Parameters.AddWithValue("$order", i);
            cmd.Parameters.AddWithValue("$node", orderedNodeIds[i]);
            cmd.Parameters.AddWithValue("$group", groupId);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static void RemoveMember(SqliteConnection conn, string nodeId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM host_group_members WHERE node_id = $node";
        cmd.Parameters.AddWithValue("$node", nodeId);
        cmd.ExecuteNonQuery();
    }

    private static string NormalizeName(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("分组名称不能为空");
        }
        return trimmed;
    }

    private static void EnsureUniqueName(SqliteConnection conn, string name, string? exceptId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = exceptId is null
            ? "SELECT COUNT(*) FROM host_groups WHERE name = $name"
            : "SELECT COUNT(*) FROM host_groups WHERE name = $name AND id <> $id";
        cmd.Parameters.AddWithValue("$name", name);
        if (exceptId is not null)
        {
            cmd.Parameters.AddWithValue("$id", exceptId);
        }
        long count = (long)(cmd.ExecuteScalar() ?? 0L);
        if (count > 0)
        {
            throw new ArgumentException("已有同名分组");
        }
    }

    private static bool GroupExists(SqliteConnection conn, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM host_groups WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    private static string? CurrentGroup(SqliteConnection conn, string nodeId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT group_id FROM host_group_members WHERE node_id = $node";
        cmd.Parameters.AddWithValue("$node", nodeId);
        object? value = cmd.ExecuteScalar();
        return value is string group ? group : null;
    }

    private static int NextGroupOrder(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(sort_order), -1) + 1 FROM host_groups";
        return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int NextMemberOrder(SqliteConnection conn, string groupId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(MAX(sort_order), -1) + 1 FROM host_group_members WHERE group_id = $group
            """;
        cmd.Parameters.AddWithValue("$group", groupId);
        return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}

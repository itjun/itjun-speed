using Microsoft.Data.Sqlite;

namespace LanSpeed.Core.Persistence;

/// <summary>SQLite 连接工厂：路径默认 %LOCALAPPDATA%\LanSpeed\lanspeed.db，首次打开建表。</summary>
public sealed class AppDb : IDisposable
{
    private readonly string _connectionString;
    private readonly object _gate = new();
    private bool _migrated;

    public AppDb(string? dbPath = null)
    {
        string path = dbPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanSpeed",
            "lanspeed.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
    }

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        EnsureSchema(conn);
        return conn;
    }

    private void EnsureSchema(SqliteConnection conn)
    {
        if (_migrated)
        {
            return;
        }
        lock (_gate)
        {
            if (_migrated)
            {
                return;
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS paired_hosts (
                  node_id TEXT PRIMARY KEY NOT NULL,
                  name TEXT NOT NULL,
                  ctrl_port INTEGER NOT NULL,
                  last_ips TEXT NOT NULL,
                  link_mbps INTEGER NOT NULL DEFAULT 0,
                  remembered_at TEXT NOT NULL,
                  last_seen_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS history (
                  id TEXT PRIMARY KEY NOT NULL,
                  time TEXT NOT NULL,
                  mode TEXT NOT NULL,
                  protocol TEXT NOT NULL,
                  duration INTEGER NOT NULL,
                  parallel INTEGER NOT NULL,
                  direction TEXT NOT NULL,
                  payload TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_history_time ON history(time DESC);
                """;
            cmd.ExecuteNonQuery();
            _migrated = true;
        }
    }

    public void Dispose()
    {
        // 连接按次打开关闭；此处无常驻句柄
    }
}

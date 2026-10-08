using LanSpeed.Core.History;

namespace LanSpeed.Core.Tests.History;

public class HistoryStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lanspeed-history-test-{Guid.NewGuid():N}.json");

    [Fact]
    public void AppendLoad_RoundTrips()
    {
        var store = new HistoryStore(_path);
        store.Append(new HistoryRecord("id1", DateTimeOffset.Now, "star", "tcp", 10, 4, "bidir",
            [new HistoryPair("a", "b", "done", 1e9, 9e8, 0.5, 1, 0, 0, "很快", "标题")]));
        store.Append(new HistoryRecord("id2", DateTimeOffset.Now, "single", "udp", 10, 4, "forward",
            [new HistoryPair("a", "b", "done", 5e8, 0, 1, 0, 0.1, 0, "正常", "标题2")]));

        var loaded = store.Load();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("id2", loaded[0].Id); // 新在前
        Assert.Equal("star", loaded[1].Mode);
        Assert.Equal("很快", loaded[1].Pairs[0].Grade);
    }

    [Fact]
    public void Cap200()
    {
        var store = new HistoryStore(_path);
        for (int i = 0; i < 210; i++)
        {
            store.Append(new HistoryRecord($"id{i}", DateTimeOffset.Now, "single", "tcp", 10, 4, "forward",
                [new HistoryPair("a", "b", "done", 1, 0, 0, 0, 0, 0, "", "")]));
        }
        Assert.Equal(200, store.Load().Count);
        Assert.Equal("id209", store.Load()[0].Id);
    }

    [Fact]
    public void MissingFile_Empty()
    {
        Assert.Empty(new HistoryStore(_path).Load());
    }

    [Fact]
    public void CsvExport()
    {
        var store = new HistoryStore(_path);
        store.Append(new HistoryRecord("id1", new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero), "single", "tcp", 10, 4, "forward",
            [new HistoryPair("a", "b", "done", 8e8, 0, 0.5, 1, 0, 0, "很快", "每秒能传约 100 MB, \"引号\"")]));

        string csv = Path.ChangeExtension(_path, ".csv");
        store.WriteCsv(csv);
        string content = File.ReadAllText(csv);
        Assert.Contains("A→B Mbps", content);
        Assert.Contains("800", content);      // 8e8 → 800 Mbps
        Assert.Contains("\"每秒能传约 100 MB, \"\"引号\"\"\"", content); // CSV 引号转义
    }

    public void Dispose()
    {
        TryDelete(_path);
        TryDelete(Path.ChangeExtension(_path, ".csv"));
        TryDelete(_path + ".tmp");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}

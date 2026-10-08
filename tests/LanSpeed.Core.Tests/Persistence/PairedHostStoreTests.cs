using LanSpeed.Core.Persistence;

namespace LanSpeed.Core.Tests.Persistence;

public class PairedHostStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lanspeed-pair-test-{Guid.NewGuid():N}.db");
    private readonly AppDb _db;

    public PairedHostStoreTests() => _db = new AppDb(_path);

    [Fact]
    public void Remember_List_Forget()
    {
        var store = new PairedHostStore(_db);
        store.Remember("n1", "PC-A", 39301, ["192.168.1.10"], 1000);
        store.Remember("n2", "PC-B", 39301, ["192.168.1.20"], 100);

        var list = store.List();
        Assert.Equal(2, list.Count);
        Assert.True(store.IsRemembered("n1"));
        Assert.Equal(100, store.Get("n2")!.LinkMbps);

        store.Forget("n1");
        Assert.False(store.IsRemembered("n1"));
        Assert.Single(store.List());
    }

    [Fact]
    public void TouchSeen_UpdatesIp()
    {
        var store = new PairedHostStore(_db);
        store.Remember("n1", "PC-A", 39301, ["192.168.1.10"], 1000);
        store.TouchSeen("n1", "PC-A", 39301, ["192.168.1.99"], 2500);
        var h = store.Get("n1")!;
        Assert.Equal(["192.168.1.99"], h.LastIps);
        Assert.Equal(2500, h.LinkMbps);
    }

    [Fact]
    public void TouchSeen_IgnoresUnknown()
    {
        var store = new PairedHostStore(_db);
        store.TouchSeen("ghost", "X", 39301, ["1.1.1.1"], 1000);
        Assert.Empty(store.List());
    }

    public void Dispose()
    {
        _db.Dispose();
        TryDelete(_path);
        TryDelete(_path + "-shm");
        TryDelete(_path + "-wal");
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

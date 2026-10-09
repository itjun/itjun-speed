using LanSpeed.Core.Persistence;

namespace LanSpeed.Core.Tests.Persistence;

public class HostGroupStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lanspeed-group-test-{Guid.NewGuid():N}.db");
    private readonly AppDb _db;

    public HostGroupStoreTests() => _db = new AppDb(_path);

    [Fact]
    public void Create_Rename_RejectsDuplicate_DeleteReturnsMembersToUngrouped()
    {
        var store = new HostGroupStore(_db);
        var room = store.Create("3楼-财务室");
        store.MoveMember("n1", room.Id);
        store.MoveMember("n2", room.Id);

        store.Rename(room.Id, "3楼-人事室");
        Assert.Equal("3楼-人事室", store.ListGroups().Single(g => g.Id == room.Id).Name);
        var dev = store.Create("研发室");
        Assert.Throws<ArgumentException>(() => store.Create("3楼-人事室"));
        Assert.Throws<ArgumentException>(() => store.Create("  "));
        Assert.Throws<ArgumentException>(() => store.Rename(room.Id, dev.Name));

        store.Delete(room.Id);
        Assert.DoesNotContain(store.ListGroups(), g => g.Id == room.Id);
        Assert.Empty(store.ListMembers());
        store.Delete(dev.Id);
        Assert.Empty(store.ListGroups());
    }

    [Fact]
    public void MoveMember_LeavesPreviousGroup()
    {
        var store = new HostGroupStore(_db);
        var a = store.Create("财务室");
        var b = store.Create("研发室");
        store.MoveMember("n1", a.Id);
        store.MoveMember("n1", b.Id);

        var members = store.ListMembers();
        Assert.DoesNotContain(members, m => m.NodeId == "n1" && m.GroupId == a.Id);
        Assert.Contains(members, m => m.NodeId == "n1" && m.GroupId == b.Id);
        Assert.Single(members);
    }

    [Fact]
    public void RemoveMember_DropsRememberedHost()
    {
        var store = new HostGroupStore(_db);
        var room = store.Create("财务室");
        store.MoveMember("n1", room.Id);
        store.MoveMember("n2", room.Id);

        store.RemoveMember("n1");
        var left = store.ListMembers();
        Assert.DoesNotContain(left, m => m.NodeId == "n1");
        Assert.Contains(left, m => m.NodeId == "n2" && m.GroupId == room.Id);
    }

    [Fact]
    public void Reorder_GroupsAndMembers_Persists()
    {
        var store = new HostGroupStore(_db);
        var a = store.Create("A");
        var b = store.Create("B");
        var c = store.Create("C");
        store.ReorderGroups([c.Id, a.Id, b.Id]);
        Assert.Equal(["C", "A", "B"], store.ListGroups().Select(g => g.Name).ToArray());

        store.MoveMember("n1", a.Id);
        store.MoveMember("n2", a.Id);
        store.ReorderMembers(a.Id, ["n2", "n1"]);
        var order = store.ListMembers().Where(m => m.GroupId == a.Id).OrderBy(m => m.SortOrder).Select(m => m.NodeId).ToArray();
        Assert.Equal(["n2", "n1"], order);

        store.MoveMember("n2", a.Id);
        Assert.Equal(0, store.ListMembers().Single(m => m.NodeId == "n2").SortOrder);
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

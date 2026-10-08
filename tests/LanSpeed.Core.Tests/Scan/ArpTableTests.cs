using LanSpeed.Core.Scan;

namespace LanSpeed.Core.Tests.Scan;

public class ArpTableTests
{
    [Fact]
    public void ParseWindowsArpA()
    {
        const string output = """
            接口: 192.168.210.79 --- 0x12
              Internet 地址          物理地址              类型
              192.168.210.1        a0-bc-d7-11-22-33     动态
              192.168.210.222      88-ae-1d-44-55-66     动态
              192.168.210.255      ff-ff-ff-ff-ff-ff     静态
              224.0.0.22           01-00-5e-00-00-16     静态
              239.255.255.250      01-00-5e-7f-ff-fa     静态
            """;
        var map = ArpTable.ParseArpA(output);

        Assert.Equal(2, map.Count);
        Assert.Equal("a0:bc:d7:11:22:33", map["192.168.210.1"]);
        Assert.Equal("88:ae:1d:44:55:66", map["192.168.210.222"]);
    }

    [Fact]
    public void ParseEnglishArpA()
    {
        const string output = """
            Interface: 10.0.0.5 --- 0x4
              Internet Address      Physical Address      Type
              10.0.0.1              00:1a:2b:3c:4d:5e     dynamic
            """;
        var map = ArpTable.ParseArpA(output);
        Assert.Single(map);
        Assert.Equal("00:1a:2b:3c:4d:5e", map["10.0.0.1"]);
    }

}

using LanSpeed.Core.Iperf;

namespace LanSpeed.Core.Tests.Iperf;

public class ProbeResultTests
{
    [Fact]
    public void Timeout_ReasonContainsPort()
    {
        var r = ProbeResult.Parse(
            """{"start":{},"intervals":[],"end":{},"error":"unable to connect to server - 192.168.1.23, port 5201: Operation timed out"}""",
            5201);
        Assert.False(r.Ok);
        Assert.Contains("5201", r.Reason);
    }

    [Fact]
    public void Busy_IsReachable()
    {
        var r = ProbeResult.Parse("""{"error":"the server is busy running a test. try again later"}""", 5201);
        Assert.True(r.Ok);
    }

    [Fact]
    public void Success_TakesMeanRtt()
    {
        var r = ProbeResult.Parse("""{"end":{"streams":[{"sender":{"mean_rtt":850}}]}}""", 5201);
        Assert.True(r.Ok);
        Assert.Equal(0.85, r.RttMs, 3);
    }

    [Fact]
    public void Refused_ReasonExplainsPort()
    {
        var r = ProbeResult.Parse("""{"error":"unable to connect to server: Connection refused"}""", 5300);
        Assert.False(r.Ok);
        Assert.Contains("5300", r.Reason);
    }

    [Fact]
    public void NoRoute()
    {
        var r = ProbeResult.Parse("""{"error":"unable to connect to server: send: No route to host"}""", 5201);
        Assert.False(r.Ok);
        Assert.Contains("路由", r.Reason);
    }

    [Fact]
    public void UnparseableNonJson_TakesFirstLine()
    {
        var r = ProbeResult.Parse("some iperf3 crash output\nsecond line", 5201);
        Assert.False(r.Ok);
        Assert.Equal("some iperf3 crash output", r.Reason);
    }

    [Fact]
    public void EmptyOutput_TreatedAsTimeout()
    {
        var r = ProbeResult.Parse("", 5201);
        Assert.False(r.Ok);
        Assert.Contains("5201", r.Reason);
    }
}

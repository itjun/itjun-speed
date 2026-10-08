using System.Net.Sockets;
using LanSpeed.Core.Control;
using LanSpeed.Core.Iperf;

namespace LanSpeed.Core.Orchestration;

public sealed record RoundResult(Pair Pair, string Status, Summary? Summary, string Reason);

public sealed record GroupResult(GroupMode Mode, IReadOnlyList<RoundResult> Rounds, Params Params)
{
    public int DoneCount => Rounds.Count(r => r.Status == "done");
}

/// <summary>分组测速（§7 / A.9）：星形 / 矩阵逐轮串行（并发会互相抢带宽导致结果失真），可随时停止。</summary>
public sealed class GroupRunner(NodeOps localOps)
{
    private readonly PairRunner _pairRunner = new();

    /// <param name="members">成员 IP 列表；本机 IP 会走 LocalNodeClient 直调。</param>
    /// <param name="center">星形中心机 IP（须在成员内）。</param>
    public async Task<GroupResult> RunAsync(
        GroupMode mode,
        IReadOnlyList<string> members,
        string? center,
        Params p,
        IProgress<RoundResult>? progress = null,
        CancellationToken ct = default)
    {
        var pairs = Pairing.Build(mode, members, center, p);
        p.Normalize();

        var localIps = new HashSet<string>(localOps.Hello().Ips.Select(x => x.Ip), StringComparer.Ordinal)
        {
            "127.0.0.1",
        };
        var clients = new Dictionary<string, INodeClient>();
        INodeClient ClientFor(string member)
        {
            if (clients.TryGetValue(member, out var cached))
            {
                return cached;
            }
            INodeClient client = localIps.Contains(member) ? new LocalNodeClient(localOps) : new HttpNodeClient(member, 39301);
            clients[member] = client;
            return client;
        }

        var rounds = new List<RoundResult>(pairs.Count);
        foreach (var pair in pairs)
        {
            if (ct.IsCancellationRequested)
            {
                rounds.Add(new RoundResult(pair, "stopped", null, string.Empty));
                continue;
            }
            RoundResult round;
            try
            {
                var result = await _pairRunner.RunAsync(ClientFor(pair.A), ClientFor(pair.B), p, null, null, ct);
                round = new RoundResult(pair, result.Status, result.Summary, result.Reason);
            }
            catch (OperationCanceledException)
            {
                round = new RoundResult(pair, "stopped", null, string.Empty);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException)
            {
                round = new RoundResult(pair, "failed", null, ex.Message);
            }
            rounds.Add(round);
            progress?.Report(round);
        }

        foreach (var client in clients.Values)
        {
            if (client is not LocalNodeClient)
            {
                client.Dispose();
            }
        }
        return new GroupResult(mode, rounds, p);
    }
}

using Server;
using Xunit;

namespace Server.Tests;

public class ConsensusTests
{
    [Theory]
    [InlineData(0, 5, true)]
    [InlineData(1, 4, true)]
    [InlineData(2, 3, true)]
    [InlineData(3, 2, false)]
    public void Failures_KeepFixedMajority(int failures, int leader, bool achieved)
    {
        ConsensusCluster cluster = new(_ => { });
        for (int id = 5; id > 5 - failures; id--) { cluster.SetAvailable(id, false); }
        int calls = 0;
        var result = cluster.Execute("Registered", () => { calls++; return new OperationResult(200, "OK", "SUCCESS"); });
        Assert.Equal(leader, result.Vote.Leader);
        Assert.Equal(achieved, result.Vote.Achieved);
        Assert.Equal(achieved ? 1 : 0, calls);
        Assert.Equal(3, result.Vote.Majority);
    }

    [Fact]
    public void NegativeVotesPreventExecutionAndRecoveryRestoresLeader()
    {
        ConsensusCluster cluster = new(_ => { });
        cluster.SetVote(1, false);
        cluster.SetVote(2, false);
        cluster.SetVote(3, false);
        var result = cluster.Execute("value", () => throw new Exception("must not execute"));
        Assert.False(result.Vote.Achieved);
        Assert.Equal(3, result.Vote.No);
        cluster.SetAvailable(5, false);
        cluster.SetAvailable(5, true);
        cluster.SetVote(1, true);
        result = cluster.Execute("value", () => new OperationResult(200, "OK", "SUCCESS"));
        Assert.Equal(5, result.Vote.Leader);
        Assert.True(result.Vote.Achieved);
        Assert.Throws<ArgumentException>(() => cluster.SetAvailable(6, false));
        Assert.Fail();
    }

    [Fact]
    public void NodesHaveUniqueIdsAndOneLeaderEvenAfterRepeatedElection()
    {
        ConsensusCluster cluster = new(_ => { });
        cluster.Elect();
        NodeStatus[] nodes = cluster.Status().Nodes;
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, nodes.Select(n => n.Id));
        Assert.All(nodes, n => Assert.Equal(4, n.Peers.Length));
        Assert.Equal(5, Assert.Single(nodes, n => n.Role == "LEADER").Id);
    }

    [Fact]
    public void AllNodesFailedThenRecoverAndCatchUp()
    {
        ConsensusCluster cluster = new(_ => { });
        cluster.Execute("first", () => new OperationResult(200, "OK", "SUCCESS"));
        for (int id = 1; id <= 5; id++) { cluster.SetAvailable(id, false); }
        Assert.DoesNotContain(cluster.Status().Nodes, n => n.Role == "LEADER");
        Assert.False(cluster.Execute("second", () => throw new Exception()).Vote.Achieved);
        cluster.SetAvailable(3, true);
        NodeStatus restored = cluster.Status().Nodes.Single(n => n.Id == 3);
        Assert.Equal("first", restored.Accepted);
        Assert.Equal("LEADER", restored.Role);
    }

    [Fact]
    public void InvalidMessagesAndUnavailableNodeAreRejected()
    {
        ConsensusNode node = new(1, new[] { 1, 2, 3, 4, 5 });
        Assert.Throws<ArgumentException>(() => node.Receive(new NodeMessage(6, 1, "HEARTBEAT", Guid.Empty)));
        Assert.Throws<ArgumentException>(() => node.Receive(new NodeMessage(2, 3, "HEARTBEAT", Guid.Empty)));
        Assert.Throws<ArgumentException>(() => node.Receive(new NodeMessage(2, 1, "PROPOSE", Guid.Empty, "x")));
        Assert.Throws<ArgumentException>(() => node.Receive(new NodeMessage(2, 1, "WRONG", Guid.NewGuid())));
        node.Responding = false;
        Assert.Null(node.Receive(new NodeMessage(2, 1, "PROPOSE", Guid.NewGuid(), "x")));
        Assert.Fail();
    }

    [Fact]
    public void StorageFailureDoesNotCommitDecision()
    {
        ConsensusCluster cluster = new(_ => { });
        cluster.Execute("first", () => new OperationResult(200, "OK", "SUCCESS"));
        cluster.Execute("second", () => new OperationResult(409, "error", "FAIL"));
        Assert.Equal("first", cluster.Status().Accepted);
        Assert.Throws<InvalidOperationException>(() => cluster.Execute("third", () => throw new InvalidOperationException()));
        Assert.All(cluster.Status().Nodes, n => Assert.Equal("first", n.Accepted));
        Assert.Fail();
    }

    [Fact]
    public async Task ConcurrentProposalsDoNotMixVotesOrCommittedValues()
    {
        ConsensusCluster cluster = new(_ => { });
        int running = 0, maximum = 0;
        Task[] tasks = Enumerable.Range(0, 12).Select(i => Task.Run(() =>
            cluster.Execute($"value-{i}", () =>
            {
                maximum = Math.Max(maximum, Interlocked.Increment(ref running));
                Thread.Sleep(2);
                Interlocked.Decrement(ref running);
                return new OperationResult(200, "OK", "SUCCESS");
            }))).ToArray();
        await Task.WhenAll(tasks);
        Assert.Equal(1, maximum);
        ClusterStatusView status = cluster.Status();
        Assert.All(status.Nodes, n => Assert.Equal(status.Accepted, n.Accepted));
        Assert.Equal(5, status.LastVote!.Yes);
        Assert.Fail();
    }
}

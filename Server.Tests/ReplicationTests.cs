using EventManagement;
using Server;
using System.Text.Json;
using Xunit;

namespace Server.Tests
{
    public class ReplicationTests
    {
        private readonly MemoryStore primary = new MemoryStore();
        private readonly MemoryStore replica = new MemoryStore();
        private readonly ReplicatedDatabase cluster;

        public ReplicationTests()
        {
            primary.Data.Events.Add(new Event { Id = 1, Name = "Конференция", Capacity = 10, AvailablePlaces = 10 });
            primary.Data.Events.Add(new Event { Id = 2, Name = "Выставка", Capacity = 5, AvailablePlaces = 5 });
            cluster = new ReplicatedDatabase(primary, replica);
            cluster.Initialize();
        }

        private StorageResponse<int> Register()
        {
            return cluster.Execute("Client2", "RegisterParticipant", true, store =>
            {
                MemoryStore memory = (MemoryStore)store;
                memory.Data.Events[0].AvailablePlaces--;
                return memory.Data.Events[0].AvailablePlaces;
            });
        }

        [Fact]
        public void InitialState_CopiesSeveralRecordsIndependently()
        {
            Assert.True(primary.ReadSnapshot().Matches(replica.ReadSnapshot()));
            Assert.Equal(2, replica.Data.Events.Count);
            Assert.NotSame(primary.Data.Events[0], replica.Data.Events[0]);
        }

        [Fact]
        public void ThreeChanges_ReplicateAfterEachOperation()
        {
            for (int places = 9; places >= 7; places--)
            {
                StorageResponse<int> response = Register();
                Assert.Equal(places, response.Value);
                Assert.Equal("Primary", response.Instance);
                Assert.Equal("SUCCESS", response.Replication);
                Assert.True(primary.ReadSnapshot().Matches(replica.ReadSnapshot()));
            }
        }

        [Fact]
        public void PrimaryUnavailable_ReadAndUpdateUseReplica()
        {
            cluster.SetAvailable("Primary", false, "Test");
            Assert.Equal("UNAVAILABLE", cluster.Status("Test").PrimaryState);
            StorageResponse<int> read = cluster.Execute("Client1", "Read", false, store => store.ReadSnapshot().Events[0].AvailablePlaces);
            Assert.Equal("Replica", read.Instance);
            Assert.Equal(10, read.Value);
            Assert.Equal("Replica", Register().Instance);
            Assert.Equal(10, primary.Data.Events[0].AvailablePlaces);
            Assert.Equal(9, replica.Data.Events[0].AvailablePlaces);
        }

        [Fact]
        public void ConnectionFailure_SelectsAvailableReplica()
        {
            primary.Available = false;
            Assert.Equal("Replica", Register().Instance);
        }

        [Fact]
        public void Recovery_ObservesDifferenceBeforeSynchronization()
        {
            cluster.SetAvailable("Primary", false, "Test");
            Register();
            cluster.SetAvailable("Primary", true, "Test");
            ClusterStatus status = cluster.Status("Test");
            Assert.Equal("AVAILABLE", status.PrimaryState);
            Assert.Equal("AVAILABLE", status.ReplicaState);
            Assert.False(status.Consistent);
            Assert.Equal("Replica", status.ActiveInstance);
            cluster.Synchronize("Test");
            Assert.True(cluster.Status("Test").Consistent);
            Assert.Equal(9, primary.Data.Events[0].AvailablePlaces);
            Assert.Equal("Primary", Register().Instance);
            Assert.Equal(8, replica.Data.Events[0].AvailablePlaces);
        }

        [Fact]
        public void Restart_UsesReplicaAfterWriteDuringPrimaryFailure()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                ReplicatedDatabase first = new ReplicatedDatabase(primary, replica, path);
                first.Initialize();
                primary.Available = false;
                first.Execute("Test", "Write", true, store =>
                {
                    ((MemoryStore)store).Data.Events[0].AvailablePlaces--;
                    return 0;
                });
                primary.Available = true;
                ReplicatedDatabase restarted = new ReplicatedDatabase(primary, replica, path);
                Assert.Equal("Replica", restarted.Status("Test").ActiveInstance);
                Assert.Equal(9, restarted.Execute("Test", "Read", false, store => store.ReadSnapshot().Events[0].AvailablePlaces).Value);
                restarted.Synchronize("Test");
                Assert.True(primary.ReadSnapshot().Matches(replica.ReadSnapshot()));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void BothUnavailable_ReturnsClearError()
        {
            cluster.SetAvailable("Primary", false, "Test");
            cluster.SetAvailable("Replica", false, "Test");
            Assert.Contains("Primary и Replica недоступны", Assert.Throws<StorageUnavailableException>(() => Register()).Message);
        }

        [Fact]
        public void ReplicaUnavailable_PrimaryChangeSucceedsWithReplicationWarning()
        {
            replica.Available = false;
            StorageResponse<int> response = Register();
            Assert.Equal(9, response.Value);
            Assert.Equal("FAIL", response.Replication);
            Assert.NotNull(response.Warning);
            Assert.Equal(10, replica.Data.Events[0].AvailablePlaces);
            replica.Available = true;
            cluster.Synchronize("Test");
            Assert.True(cluster.Status("Test").Consistent);
        }

        [Fact]
        public void ReplicationError_DoesNotRepeatSuccessfulChange()
        {
            replica.FailWrite = true;
            Assert.Equal("FAIL", Register().Replication);
            Assert.Equal(9, primary.Data.Events[0].AvailablePlaces);
            Assert.Equal(10, replica.Data.Events[0].AvailablePlaces);
        }

        [Fact]
        public void StaleReplica_IsNotUsedAfterFailedReplication()
        {
            replica.FailWrite = true;
            Register();
            primary.Available = false;
            Assert.Contains("устаревшие", Assert.Throws<StorageUnavailableException>(() => Register()).Message);
        }

        [Fact]
        public void SynchronizationFailure_PreservesActualReplica()
        {
            primary.Available = false;
            Register();
            primary.Available = true;
            primary.FailWrite = true;
            Assert.Throws<IOException>(() => cluster.Synchronize("Test"));
            Assert.Equal("Replica", cluster.Status("Test").ActiveInstance);
            primary.FailWrite = false;
            cluster.Synchronize("Test");
            Assert.Equal(9, primary.Data.Events[0].AvailablePlaces);
        }

        [Fact]
        public void SynchronizationRequiresBothAvailableCopies()
        {
            replica.Available = false;
            Assert.Throws<StorageUnavailableException>(() => cluster.Synchronize("Test"));
        }

        [Fact]
        public void ReadAndWriteErrors_ArePropagatedWithoutChangingOtherCopy()
        {
            Assert.Throws<IOException>(() => cluster.Execute<int>("Test", "Read", false, store => throw new IOException("Ошибка чтения")));
            Assert.Throws<IOException>(() => cluster.Execute<int>("Test", "Update", true, store => throw new IOException("Ошибка изменения")));
            Assert.Equal(10, replica.Data.Events[0].AvailablePlaces);
        }

        [Fact]
        public async Task TwoClients_CanReadInParallel()
        {
            using (Barrier barrier = new Barrier(2))
            {
                Task<int> Read(string client)
                {
                    return Task.Factory.StartNew(() => cluster.Execute(client, "Read", false, store =>
                    {
                        Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(5)));
                        return store.ReadSnapshot().Events[0].AvailablePlaces;
                    }).Value, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }
                Assert.Equal(new int[] { 10, 10 }, await Task.WhenAll(Read("Client1"), Read("Client2")));
            }
        }

        private class MemoryStore : IDataStore
        {
            public DataSnapshot Data { get; private set; } = new DataSnapshot();
            public bool Available { get; set; } = true;
            public bool FailWrite { get; set; }

            public void Initialize() { }
            public bool IsAvailable() { return Available; }
            public DataSnapshot ReadSnapshot()
            {
                return JsonSerializer.Deserialize<DataSnapshot>(JsonSerializer.Serialize(Data))!;
            }
            public void WriteSnapshot(DataSnapshot snapshot)
            {
                if (FailWrite)
                {
                    throw new IOException("Ошибка записи копии");
                }
                Data = JsonSerializer.Deserialize<DataSnapshot>(JsonSerializer.Serialize(snapshot))!;
            }
            public DataSnapshot CopyFrom(IDataStore source)
            {
                DataSnapshot snapshot = source.ReadSnapshot();
                WriteSnapshot(snapshot);
                return snapshot;
            }
        }
    }
}

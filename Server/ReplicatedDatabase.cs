using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Server
{
    public class ReplicatedDatabase
    {
        private readonly IDataStore primary;
        private readonly IDataStore replica;
        private readonly ReaderWriterLockSlim gate = new ReaderWriterLockSlim();
        private bool primaryEnabled = true;
        private bool replicaEnabled = true;
        private volatile bool initialized;
        private string activeInstance = "Primary";
        private bool synchronized = true;
        private readonly string? statePath;

        public ReplicatedDatabase(IDataStore primary, IDataStore replica, string? statePath = null)
        {
            this.primary = primary;
            this.replica = replica;
            this.statePath = statePath;
        }

        private void SaveActive(string instance)
        {
            if (statePath == null) return;
            string? directory = Path.GetDirectoryName(statePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = statePath + ".tmp";
            File.WriteAllText(temporary, instance);
            File.Move(temporary, statePath, true);
        }

        public void Initialize()
        {
            if (initialized)
            {
                return;
            }
            gate.EnterWriteLock();
            try
            {
                if (initialized)
                {
                    return;
                }
                bool p = Check("Primary", "Server", "Initialize");
                bool r = Check("Replica", "Server", "Initialize");
                if (!p || !r)
                {
                    throw new StorageUnavailableException("Для первого запуска нужны обе БД: Primary и Replica.");
                }
                DataSnapshot original = primary.ReadSnapshot();
                DataSnapshot backup = replica.ReadSnapshot();
                if (statePath != null && File.Exists(statePath))
                {
                    string saved = File.ReadAllText(statePath).Trim();
                    if (saved != "Primary" && saved != "Replica")
                        throw new StorageUnavailableException("Неизвестное состояние репликации.");
                    activeInstance = saved;
                }
                bool emptyReplica = backup.Venues.Count + backup.Participants.Count + backup.Events.Count + backup.Registrations.Count == 0;
                if (emptyReplica && activeInstance == "Primary")
                {
                    Database.Log("Server", "InitialReplication", "START", "Primary -> Replica");
                    replica.CopyFrom(primary);
                    backup = replica.ReadSnapshot();
                    Database.Log("Server", "InitialReplication", "SUCCESS", "Primary = Replica");
                }
                synchronized = original.Matches(backup);
                initialized = true;
            }
            finally
            {
                gate.ExitWriteLock();
            }
        }

        public StorageResponse<T> Execute<T>(string clientId, string operation, bool write, Func<IDataStore, T> action)
        {
            Initialize();
            if (write)
            {
                gate.EnterWriteLock();
            }
            else
            {
                gate.EnterReadLock();
            }
            try
            {
                Database.Log(clientId, operation, "REQUEST");
                string selected = Select(clientId, operation);
                if (write)
                {
                    SaveActive(selected);
                    synchronized = false;
                }
                T value = action(Store(selected));
                string replication = "NOT_REQUIRED";
                string? warning = null;
                if (write && selected == "Primary")
                {
                    Database.Log(clientId, "Replication", "START", "Primary -> Replica");
                    try
                    {
                        if (!Check("Replica", clientId, "Replication"))
                        {
                            throw new StorageUnavailableException("Replica недоступна");
                        }
                        replica.CopyFrom(primary);
                        synchronized = true;
                        replication = "SUCCESS";
                        Database.Log(clientId, "Replication", "SUCCESS", "Replica UPDATE; Primary = Replica");
                    }
                    catch (Exception ex) when (IsStorageError(ex))
                    {
                        replication = "FAIL";
                        warning = "Операция выполнена в Primary, но репликация не завершена. Требуется синхронизация.";
                        Database.Log(clientId, "Replication", "FAIL", ex.GetBaseException().Message);
                    }
                }
                else if (write)
                {
                    replication = "PENDING";
                    warning = "Изменение сохранено в Replica. После восстановления Primary выполните синхронизацию.";
                    Database.Log(clientId, "Replication", "PENDING", "Replica — актуальная копия");
                }
                string outcome = value is OperationResult result && result.StatusCode >= 400 ? "FAIL" : "SUCCESS";
                Database.Log(clientId, operation, outcome, $"Instance={selected}; Replication={replication}");
                return new StorageResponse<T>(value, selected, replication, warning);
            }
            catch (Exception ex) when (IsStorageError(ex))
            {
                Database.Log(clientId, operation, "FAIL", $"Instance={activeInstance}; {ex.GetBaseException().Message}");
                throw;
            }
            finally
            {
                if (write)
                {
                    gate.ExitWriteLock();
                }
                else
                {
                    gate.ExitReadLock();
                }
            }
        }

        private string Select(string clientId, string operation)
        {
            string current = activeInstance;
            if (Check(current, clientId, operation))
            {
                return current;
            }
            string fallback = current == "Primary" ? "Replica" : "Primary";
            Database.Log(clientId, "Failover", "START", $"{current} -> {fallback}");
            if (!Check(fallback, clientId, operation))
            {
                Database.Log(clientId, "Failover", "FAIL", "Оба экземпляра UNAVAILABLE");
                throw new StorageUnavailableException("Primary и Replica недоступны. Запрос не выполнен.");
            }
            if (!synchronized)
            {
                Database.Log(clientId, "Failover", "FAIL", "Резервная копия устарела");
                throw new StorageUnavailableException($"{current} недоступна, а {fallback} содержит устаревшие данные. Восстановите актуальную копию.");
            }
            activeInstance = fallback;
            Database.Log(clientId, "Failover", "SUCCESS", $"Request redirected to {fallback}");
            return fallback;
        }

        public ClusterStatus Status(string clientId)
        {
            Initialize();
            gate.EnterReadLock();
            try
            {
                bool p = Check("Primary", clientId, "Status");
                bool r = Check("Replica", clientId, "Status");
                DataSnapshot? ps = p ? primary.ReadSnapshot() : null;
                DataSnapshot? rs = r ? replica.ReadSnapshot() : null;
                bool? equal = ps != null && rs != null ? ps.Matches(rs) : null;
                Database.Log(clientId, "Compare", equal == true ? "SUCCESS" : "NOT_SYNCHRONIZED", $"Active={activeInstance}; Equal={equal}");
                return new ClusterStatus(p ? "AVAILABLE" : "UNAVAILABLE", r ? "AVAILABLE" : "UNAVAILABLE",
                    activeInstance, !synchronized, equal, ps, rs);
            }
            finally
            {
                gate.ExitReadLock();
            }
        }

        public void SetAvailable(string instance, bool available, string clientId)
        {
            gate.EnterWriteLock();
            try
            {
                if (instance == "Primary")
                {
                    primaryEnabled = available;
                }
                else if (instance == "Replica")
                {
                    replicaEnabled = available;
                }
                else
                {
                    throw new ArgumentException("Укажите Primary или Replica");
                }
                Database.Log(clientId, available ? "Recovery" : "Failure", available ? "RESTORED" : "UNAVAILABLE", instance);
            }
            finally
            {
                gate.ExitWriteLock();
            }
        }

        public void Synchronize(string clientId)
        {
            Initialize();
            gate.EnterWriteLock();
            try
            {
                if (!Check("Primary", clientId, "Synchronization") || !Check("Replica", clientId, "Synchronization"))
                {
                    throw new StorageUnavailableException("Синхронизация невозможна: нужны оба доступных экземпляра.");
                }
                string source = activeInstance;
                string target = source == "Primary" ? "Replica" : "Primary";
                SaveActive(source);
                synchronized = false;
                Database.Log(clientId, "Synchronization", "START", $"{source} -> {target}");
                DataSnapshot snapshot = Store(target).CopyFrom(Store(source));
                if (!snapshot.Matches(Store(target).ReadSnapshot()))
                {
                    throw new StorageUnavailableException("После синхронизации копии не совпадают");
                }
                activeInstance = "Primary";
                synchronized = true;
                SaveActive(activeInstance);
                Database.Log(clientId, "Synchronization", "SUCCESS", "Primary = Replica; Active=Primary");
            }
            catch (Exception ex) when (IsStorageError(ex))
            {
                Database.Log(clientId, "Synchronization", "FAIL", ex.GetBaseException().Message);
                throw;
            }
            finally
            {
                gate.ExitWriteLock();
            }
        }

        private bool Check(string instance, string clientId, string operation)
        {
            bool enabled = instance == "Primary" ? primaryEnabled : replicaEnabled;
            Database.Log(clientId, operation, "CHECK", instance);
            bool available = enabled && Store(instance).IsAvailable();
            Database.Log(clientId, operation, available ? "AVAILABLE" : "UNAVAILABLE", instance);
            return available;
        }

        private IDataStore Store(string instance)
        {
            return instance == "Primary" ? primary : replica;
        }

        public static bool IsStorageError(Exception ex)
        {
            Exception cause = ex.GetBaseException();
            return ex is NpgsqlException || ex is DbUpdateException || ex is IOException || ex is TimeoutException
                || cause is NpgsqlException || cause is IOException || cause is TimeoutException;
        }
    }

    public class StorageUnavailableException : IOException
    {
        public StorageUnavailableException(string message) : base(message)
        {
        }
    }

    public record StorageResponse<T>(T Value, string Instance, string Replication, string? Warning);
    public record ClusterStatus(string PrimaryState, string ReplicaState, string ActiveInstance,
        bool RequiresSynchronization, bool? Consistent, DataSnapshot? Primary, DataSnapshot? Replica);
}

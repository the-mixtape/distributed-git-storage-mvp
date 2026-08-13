namespace DistributedGitStorage.Data.Enums;

public enum ERepositoryReplicaStatus
{
    Pending,
    Replicating,
    Healthy,
    Lagging,
    Unavailable
}

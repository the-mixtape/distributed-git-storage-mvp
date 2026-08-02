namespace DistributedGitStorage.Data.Models;

public enum RepositoryReplicaStatus
{
    Pending,
    Replicating,
    Healthy,
    Lagging,
    Unavailable
}

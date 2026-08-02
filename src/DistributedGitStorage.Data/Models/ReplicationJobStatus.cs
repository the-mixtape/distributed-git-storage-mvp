namespace DistributedGitStorage.Data.Models;

public enum ReplicationJobStatus
{
    Pending,
    Processing,
    Retry,
    Completed
}

namespace DistributedGitStorage.Data.Models;

public sealed class RepositoryReplica
{
    public Guid RepositoryId { get; set; }
    public required string StorageNode { get; set; }
    public long AppliedGeneration { get; set; }
    public RepositoryReplicaStatus Status { get; set; }
    public string? RefsHash { get; set; }
    public DateTimeOffset? LastSuccessfulReplicationAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
    public RepositoryPlacement Repository { get; set; } = null!;
}

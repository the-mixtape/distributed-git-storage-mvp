namespace DistributedGitStorage.Services.Interfaces;

public interface IReplicationService
{
    Task<IReadOnlyList<ReplicationJobInfo>> GetJobsAsync(CancellationToken cancellationToken);
    Task RetryAsync(Guid jobId, CancellationToken cancellationToken);
}

public sealed record ReplicationJobInfo(
    Guid Id,
    Guid RepositoryId,
    string SourceNode,
    string TargetNode,
    long Generation,
    string Status,
    int Attempt,
    DateTimeOffset NextAttemptAt,
    string? LastError);

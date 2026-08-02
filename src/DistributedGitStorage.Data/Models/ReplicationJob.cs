namespace DistributedGitStorage.Data.Models;

public sealed class ReplicationJob
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public required string SourceNode { get; set; }
    public required string TargetNode { get; set; }
    public long Generation { get; set; }
    public ReplicationJobStatus Status { get; set; }
    public int Attempt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public RepositoryPlacement Repository { get; set; } = null!;
}

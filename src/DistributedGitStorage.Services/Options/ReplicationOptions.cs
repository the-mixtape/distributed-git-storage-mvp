namespace DistributedGitStorage.Services.Options;

public sealed class ReplicationOptions
{
    public bool Enabled { get; init; } = true;
    public int WriteQuorum { get; init; } = 2;
    public int WriteQuorumTimeoutSeconds { get; init; } = 30;
    public int PollIntervalSeconds { get; init; } = 2;
    public int LeaseSeconds { get; init; } = 60;
    public int ReconciliationIntervalSeconds { get; init; } = 30;
}

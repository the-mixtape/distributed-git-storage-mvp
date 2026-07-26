namespace DistributedGitStorage.Services.Options;

public sealed class GitClusterOptions
{
    public List<GitStorageNodeOptions> Nodes { get; init; } = [];
}

public sealed class GitStorageNodeOptions
{
    public required string Name { get; init; }
    public required string Address { get; init; }
    public required string InternalAddress { get; init; }
}

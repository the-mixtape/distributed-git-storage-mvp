namespace DistributedGitStorage.StorageNode.Models;

public sealed record RepositoryStateResponse(
    bool Exists,
    string? RefsHash,
    string? Head);

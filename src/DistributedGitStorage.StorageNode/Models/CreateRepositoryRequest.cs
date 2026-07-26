namespace DistributedGitStorage.StorageNode.Models;

public sealed record CreateRepositoryRequest(string DefaultBranch = "main");

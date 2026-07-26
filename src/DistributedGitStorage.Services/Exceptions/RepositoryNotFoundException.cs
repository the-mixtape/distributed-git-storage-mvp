namespace DistributedGitStorage.Services.Exceptions;

public sealed class RepositoryNotFoundException(string name)
    : Exception($"Repository '{name}' was not found.");

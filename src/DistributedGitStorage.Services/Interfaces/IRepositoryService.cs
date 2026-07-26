namespace DistributedGitStorage.Services.Interfaces;

public interface IRepositoryService
{
    IReadOnlyList<StorageInfo> GetStorages();
    Task<IReadOnlyList<RepositoryInfo>> GetRepositoriesAsync(CancellationToken cancellationToken);
    Task<RepositoryInfo> CreateAsync(string name, CancellationToken cancellationToken);
    Task<RepositoryInfo?> GetAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record StorageInfo(string Name, string Address, string StorageName);

public sealed record RepositoryInfo(
    Guid Id,
    string Name,
    string Storage,
    string StorageName,
    string RelativePath,
    bool ExistsInStorage,
    DateTimeOffset CreatedAt);

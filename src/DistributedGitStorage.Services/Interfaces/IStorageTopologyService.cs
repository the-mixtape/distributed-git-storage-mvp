namespace DistributedGitStorage.Services.Interfaces;

public interface IStorageTopologyService
{
    Task<IReadOnlyList<StorageClusterInfo>> GetClustersAsync(CancellationToken cancellationToken);
    Task<StorageClusterInfo> CreateClusterAsync(string name, CancellationToken cancellationToken);
    Task<StorageClusterInfo> SetClusterActiveAsync(
        Guid clusterId,
        bool isActive,
        CancellationToken cancellationToken);
    Task<StorageNodeInfo> AddNodeAsync(
        Guid clusterId,
        string name,
        string address,
        string internalAddress,
        CancellationToken cancellationToken);
    Task<StorageNodeInfo> UpdateNodeAsync(
        Guid clusterId,
        Guid nodeId,
        string address,
        string internalAddress,
        bool isActive,
        CancellationToken cancellationToken);
}

public sealed record StorageClusterInfo(
    Guid Id,
    string Name,
    bool IsActive,
    IReadOnlyList<StorageNodeInfo> Nodes);

public sealed record StorageNodeInfo(
    Guid Id,
    string Name,
    string Address,
    string InternalAddress,
    bool IsActive);

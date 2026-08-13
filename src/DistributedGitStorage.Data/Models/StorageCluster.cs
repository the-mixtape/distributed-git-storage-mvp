namespace DistributedGitStorage.Data.Models;

public sealed class StorageCluster
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<StorageNode> Nodes { get; set; } = [];
    public ICollection<RepositoryPlacement> RepositoryPlacements { get; set; } = [];
}

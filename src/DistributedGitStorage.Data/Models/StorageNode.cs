namespace DistributedGitStorage.Data.Models;

public sealed class StorageNode
{
    public Guid Id { get; set; }
    public Guid StorageClusterId { get; set; }
    public required string Name { get; set; }
    public required string Address { get; set; }
    public required string InternalAddress { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public StorageCluster StorageCluster { get; set; } = null!;
}

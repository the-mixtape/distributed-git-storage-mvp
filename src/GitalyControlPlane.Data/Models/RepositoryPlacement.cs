namespace GitalyControlPlane.Data.Models;

public sealed class RepositoryPlacement
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Storage { get; set; }
    public required string StorageName { get; set; }
    public required string RelativePath { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

using DistributedGitStorage.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistributedGitStorage.Data.Configurations;

internal sealed class StorageNodeConfiguration : IEntityTypeConfiguration<StorageNode>
{
    public void Configure(EntityTypeBuilder<StorageNode> builder)
    {
        builder.ToTable("storage_nodes");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Name).HasMaxLength(100);
        builder.Property(item => item.Address).HasMaxLength(500);
        builder.Property(item => item.InternalAddress).HasMaxLength(500);
        builder.HasIndex(item => item.Name).IsUnique();
        builder.HasOne(item => item.StorageCluster)
            .WithMany(item => item.Nodes)
            .HasForeignKey(item => item.StorageClusterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

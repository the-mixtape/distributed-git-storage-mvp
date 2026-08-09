using DistributedGitStorage.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistributedGitStorage.Data.Configurations;

internal sealed class StorageClusterConfiguration : IEntityTypeConfiguration<StorageCluster>
{
    public void Configure(EntityTypeBuilder<StorageCluster> builder)
    {
        builder.ToTable("storage_clusters");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Name).HasMaxLength(100);
        builder.HasIndex(item => item.Name).IsUnique();
    }
}

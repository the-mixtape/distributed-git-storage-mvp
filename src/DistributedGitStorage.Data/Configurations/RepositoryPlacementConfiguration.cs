using DistributedGitStorage.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistributedGitStorage.Data.Configurations;

internal sealed class RepositoryPlacementConfiguration : IEntityTypeConfiguration<RepositoryPlacement>
{
    public void Configure(EntityTypeBuilder<RepositoryPlacement> builder)
    {
        builder.ToTable("repository_placements");

        builder.Property(item => item.Name).HasMaxLength(100);
        builder.Property(item => item.Storage).HasMaxLength(100);
        builder.Property(item => item.StorageName).HasMaxLength(100);
        builder.Property(item => item.RelativePath).HasMaxLength(500);

        builder.HasIndex(item => item.Name)
            .IsUnique()
            .HasDatabaseName("uq_repository_placements_name");
    }
}

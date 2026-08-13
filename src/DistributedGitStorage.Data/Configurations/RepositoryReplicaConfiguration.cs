using DistributedGitStorage.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistributedGitStorage.Data.Configurations;

internal sealed class RepositoryReplicaConfiguration : IEntityTypeConfiguration<RepositoryReplica>
{
    public void Configure(EntityTypeBuilder<RepositoryReplica> builder)
    {
        builder.ToTable("repository_replicas");
        builder.HasKey(item => new { item.RepositoryId, item.StorageNode });
        builder.Property(item => item.StorageNode).HasMaxLength(100);
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(item => item.RefsHash).HasMaxLength(64);
        builder.Property(item => item.LastError).HasMaxLength(2000);
        builder.HasOne(item => item.Repository)
            .WithMany(item => item.Replicas)
            .HasForeignKey(item => item.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(item => new { item.RepositoryId, item.Status, item.AppliedGeneration });
    }
}

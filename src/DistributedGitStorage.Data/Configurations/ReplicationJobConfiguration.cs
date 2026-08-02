using DistributedGitStorage.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistributedGitStorage.Data.Configurations;

internal sealed class ReplicationJobConfiguration : IEntityTypeConfiguration<ReplicationJob>
{
    public void Configure(EntityTypeBuilder<ReplicationJob> builder)
    {
        builder.ToTable("replication_jobs");
        builder.Property(item => item.SourceNode).HasMaxLength(100);
        builder.Property(item => item.TargetNode).HasMaxLength(100);
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(item => item.LastError).HasMaxLength(2000);
        builder.HasOne(item => item.Repository)
            .WithMany()
            .HasForeignKey(item => item.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(item => new { item.Status, item.NextAttemptAt });
        builder.HasIndex(item => new { item.RepositoryId, item.TargetNode, item.Generation }).IsUnique();
    }
}

using Microsoft.EntityFrameworkCore;
using DistributedGitStorage.Data.Models;

namespace DistributedGitStorage.Data;

public sealed class DistributedGitStorageDbContext(DbContextOptions<DistributedGitStorageDbContext> options)
    : DbContext(options)
{
    public DbSet<RepositoryPlacement> RepositoryPlacements => Set<RepositoryPlacement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DistributedGitStorageDbContext).Assembly);
    }
}

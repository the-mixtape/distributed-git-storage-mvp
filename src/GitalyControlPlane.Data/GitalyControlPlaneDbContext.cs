using Microsoft.EntityFrameworkCore;
using GitalyControlPlane.Data.Models;

namespace GitalyControlPlane.Data;

public sealed class GitalyControlPlaneDbContext(DbContextOptions<GitalyControlPlaneDbContext> options)
    : DbContext(options)
{
    public DbSet<RepositoryPlacement> RepositoryPlacements => Set<RepositoryPlacement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GitalyControlPlaneDbContext).Assembly);
    }
}

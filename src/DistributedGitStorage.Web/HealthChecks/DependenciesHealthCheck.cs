using DistributedGitStorage.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DistributedGitStorage.Web.HealthChecks;

internal sealed class DependenciesHealthCheck(
    IDbContextFactory<DistributedGitStorageDbContext> dbContextFactory,
    IHttpClientFactory httpClientFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            if (!await db.Database.CanConnectAsync(cancellationToken))
                return HealthCheckResult.Unhealthy("Application PostgreSQL is unavailable.");

            var nodes = await db.StorageNodes
                .AsNoTracking()
                .Where(item => item.IsActive && item.StorageCluster.IsActive)
                .ToArrayAsync(cancellationToken);
            if (nodes.Length == 0)
                return HealthCheckResult.Unhealthy("No active storage nodes are configured in PostgreSQL.");

            foreach (var node in nodes)
            {
                var client = httpClientFactory.CreateClient("GitStorageNode");
                using var response = await client.GetAsync(
                    new Uri(new Uri(node.Address), "/health"),
                    cancellationToken);
                response.EnsureSuccessStatusCode();
            }

            return HealthCheckResult.Healthy("PostgreSQL and all C# Git storage nodes are reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("A required dependency is unavailable.", exception);
        }
    }
}

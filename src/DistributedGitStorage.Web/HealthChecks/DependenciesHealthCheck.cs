using DistributedGitStorage.Data;
using DistributedGitStorage.Services.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DistributedGitStorage.Web.HealthChecks;

internal sealed class DependenciesHealthCheck(
    IDbContextFactory<DistributedGitStorageDbContext> dbContextFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<GitClusterOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            if (!await db.Database.CanConnectAsync(cancellationToken))
                return HealthCheckResult.Unhealthy("Application PostgreSQL is unavailable.");

            foreach (var node in options.Value.Nodes)
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

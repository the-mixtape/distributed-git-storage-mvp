using System.Net.Sockets;
using GitalyControlPlane.Data;
using GitalyControlPlane.Services.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace GitalyControlPlane.Web.HealthChecks;

internal sealed class DependenciesHealthCheck(
    IDbContextFactory<GitalyControlPlaneDbContext> dbContextFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<GitalyOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            if (!await db.Database.CanConnectAsync(cancellationToken))
                return HealthCheckResult.Unhealthy("Application PostgreSQL is unavailable.");

            foreach (var storage in options.Value.Storages)
            {
                var uri = new Uri(storage.Address);
                using var tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(uri.Host, uri.Port, cancellationToken);
            }

            var client = httpClientFactory.CreateClient("SidechannelGateway");
            using var response = await client.GetAsync(new Uri(new Uri(options.Value.SidechannelGatewayAddress), "/health"), cancellationToken);
            response.EnsureSuccessStatusCode();
            return HealthCheckResult.Healthy("PostgreSQL, Praefect and sidechannel gateway are reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("A required dependency is unavailable.", exception);
        }
    }
}

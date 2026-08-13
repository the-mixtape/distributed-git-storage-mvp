using DistributedGitStorage.Data;
using DistributedGitStorage.Services.Interfaces;
using DistributedGitStorage.Services.Options;
using DistributedGitStorage.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DistributedGitStorage.Services.Extensions;

public static class StartupExtensions
{
    public static IServiceCollection AddDistributedGitStorageServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("PostgreSql connection string is missing.");

        services.AddDbContextFactory<DistributedGitStorageDbContext>(options => options.UseNpgsql(connectionString));
        services.AddOptions<ReplicationOptions>()
            .Bind(configuration.GetSection("Replication"))
            .Validate(options => options.WriteQuorum > 0, "WriteQuorum must be greater than zero.")
            .Validate(options => options.WriteQuorumTimeoutSeconds > 0,
                "WriteQuorumTimeoutSeconds must be greater than zero.")
            .ValidateOnStart();
        services.AddSingleton<StorageClusterClient>();
        services.AddSingleton<IRepositoryService, RepositoryService>();
        services.AddSingleton<IGitSmartHttpService, GitSmartHttpService>();
        services.AddSingleton<IReplicationService, ReplicationService>();
        services.AddSingleton<IStorageTopologyService, StorageTopologyService>();
        services.AddHostedService<ReplicationWorker>();
        services.AddHttpClient("GitStorageNode", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        return services;
    }
}

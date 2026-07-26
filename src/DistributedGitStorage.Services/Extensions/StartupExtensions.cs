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
        services.Configure<GitClusterOptions>(configuration.GetSection("GitCluster"));
        services.AddSingleton<StorageClusterClient>();
        services.AddSingleton<IRepositoryService, RepositoryService>();
        services.AddSingleton<IGitSmartHttpService, GitSmartHttpService>();
        services.AddHttpClient("GitStorageNode", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        return services;
    }
}

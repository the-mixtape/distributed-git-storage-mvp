using GitalyControlPlane.Data;
using GitalyControlPlane.Services.Interfaces;
using GitalyControlPlane.Services.Options;
using GitalyControlPlane.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GitalyControlPlane.Services.Extensions;

public static class StartupExtensions
{
    public static IServiceCollection AddGitalyControlPlaneServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("PostgreSql connection string is missing.");

        services.AddDbContextFactory<GitalyControlPlaneDbContext>(options => options.UseNpgsql(connectionString));
        services.Configure<GitalyOptions>(configuration.GetSection("Gitaly"));
        services.AddSingleton<GitalyClientProvider>();
        services.AddSingleton<IRepositoryService, GitalyRepositoryService>();
        services.AddSingleton<IGitSmartHttpService, GitalyGitSmartHttpService>();
        services.AddHttpClient("SidechannelGateway");

        return services;
    }
}

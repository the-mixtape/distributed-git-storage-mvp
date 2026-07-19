using System.Text.RegularExpressions;
using GitalyControlPlane.Data;
using GitalyControlPlane.Data.Models;
using GitalyControlPlane.Services.Exceptions;
using GitalyControlPlane.Services.Interfaces;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GitalyControlPlane.Services.Services;

internal sealed partial class GitalyRepositoryService(
    GitalyClientProvider clients,
    IDbContextFactory<GitalyControlPlaneDbContext> dbContextFactory) : IRepositoryService
{
    public IReadOnlyList<StorageInfo> GetStorages() => clients.Storages
        .Select(storage => new StorageInfo(storage.Options.Name, storage.Options.Address, storage.Options.StorageName))
        .ToArray();

    public async Task<IReadOnlyList<RepositoryInfo>> GetRepositoriesAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.RepositoryPlacements
            .AsNoTracking()
            .OrderBy(item => item.CreatedAt)
            .Select(item => ToInfo(item, true))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<RepositoryInfo> CreateAsync(string name, CancellationToken cancellationToken)
    {
        var normalizedName = NormalizeName(name);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await dbContext.RepositoryPlacements.AnyAsync(item => item.Name == normalizedName, cancellationToken))
        {
            throw new RepositoryAlreadyExistsException(normalizedName);
        }

        var storage = clients.Storages[await dbContext.RepositoryPlacements.CountAsync(cancellationToken) % clients.Storages.Count];
        var placement = new RepositoryPlacement
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            Storage = storage.Options.Name,
            StorageName = storage.Options.StorageName,
            RelativePath = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow
        };
        placement.RelativePath = $"poc/{placement.Id:N}.git";
        var repository = CreateGitalyRepository(placement);

        await storage.RepositoryClient.CreateRepositoryAsync(
            new Gitaly.CreateRepositoryRequest
            {
                Repository = repository,
                DefaultBranch = ByteString.CopyFromUtf8("main")
            },
            cancellationToken: cancellationToken);

        dbContext.RepositoryPlacements.Add(placement);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            await TryRemoveRepositoryAsync(storage, repository);
            if (exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } })
            {
                throw new RepositoryAlreadyExistsException(normalizedName, exception);
            }

            throw;
        }

        return ToInfo(placement, true);
    }

    public async Task<RepositoryInfo?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var placement = await dbContext.RepositoryPlacements.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (placement is null) return null;

        var response = await clients.GetStorage(placement.Storage).RepositoryClient.RepositoryExistsAsync(
            new Gitaly.RepositoryExistsRequest { Repository = CreateGitalyRepository(placement) },
            cancellationToken: cancellationToken);
        return ToInfo(placement, response.Exists);
    }

    private static async Task TryRemoveRepositoryAsync(StorageClient storage, Gitaly.Repository repository)
    {
        try
        {
            await storage.RepositoryClient.RemoveRepositoryAsync(new Gitaly.RemoveRepositoryRequest { Repository = repository });
        }
        catch
        {
            // Preserve the original database exception. Production code should also emit a cleanup metric/event.
        }
    }

    internal static string NormalizeName(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        if (!RepositoryNameRegex().IsMatch(normalized)) throw new InvalidRepositoryNameException();
        return normalized;
    }

    internal static Gitaly.Repository CreateGitalyRepository(RepositoryPlacement placement) => new()
    {
        StorageName = placement.StorageName,
        RelativePath = placement.RelativePath,
        GlRepository = $"project-{placement.Id:N}",
        GlProjectPath = placement.Name
    };

    private static RepositoryInfo ToInfo(RepositoryPlacement placement, bool exists) => new(
        placement.Id, placement.Name, placement.Storage, placement.StorageName,
        placement.RelativePath, exists, placement.CreatedAt);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryNameRegex();
}

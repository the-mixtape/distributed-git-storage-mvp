using System.Text.RegularExpressions;
using DistributedGitStorage.Data;
using DistributedGitStorage.Data.Models;
using DistributedGitStorage.Services.Exceptions;
using DistributedGitStorage.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DistributedGitStorage.Services.Services;

internal sealed partial class RepositoryService(
    StorageClusterClient cluster,
    IDbContextFactory<DistributedGitStorageDbContext> dbContextFactory) : IRepositoryService
{
    public IReadOnlyList<StorageInfo> GetStorages() => cluster.Nodes
        .Select(node => new StorageInfo(node.Name, node.Address, "csharp-cluster"))
        .ToArray();

    public async Task<IReadOnlyList<RepositoryInfo>> GetRepositoriesAsync(
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.RepositoryPlacements
            .AsNoTracking()
            .OrderBy(item => item.CreatedAt)
            .Select(item => ToInfo(item, true))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<RepositoryInfo> CreateAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var normalizedName = NormalizeName(name);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await dbContext.RepositoryPlacements.AnyAsync(
                item => item.Name == normalizedName,
                cancellationToken))
        {
            throw new RepositoryAlreadyExistsException(normalizedName);
        }

        var primaryNode = cluster.Nodes[
            await dbContext.RepositoryPlacements.CountAsync(cancellationToken) % cluster.Nodes.Count];
        var placement = new RepositoryPlacement
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            Storage = primaryNode.Name,
            StorageName = "csharp-cluster",
            RelativePath = $"{Guid.NewGuid():N}.git",
            CreatedAt = DateTimeOffset.UtcNow
        };
        placement.RelativePath = $"{placement.Id:N}.git";

        await cluster.CreateOnAllNodesAsync(placement.Id, cancellationToken);
        dbContext.RepositoryPlacements.Add(placement);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            await cluster.DeleteFromAllNodesBestEffortAsync(placement.Id);
            if (exception is DbUpdateException
                {
                    InnerException: PostgresException
                    {
                        SqlState: PostgresErrorCodes.UniqueViolation
                    }
                })
            {
                throw new RepositoryAlreadyExistsException(normalizedName, exception);
            }

            throw;
        }

        return ToInfo(placement, true);
    }

    public async Task<RepositoryInfo?> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var placement = await dbContext.RepositoryPlacements
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (placement is null)
        {
            return null;
        }

        var node = await cluster.GetAvailableNodeAsync(placement.Storage, cancellationToken);
        var exists = await cluster.ExistsAsync(node, placement.Id, cancellationToken);
        return ToInfo(placement, exists);
    }

    internal static string NormalizeName(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        if (!RepositoryNameRegex().IsMatch(normalized))
        {
            throw new InvalidRepositoryNameException();
        }

        return normalized;
    }

    private static RepositoryInfo ToInfo(RepositoryPlacement placement, bool exists) => new(
        placement.Id,
        placement.Name,
        placement.Storage,
        placement.StorageName,
        placement.RelativePath,
        exists,
        placement.CreatedAt);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryNameRegex();
}

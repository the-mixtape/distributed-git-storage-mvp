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
            CurrentGeneration = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
        placement.RelativePath = $"{placement.Id:N}.git";

        await cluster.CreateOnAllNodesAsync(placement.Id, cancellationToken);
        foreach (var node in cluster.Nodes)
        {
            var state = await cluster.GetRepositoryStateAsync(node, placement.Id, cancellationToken);
            placement.Replicas.Add(new RepositoryReplica
            {
                RepositoryId = placement.Id,
                StorageNode = node.Name,
                AppliedGeneration = 0,
                Status = RepositoryReplicaStatus.Healthy,
                RefsHash = state.RefsHash,
                LastSuccessfulReplicationAt = DateTimeOffset.UtcNow
            });
        }
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

    public async Task<IReadOnlyList<RepositoryReplicaInfo>> GetReplicasAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var exists = await dbContext.RepositoryPlacements.AnyAsync(item => item.Id == id, cancellationToken);
        if (!exists)
        {
            throw new RepositoryNotFoundException(id.ToString());
        }

        return await dbContext.RepositoryReplicas
            .AsNoTracking()
            .Where(item => item.RepositoryId == id)
            .OrderBy(item => item.StorageNode)
            .Select(item => new RepositoryReplicaInfo(
                item.StorageNode,
                item.AppliedGeneration,
                item.Status.ToString(),
                item.RefsHash,
                item.LastSuccessfulReplicationAt,
                item.LastAttemptAt,
                item.LastError))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<RepositoryInfo?> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var placement = await dbContext.RepositoryPlacements
            .Include(item => item.Replicas)
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (placement is null)
        {
            return null;
        }

        var currentNodes = placement.Replicas
            .Where(item => item.Status == RepositoryReplicaStatus.Healthy
                && item.AppliedGeneration == placement.CurrentGeneration)
            .OrderByDescending(item => item.StorageNode == placement.Storage)
            .Select(item => item.StorageNode);
        var node = await cluster.GetFirstHealthyNodeAsync(currentNodes, cancellationToken)
            ?? throw new HttpRequestException(
                $"No healthy current replica is available for repository '{placement.Name}'.");
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

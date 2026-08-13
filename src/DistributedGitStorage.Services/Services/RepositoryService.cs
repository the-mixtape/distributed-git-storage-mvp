using System.Text.RegularExpressions;
using DistributedGitStorage.Data;
using DistributedGitStorage.Data.Enums;
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
    public async Task<IReadOnlyList<StorageInfo>> GetStoragesAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.StorageNodes
            .AsNoTracking()
            .OrderBy(item => item.StorageCluster.Name)
            .ThenBy(item => item.Name)
            .Select(item => new StorageInfo(item.Name, item.Address, item.StorageCluster.Name))
            .ToArrayAsync(cancellationToken);
    }

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

        var storageCluster = await dbContext.StorageClusters
            .Include(item => item.Nodes.Where(node => node.IsActive))
            .Where(item => item.IsActive && item.Nodes.Any(node => node.IsActive))
            .OrderBy(item => item.RepositoryPlacements.Count)
            .ThenBy(item => item.Name)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No active storage cluster with active nodes is configured.");
        var nodes = storageCluster.Nodes.OrderBy(item => item.Name).ToArray();
        var repositoryCount = await dbContext.RepositoryPlacements.CountAsync(
            item => item.StorageClusterId == storageCluster.Id,
            cancellationToken);
        var primaryNode = nodes[repositoryCount % nodes.Length];
        var placement = new RepositoryPlacement
        {
            Id = Guid.NewGuid(),
            StorageClusterId = storageCluster.Id,
            Name = normalizedName,
            Storage = primaryNode.Name,
            StorageName = storageCluster.Name,
            RelativePath = $"{Guid.NewGuid():N}.git",
            CurrentGeneration = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
        placement.RelativePath = $"{placement.Id:N}.git";

        await cluster.CreateOnAllNodesAsync(storageCluster.Id, placement.Id, cancellationToken);
        foreach (var node in await cluster.GetNodesAsync(storageCluster.Id, cancellationToken))
        {
            var state = await cluster.GetRepositoryStateAsync(node, placement.Id, cancellationToken);
            placement.Replicas.Add(new RepositoryReplica
            {
                RepositoryId = placement.Id,
                StorageNode = node.Name,
                AppliedGeneration = 0,
                Status = ERepositoryReplicaStatus.Healthy,
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
            await cluster.DeleteFromAllNodesBestEffortAsync(storageCluster.Id, placement.Id);
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
            .Where(item => item.Status == ERepositoryReplicaStatus.Healthy
                && item.AppliedGeneration == placement.CurrentGeneration)
            .OrderByDescending(item => item.StorageNode == placement.Storage)
            .Select(item => item.StorageNode);
        var node = await cluster.GetFirstHealthyNodeAsync(
            placement.StorageClusterId,
            currentNodes,
            cancellationToken)
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

using System.Text.RegularExpressions;
using DistributedGitStorage.Data;
using DistributedGitStorage.Data.Models;
using DistributedGitStorage.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DistributedGitStorage.Services.Services;

internal sealed partial class StorageTopologyService(
    IDbContextFactory<DistributedGitStorageDbContext> dbContextFactory) : IStorageTopologyService
{
    public async Task<IReadOnlyList<StorageClusterInfo>> GetClustersAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var clusters = await db.StorageClusters
            .Include(item => item.Nodes)
            .AsNoTracking()
            .OrderBy(item => item.Name)
            .ToArrayAsync(cancellationToken);
        return clusters.Select(ToInfo).ToArray();
    }

    public async Task<StorageClusterInfo> CreateClusterAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var normalizedName = NormalizeName(name, "cluster");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.StorageClusters.AnyAsync(item => item.Name == normalizedName, cancellationToken))
        {
            throw new ArgumentException($"Storage cluster '{normalizedName}' already exists.", nameof(name));
        }

        var cluster = new StorageCluster
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.StorageClusters.Add(cluster);
        await db.SaveChangesAsync(cancellationToken);
        return ToInfo(cluster);
    }

    public async Task<StorageNodeInfo> AddNodeAsync(
        Guid clusterId,
        string name,
        string address,
        string internalAddress,
        CancellationToken cancellationToken)
    {
        var normalizedName = NormalizeName(name, "node");
        ValidateAddress(address, nameof(address));
        ValidateAddress(internalAddress, nameof(internalAddress));
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.StorageClusters.AnyAsync(item => item.Id == clusterId, cancellationToken))
        {
            throw new KeyNotFoundException($"Storage cluster '{clusterId}' was not found.");
        }

        if (await db.StorageNodes.AnyAsync(item => item.Name == normalizedName, cancellationToken))
        {
            throw new ArgumentException($"Storage node '{normalizedName}' already exists.", nameof(name));
        }

        var node = new StorageNode
        {
            Id = Guid.NewGuid(),
            StorageClusterId = clusterId,
            Name = normalizedName,
            Address = address.TrimEnd('/'),
            InternalAddress = internalAddress.TrimEnd('/'),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.StorageNodes.Add(node);
        await db.SaveChangesAsync(cancellationToken);
        return ToInfo(node);
    }

    public async Task<StorageClusterInfo> SetClusterActiveAsync(
        Guid clusterId,
        bool isActive,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var cluster = await db.StorageClusters
            .Include(item => item.Nodes)
            .SingleOrDefaultAsync(item => item.Id == clusterId, cancellationToken)
            ?? throw new KeyNotFoundException($"Storage cluster '{clusterId}' was not found.");
        cluster.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        return ToInfo(cluster);
    }

    public async Task<StorageNodeInfo> UpdateNodeAsync(
        Guid clusterId,
        Guid nodeId,
        string address,
        string internalAddress,
        bool isActive,
        CancellationToken cancellationToken)
    {
        ValidateAddress(address, nameof(address));
        ValidateAddress(internalAddress, nameof(internalAddress));
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var node = await db.StorageNodes.SingleOrDefaultAsync(
            item => item.Id == nodeId && item.StorageClusterId == clusterId,
            cancellationToken)
            ?? throw new KeyNotFoundException($"Storage node '{nodeId}' was not found in cluster '{clusterId}'.");
        node.Address = address.TrimEnd('/');
        node.InternalAddress = internalAddress.TrimEnd('/');
        node.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        return ToInfo(node);
    }

    private static StorageClusterInfo ToInfo(StorageCluster cluster) => new(
        cluster.Id,
        cluster.Name,
        cluster.IsActive,
        cluster.Nodes.OrderBy(item => item.Name).Select(ToInfo).ToArray());

    private static StorageNodeInfo ToInfo(StorageNode node) => new(
        node.Id,
        node.Name,
        node.Address,
        node.InternalAddress,
        node.IsActive);

    private static string NormalizeName(string value, string resource)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return ResourceNameRegex().IsMatch(normalized)
            ? normalized
            : throw new ArgumentException($"Invalid storage {resource} name.", nameof(value));
    }

    private static void ValidateAddress(string value, string parameterName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("A storage node address must be an absolute HTTP or HTTPS URL.", parameterName);
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex ResourceNameRegex();
}

using System.Net;
using System.Net.Http.Json;
using DistributedGitStorage.Data;
using DistributedGitStorage.Services.Options;
using Microsoft.EntityFrameworkCore;

namespace DistributedGitStorage.Services.Services;

internal sealed class StorageClusterClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDbContextFactory<DistributedGitStorageDbContext> _dbContextFactory;

    public StorageClusterClient(
        IDbContextFactory<DistributedGitStorageDbContext> dbContextFactory,
        IHttpClientFactory httpClientFactory)
    {
        _dbContextFactory = dbContextFactory;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyList<GitStorageNodeOptions>> GetNodesAsync(
        Guid storageClusterId,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.StorageNodes
            .AsNoTracking()
            .Where(item => item.StorageClusterId == storageClusterId && item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new GitStorageNodeOptions(item.Name, item.Address, item.InternalAddress))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<GitStorageNodeOptions> GetNodeAsync(
        Guid storageClusterId,
        string name,
        CancellationToken cancellationToken) =>
        (await GetNodesAsync(storageClusterId, cancellationToken))
        .SingleOrDefault(node => node.Name == name)
        ?? throw new InvalidOperationException($"Active storage node '{name}' is not configured.");

    public async Task CreateOnAllNodesAsync(
        Guid storageClusterId,
        Guid repositoryId,
        CancellationToken cancellationToken)
    {
        var nodes = await GetNodesAsync(storageClusterId, cancellationToken);
        EnsureNodesConfigured(nodes, storageClusterId);
        var createdNodes = new List<GitStorageNodeOptions>();
        try
        {
            foreach (var node in nodes)
            {
                using var response = await CreateClient(node).PostAsJsonAsync(
                    $"/internal/repositories/{repositoryId}",
                    new { defaultBranch = "main" },
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                createdNodes.Add(node);
            }
        }
        catch
        {
            await DeleteFromNodesBestEffortAsync(repositoryId, createdNodes);
            throw;
        }
    }

    public async Task DeleteFromAllNodesBestEffortAsync(Guid storageClusterId, Guid repositoryId) =>
        await DeleteFromNodesBestEffortAsync(
            repositoryId,
            await GetNodesAsync(storageClusterId, CancellationToken.None));

    public async Task<bool> ExistsAsync(
        GitStorageNodeOptions node,
        Guid repositoryId,
        CancellationToken cancellationToken)
    {
        using var response = await CreateClient(node).GetAsync(
            $"/internal/repositories/{repositoryId}",
            cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.OK => true,
            HttpStatusCode.NotFound => false,
            _ => throw new HttpRequestException(
                $"Storage node '{node.Name}' returned {(int)response.StatusCode}.")
        };
    }

    public async Task<GitStorageNodeOptions?> GetFirstHealthyNodeAsync(
        Guid storageClusterId,
        IEnumerable<string> orderedNodeNames,
        CancellationToken cancellationToken)
    {
        var nodes = await GetNodesAsync(storageClusterId, cancellationToken);
        foreach (var nodeName in orderedNodeNames.Distinct(StringComparer.Ordinal))
        {
            var node = nodes.SingleOrDefault(item => item.Name == nodeName);
            if (node is null)
            {
                continue;
            }

            if (await IsHealthyAsync(node, cancellationToken))
            {
                return node;
            }
        }

        return null;
    }

    public async Task<bool> IsHealthyAsync(
        GitStorageNodeOptions node,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await CreateClient(node).GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async Task<RepositoryState> GetRepositoryStateAsync(
        GitStorageNodeOptions node,
        Guid repositoryId,
        CancellationToken cancellationToken)
    {
        using var response = await CreateClient(node).GetAsync(
            $"/internal/repositories/{repositoryId}/state",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new RepositoryState(false, null, null);
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RepositoryState>(cancellationToken)
            ?? throw new HttpRequestException($"Storage node '{node.Name}' returned an empty state response.");
    }

    public async Task ReplicateToNodeAsync(
        Guid repositoryId,
        GitStorageNodeOptions sourceNode,
        GitStorageNodeOptions targetNode,
        CancellationToken cancellationToken)
    {
        var sourceUrl = $"{sourceNode.InternalAddress.TrimEnd('/')}/repositories/{repositoryId}.git";
        using var response = await CreateClient(targetNode).PostAsJsonAsync(
            $"/internal/repositories/{repositoryId}/replicate",
            new { sourceUrl },
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<HttpResponseMessage> SendGitRequestAsync(
        GitStorageNodeOptions node,
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        await CreateClient(node).SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

    private HttpClient CreateClient(GitStorageNodeOptions node)
    {
        var client = _httpClientFactory.CreateClient("GitStorageNode");
        client.BaseAddress = new Uri(node.Address);
        return client;
    }

    private async Task DeleteFromNodesBestEffortAsync(
        Guid repositoryId,
        IEnumerable<GitStorageNodeOptions> nodes)
    {
        foreach (var node in nodes)
        {
            try
            {
                using var response = await CreateClient(node).DeleteAsync(
                    $"/internal/repositories/{repositoryId}");
            }
            catch (HttpRequestException)
            {
                // Cleanup is best effort in this MVP.
            }
        }
    }

    private static void EnsureNodesConfigured(
        IReadOnlyCollection<GitStorageNodeOptions> nodes,
        Guid storageClusterId)
    {
        if (nodes.Count == 0)
        {
            throw new InvalidOperationException(
                $"Storage cluster '{storageClusterId}' has no active nodes configured in PostgreSQL.");
        }
    }
}

internal sealed record RepositoryState(bool Exists, string? RefsHash, string? Head);

using System.Net;
using System.Net.Http.Json;
using DistributedGitStorage.Services.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DistributedGitStorage.Services.Services;

internal sealed class StorageClusterClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<StorageClusterClient> _logger;

    public StorageClusterClient(
        IOptions<GitClusterOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<StorageClusterClient> logger)
    {
        if (options.Value.Nodes.Count == 0)
        {
            throw new InvalidOperationException("At least one Git storage node must be configured.");
        }

        Nodes = options.Value.Nodes;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public IReadOnlyList<GitStorageNodeOptions> Nodes { get; }

    public async Task CreateOnAllNodesAsync(Guid repositoryId, CancellationToken cancellationToken)
    {
        var createdNodes = new List<GitStorageNodeOptions>();
        try
        {
            foreach (var node in Nodes)
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

    public Task DeleteFromAllNodesBestEffortAsync(Guid repositoryId) =>
        DeleteFromNodesBestEffortAsync(repositoryId, Nodes);

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

    public async Task<GitStorageNodeOptions> GetAvailableNodeAsync(
        string preferredNode,
        CancellationToken cancellationToken)
    {
        var orderedNodes = Nodes
            .OrderByDescending(node => node.Name == preferredNode)
            .ToArray();
        foreach (var node in orderedNodes)
        {
            try
            {
                using var response = await CreateClient(node).GetAsync("/health", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return node;
                }
            }
            catch (HttpRequestException)
            {
                // Try the next replica.
            }
        }

        throw new HttpRequestException("No healthy Git storage node is available.");
    }

    public async Task<HttpResponseMessage> SendGitRequestAsync(
        GitStorageNodeOptions node,
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        await CreateClient(node).SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

    public async Task ReplicateAsync(
        Guid repositoryId,
        GitStorageNodeOptions sourceNode,
        CancellationToken cancellationToken)
    {
        var sourceUrl = $"{sourceNode.InternalAddress.TrimEnd('/')}/repositories/{repositoryId}.git";
        foreach (var targetNode in Nodes.Where(node => node.Name != sourceNode.Name))
        {
            try
            {
                using var response = await CreateClient(targetNode).PostAsJsonAsync(
                    $"/internal/repositories/{repositoryId}/replicate",
                    new { sourceUrl },
                    cancellationToken);
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Replication of repository {RepositoryId} to {TargetNode} was deferred.",
                    repositoryId,
                    targetNode.Name);
            }
        }
    }

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
}

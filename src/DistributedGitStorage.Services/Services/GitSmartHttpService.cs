using System.Net.Http.Headers;
using DistributedGitStorage.Data;
using DistributedGitStorage.Data.Models;
using DistributedGitStorage.Services.Exceptions;
using DistributedGitStorage.Services.Interfaces;
using DistributedGitStorage.Services.Options;
using Microsoft.EntityFrameworkCore;

namespace DistributedGitStorage.Services.Services;

internal sealed class GitSmartHttpService(
    StorageClusterClient cluster,
    IDbContextFactory<DistributedGitStorageDbContext> dbContextFactory)
    : IGitSmartHttpService
{
    public async Task WriteInfoRefsAsync(
        string name,
        string service,
        string gitProtocol,
        Stream responseBody,
        CancellationToken cancellationToken)
    {
        var (placement, node) = await ResolveAsync(name, cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/repositories/{placement.Id}.git/info/refs?service={Uri.EscapeDataString(service)}");
        AddGitProtocol(request, gitProtocol);
        using var response = await cluster.SendGitRequestAsync(node, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await response.Content.CopyToAsync(responseBody, cancellationToken);
    }

    public async Task ReceivePackAsync(
        string name,
        string gitProtocol,
        Stream requestBody,
        Stream responseBody,
        CancellationToken cancellationToken)
    {
        var (placement, node) = await ResolveAsync(name, cancellationToken);
        using var request = CreateRpcRequest(
            $"/repositories/{placement.Id}.git/git-receive-pack",
            "application/x-git-receive-pack-request",
            gitProtocol,
            requestBody);
        using var response = await cluster.SendGitRequestAsync(node, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var receivePackResult = new MemoryStream();
        await response.Content.CopyToAsync(receivePackResult, cancellationToken);
        await cluster.ReplicateAsync(placement.Id, node, cancellationToken);
        await PromoteIfNeededAsync(placement, node.Name, cancellationToken);
        receivePackResult.Position = 0;
        await receivePackResult.CopyToAsync(responseBody, cancellationToken);
    }

    public async Task UploadPackAsync(
        string name,
        string gitProtocol,
        Stream requestBody,
        Stream responseBody,
        CancellationToken cancellationToken)
    {
        var (placement, node) = await ResolveAsync(name, cancellationToken);
        using var request = CreateRpcRequest(
            $"/repositories/{placement.Id}.git/git-upload-pack",
            "application/x-git-upload-pack-request",
            gitProtocol,
            requestBody);
        using var response = await cluster.SendGitRequestAsync(node, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await response.Content.CopyToAsync(responseBody, cancellationToken);
    }

    private async Task<(RepositoryPlacement Placement, GitStorageNodeOptions Node)> ResolveAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var normalizedName = RepositoryService.NormalizeName(name);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var placement = await dbContext.RepositoryPlacements
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Name == normalizedName, cancellationToken)
            ?? throw new RepositoryNotFoundException(name);
        var node = await cluster.GetAvailableNodeAsync(placement.Storage, cancellationToken);
        return (placement, node);
    }

    private async Task PromoteIfNeededAsync(
        RepositoryPlacement placement,
        string nodeName,
        CancellationToken cancellationToken)
    {
        if (placement.Storage == nodeName)
        {
            return;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var trackedPlacement = await dbContext.RepositoryPlacements.SingleAsync(
            item => item.Id == placement.Id,
            cancellationToken);
        trackedPlacement.Storage = nodeName;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static HttpRequestMessage CreateRpcRequest(
        string path,
        string contentType,
        string gitProtocol,
        Stream requestBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StreamContent(requestBody)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        AddGitProtocol(request, gitProtocol);
        return request;
    }

    private static void AddGitProtocol(HttpRequestMessage request, string gitProtocol)
    {
        if (!string.IsNullOrWhiteSpace(gitProtocol))
        {
            request.Headers.TryAddWithoutValidation("Git-Protocol", gitProtocol);
        }
    }
}

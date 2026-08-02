using System.Net.Http.Headers;
using DistributedGitStorage.Data;
using DistributedGitStorage.Data.Models;
using DistributedGitStorage.Services.Exceptions;
using DistributedGitStorage.Services.Interfaces;
using DistributedGitStorage.Services.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DistributedGitStorage.Services.Services;

internal sealed class GitSmartHttpService(
    StorageClusterClient cluster,
    IDbContextFactory<DistributedGitStorageDbContext> dbContextFactory,
    IOptions<ReplicationOptions> replicationOptions)
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
        var normalizedName = RepositoryService.NormalizeName(name);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        long? lockKey = null;
        try
        {
            var placement = await dbContext.RepositoryPlacements
                .Include(item => item.Replicas)
                .SingleOrDefaultAsync(item => item.Name == normalizedName, cancellationToken)
                ?? throw new RepositoryNotFoundException(name);
            lockKey = BitConverter.ToInt64(placement.Id.ToByteArray());
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_lock({lockKey.Value})",
                cancellationToken);
            await dbContext.Entry(placement).ReloadAsync(cancellationToken);
            dbContext.Entry(placement).Collection(item => item.Replicas).IsLoaded = false;
            await dbContext.Entry(placement).Collection(item => item.Replicas).LoadAsync(cancellationToken);

            var node = await SelectCurrentNodeAsync(placement, cancellationToken);
            placement.CurrentGeneration++;
            placement.Storage = node.Name;
            foreach (var replica in placement.Replicas)
            {
                replica.Status = RepositoryReplicaStatus.Pending;
                replica.LastError = null;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            using var request = CreateRpcRequest(
                $"/repositories/{placement.Id}.git/git-receive-pack",
                "application/x-git-receive-pack-request",
                gitProtocol,
                requestBody);
            using var response = await cluster.SendGitRequestAsync(node, request, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var receivePackResult = new MemoryStream();
            await response.Content.CopyToAsync(receivePackResult, cancellationToken);

            var sourceState = await cluster.GetRepositoryStateAsync(node, placement.Id, cancellationToken);
            if (!sourceState.Exists || sourceState.RefsHash is null)
            {
                throw new HttpRequestException("The node accepted the push but its repository state cannot be verified.");
            }

            var sourceReplica = placement.Replicas.Single(item => item.StorageNode == node.Name);
            sourceReplica.AppliedGeneration = placement.CurrentGeneration;
            sourceReplica.Status = RepositoryReplicaStatus.Healthy;
            sourceReplica.RefsHash = sourceState.RefsHash;
            sourceReplica.LastSuccessfulReplicationAt = DateTimeOffset.UtcNow;
            ReplicationWorker.EnsureJobs(dbContext, placement, node.Name);
            var reservedUntil = DateTimeOffset.UtcNow.AddSeconds(
                replicationOptions.Value.WriteQuorumTimeoutSeconds + replicationOptions.Value.LeaseSeconds);
            foreach (var jobEntry in dbContext.ChangeTracker.Entries<ReplicationJob>()
                         .Where(entry => entry.Entity.RepositoryId == placement.Id
                             && entry.Entity.Generation == placement.CurrentGeneration))
            {
                jobEntry.Entity.Status = ReplicationJobStatus.Processing;
                jobEntry.Entity.LockedUntil = reservedUntil;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            await ReachWriteQuorumAsync(
                dbContext,
                placement,
                node,
                sourceState.RefsHash,
                cancellationToken);

            receivePackResult.Position = 0;
            await receivePackResult.CopyToAsync(responseBody, cancellationToken);
        }
        finally
        {
            if (lockKey.HasValue)
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_unlock({lockKey.Value})",
                    CancellationToken.None);
            }

            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private async Task ReachWriteQuorumAsync(
        DistributedGitStorageDbContext dbContext,
        RepositoryPlacement placement,
        GitStorageNodeOptions sourceNode,
        string sourceRefsHash,
        CancellationToken cancellationToken)
    {
        var requiredCopies = replicationOptions.Value.WriteQuorum;
        if (requiredCopies > cluster.Nodes.Count)
        {
            await ReleaseReservedJobsAsync(dbContext, placement.Id, placement.CurrentGeneration,
                cancellationToken);
            throw new InvalidOperationException(
                $"WriteQuorum ({requiredCopies}) exceeds the configured storage node count ({cluster.Nodes.Count}).");
        }

        if (requiredCopies == 1)
        {
            await ReleaseReservedJobsAsync(dbContext, placement.Id, placement.CurrentGeneration,
                cancellationToken);
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(replicationOptions.Value.WriteQuorumTimeoutSeconds));
        var currentCopies = 1;
        foreach (var targetNode in cluster.Nodes.Where(item => item.Name != sourceNode.Name))
        {
            var replica = placement.Replicas.Single(item => item.StorageNode == targetNode.Name);
            var job = await dbContext.ReplicationJobs.SingleAsync(item =>
                item.RepositoryId == placement.Id
                && item.Generation == placement.CurrentGeneration
                && item.TargetNode == targetNode.Name,
                cancellationToken);
            replica.Status = RepositoryReplicaStatus.Replicating;
            replica.LastAttemptAt = DateTimeOffset.UtcNow;
            job.Status = ReplicationJobStatus.Processing;
            job.Attempt++;
            job.LockedUntil = DateTimeOffset.UtcNow.AddSeconds(replicationOptions.Value.LeaseSeconds);
            await dbContext.SaveChangesAsync(cancellationToken);

            try
            {
                await cluster.ReplicateToNodeAsync(placement.Id, sourceNode, targetNode, timeout.Token);
                var targetState = await cluster.GetRepositoryStateAsync(targetNode, placement.Id, timeout.Token);
                if (!targetState.Exists || targetState.RefsHash != sourceRefsHash)
                {
                    throw new InvalidOperationException(
                        $"Replica '{targetNode.Name}' refs fingerprint does not match the source.");
                }

                replica.AppliedGeneration = placement.CurrentGeneration;
                replica.Status = RepositoryReplicaStatus.Healthy;
                replica.RefsHash = targetState.RefsHash;
                replica.LastSuccessfulReplicationAt = DateTimeOffset.UtcNow;
                replica.LastError = null;
                job.Status = ReplicationJobStatus.Completed;
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.LockedUntil = null;
                job.LastError = null;
                await dbContext.SaveChangesAsync(cancellationToken);
                currentCopies++;
                if (currentCopies >= requiredCopies)
                {
                    await ReleaseReservedJobsAsync(dbContext, placement.Id, placement.CurrentGeneration,
                        cancellationToken);
                    return;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested)
            {
                replica.Status = RepositoryReplicaStatus.Unavailable;
                replica.LastError = exception.Message;
                job.Status = ReplicationJobStatus.Retry;
                job.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(5);
                job.LockedUntil = null;
                job.LastError = exception.Message;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        throw new WriteQuorumNotReachedException(
            placement.Id,
            placement.CurrentGeneration,
            currentCopies,
            requiredCopies);
    }

    private static async Task ReleaseReservedJobsAsync(
        DistributedGitStorageDbContext dbContext,
        Guid repositoryId,
        long generation,
        CancellationToken cancellationToken)
    {
        var reservedJobs = await dbContext.ReplicationJobs
            .Where(item => item.RepositoryId == repositoryId
                && item.Generation == generation
                && item.Status == ReplicationJobStatus.Processing)
            .ToArrayAsync(cancellationToken);
        foreach (var job in reservedJobs)
        {
            job.Status = ReplicationJobStatus.Pending;
            job.LockedUntil = null;
            job.NextAttemptAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
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
            .Include(item => item.Replicas)
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Name == normalizedName, cancellationToken)
            ?? throw new RepositoryNotFoundException(name);
        var node = await SelectCurrentNodeAsync(placement, cancellationToken);
        return (placement, node);
    }

    private async Task<GitStorageNodeOptions> SelectCurrentNodeAsync(
        RepositoryPlacement placement,
        CancellationToken cancellationToken)
    {
        var currentNodes = placement.Replicas
            .Where(item => item.Status == RepositoryReplicaStatus.Healthy
                && item.AppliedGeneration == placement.CurrentGeneration)
            .OrderByDescending(item => item.StorageNode == placement.Storage)
            .Select(item => item.StorageNode);
        return await cluster.GetFirstHealthyNodeAsync(currentNodes, cancellationToken)
            ?? throw new HttpRequestException(
                $"No healthy current replica is available for repository '{placement.Name}'.");
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

using DistributedGitStorage.Data;
using DistributedGitStorage.Data.Enums;
using DistributedGitStorage.Data.Models;
using DistributedGitStorage.Services.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DistributedGitStorage.Services.Services;

internal sealed class ReplicationWorker(
    IDbContextFactory<DistributedGitStorageDbContext> dbContextFactory,
    StorageClusterClient cluster,
    IOptions<ReplicationOptions> options,
    ILogger<ReplicationWorker> logger) : BackgroundService
{
    private DateTimeOffset _nextReconciliationAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow >= _nextReconciliationAt)
                {
                    await BootstrapMissingReplicasAsync(stoppingToken);
                    await ReconcileOrphanedGenerationsAsync(stoppingToken);
                    _nextReconciliationAt = DateTimeOffset.UtcNow.AddSeconds(
                        options.Value.ReconciliationIntervalSeconds);
                }

                var job = await TryClaimJobAsync(stoppingToken);
                if (job is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds), stoppingToken);
                    continue;
                }

                await ProcessJobAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Replication worker iteration failed.");
                await Task.Delay(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds), stoppingToken);
            }
        }
    }

    private async Task BootstrapMissingReplicasAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var placements = await db.RepositoryPlacements
            .Include(item => item.Replicas)
            .ToArrayAsync(cancellationToken);
        foreach (var placement in placements)
        {
            var knownNodes = placement.Replicas
                .Select(item => item.StorageNode)
                .ToHashSet(StringComparer.Ordinal);
            var nodes = await cluster.GetNodesAsync(placement.StorageClusterId, cancellationToken);
            var topologyChanged = false;
            foreach (var node in nodes.Where(item => !knownNodes.Contains(item.Name)))
            {
                placement.Replicas.Add(new RepositoryReplica
                {
                    RepositoryId = placement.Id,
                    StorageNode = node.Name,
                    AppliedGeneration = placement.CurrentGeneration,
                    Status = ERepositoryReplicaStatus.Pending
                });
                topologyChanged = true;
            }

            if (topologyChanged)
            {
                var sourceNode = placement.Replicas.FirstOrDefault(item =>
                    item.Status == ERepositoryReplicaStatus.Healthy
                    && item.AppliedGeneration == placement.CurrentGeneration)?.StorageNode;
                if (sourceNode is not null)
                {
                    EnsureJobs(
                        db,
                        placement,
                        sourceNode,
                        nodes.Select(item => item.Name).ToHashSet(StringComparer.Ordinal));
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<ReplicationJob?> TryClaimJobAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var candidate = await db.ReplicationJobs
            .AsNoTracking()
            .Where(item =>
                ((item.Status == EReplicationJobStatus.Pending || item.Status == EReplicationJobStatus.Retry)
                    && item.NextAttemptAt <= now)
                || (item.Status == EReplicationJobStatus.Processing && item.LockedUntil < now))
            .OrderBy(item => item.NextAttemptAt)
            .Select(item => new { item.Id, item.Status, item.LockedUntil })
            .FirstOrDefaultAsync(cancellationToken);
        if (candidate is null)
        {
            return null;
        }

        var claimed = await db.ReplicationJobs
            .Where(item => item.Id == candidate.Id
                && item.Status == candidate.Status
                && item.LockedUntil == candidate.LockedUntil)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, EReplicationJobStatus.Processing)
                .SetProperty(item => item.LockedUntil, now.AddSeconds(options.Value.LeaseSeconds))
                .SetProperty(item => item.Attempt, item => item.Attempt + 1),
                cancellationToken);
        return claimed == 1
            ? await db.ReplicationJobs.AsNoTracking().SingleAsync(item => item.Id == candidate.Id, cancellationToken)
            : null;
    }

    private async Task ProcessJobAsync(ReplicationJob job, CancellationToken cancellationToken)
    {
        try
        {
            await using var stateDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var placementState = await stateDb.RepositoryPlacements
                .Include(item => item.Replicas)
                .AsNoTracking()
                .SingleAsync(item => item.Id == job.RepositoryId, cancellationToken);
            if (placementState.CurrentGeneration != job.Generation)
            {
                await CompleteObsoleteJobAsync(job.Id, cancellationToken);
                return;
            }

            await stateDb.RepositoryReplicas
                .Where(item => item.RepositoryId == job.RepositoryId && item.StorageNode == job.TargetNode)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, ERepositoryReplicaStatus.Replicating)
                    .SetProperty(item => item.LastAttemptAt, DateTimeOffset.UtcNow),
                    cancellationToken);

            var sourceNames = placementState.Replicas
                .Where(item => item.Status == ERepositoryReplicaStatus.Healthy
                    && item.AppliedGeneration == job.Generation
                    && item.StorageNode != job.TargetNode)
                .OrderByDescending(item => item.StorageNode == job.SourceNode)
                .Select(item => item.StorageNode);
            var sourceNode = await cluster.GetFirstHealthyNodeAsync(
                placementState.StorageClusterId,
                sourceNames,
                cancellationToken)
                ?? throw new HttpRequestException("No healthy current source replica is available.");
            var targetNode = await cluster.GetNodeAsync(
                placementState.StorageClusterId,
                job.TargetNode,
                cancellationToken);
            var sourceState = await cluster.GetRepositoryStateAsync(sourceNode, job.RepositoryId, cancellationToken);
            if (!sourceState.Exists || sourceState.RefsHash is null)
            {
                throw new InvalidOperationException("Source repository is unavailable.");
            }

            await cluster.ReplicateToNodeAsync(job.RepositoryId, sourceNode, targetNode, cancellationToken);
            var targetState = await cluster.GetRepositoryStateAsync(targetNode, job.RepositoryId, cancellationToken);
            if (!targetState.Exists || targetState.RefsHash != sourceState.RefsHash)
            {
                throw new InvalidOperationException("Replica refs fingerprint does not match the source.");
            }

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var trackedJob = await db.ReplicationJobs.SingleAsync(item => item.Id == job.Id, cancellationToken);
            trackedJob.SourceNode = sourceNode.Name;
            var replica = await db.RepositoryReplicas.SingleAsync(
                item => item.RepositoryId == job.RepositoryId && item.StorageNode == job.TargetNode,
                cancellationToken);
            var placement = await db.RepositoryPlacements.AsNoTracking().SingleAsync(
                item => item.Id == job.RepositoryId,
                cancellationToken);
            if (placement.CurrentGeneration == job.Generation)
            {
                replica.AppliedGeneration = job.Generation;
                replica.Status = ERepositoryReplicaStatus.Healthy;
                replica.RefsHash = targetState.RefsHash;
                replica.LastSuccessfulReplicationAt = DateTimeOffset.UtcNow;
                replica.LastAttemptAt = DateTimeOffset.UtcNow;
                replica.LastError = null;
            }

            trackedJob.Status = EReplicationJobStatus.Completed;
            trackedJob.CompletedAt = DateTimeOffset.UtcNow;
            trackedJob.LockedUntil = null;
            trackedJob.LastError = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await using var topologyDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var storageClusterId = await topologyDb.RepositoryPlacements
                .Where(item => item.Id == job.RepositoryId)
                .Select(item => item.StorageClusterId)
                .SingleAsync(cancellationToken);
            var targetNode = await cluster.GetNodeAsync(storageClusterId, job.TargetNode, cancellationToken);
            var targetIsHealthy = await cluster.IsHealthyAsync(targetNode, cancellationToken);
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var trackedJob = await db.ReplicationJobs.SingleAsync(item => item.Id == job.Id, cancellationToken);
            var replica = await db.RepositoryReplicas.SingleAsync(
                item => item.RepositoryId == job.RepositoryId && item.StorageNode == job.TargetNode,
                cancellationToken);
            var delaySeconds = Math.Min(300, (int)Math.Pow(2, Math.Min(trackedJob.Attempt, 8)) * 5);
            trackedJob.Status = EReplicationJobStatus.Retry;
            trackedJob.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
            trackedJob.LockedUntil = null;
            trackedJob.LastError = exception.Message;
            replica.Status = targetIsHealthy
                ? ERepositoryReplicaStatus.Lagging
                : ERepositoryReplicaStatus.Unavailable;
            replica.LastAttemptAt = DateTimeOffset.UtcNow;
            replica.LastError = exception.Message;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning(exception,
                "Replication of repository {RepositoryId} generation {Generation} to {TargetNode} will be retried.",
                job.RepositoryId, job.Generation, job.TargetNode);
        }
    }

    private async Task CompleteObsoleteJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var job = await db.ReplicationJobs.SingleAsync(item => item.Id == jobId, cancellationToken);
        job.Status = EReplicationJobStatus.Completed;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.LockedUntil = null;
        job.LastError = "Superseded by a newer repository generation.";
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ReconcileOrphanedGenerationsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var placements = await db.RepositoryPlacements
            .Include(item => item.Replicas)
            .Where(item => !item.Replicas.Any(replica =>
                replica.Status == ERepositoryReplicaStatus.Healthy
                && replica.AppliedGeneration == item.CurrentGeneration))
            .ToArrayAsync(cancellationToken);
        foreach (var placement in placements)
        {
            var sourceReplica = placement.Replicas.SingleOrDefault(item => item.StorageNode == placement.Storage);
            if (sourceReplica is null)
            {
                continue;
            }

            var sourceNode = await cluster.GetNodeAsync(
                placement.StorageClusterId,
                sourceReplica.StorageNode,
                cancellationToken);
            if (!await cluster.IsHealthyAsync(sourceNode, cancellationToken))
            {
                continue;
            }

            var state = await cluster.GetRepositoryStateAsync(sourceNode, placement.Id, cancellationToken);
            if (!state.Exists || state.RefsHash is null)
            {
                continue;
            }

            sourceReplica.Status = ERepositoryReplicaStatus.Healthy;
            sourceReplica.AppliedGeneration = placement.CurrentGeneration;
            sourceReplica.RefsHash = state.RefsHash;
            sourceReplica.LastSuccessfulReplicationAt = DateTimeOffset.UtcNow;
            sourceReplica.LastError = null;
            var activeNodes = await cluster.GetNodesAsync(placement.StorageClusterId, cancellationToken);
            EnsureJobs(
                db,
                placement,
                sourceReplica.StorageNode,
                activeNodes.Select(item => item.Name).ToHashSet(StringComparer.Ordinal));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    internal static void EnsureJobs(
        DistributedGitStorageDbContext db,
        RepositoryPlacement placement,
        string sourceNode,
        IReadOnlySet<string> activeNodeNames)
    {
        var existingTargets = db.ReplicationJobs
            .Where(item => item.RepositoryId == placement.Id && item.Generation == placement.CurrentGeneration)
            .Select(item => item.TargetNode)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var replica in placement.Replicas.Where(item =>
                     item.StorageNode != sourceNode && activeNodeNames.Contains(item.StorageNode)))
        {
            if (existingTargets.Add(replica.StorageNode))
            {
                db.ReplicationJobs.Add(new ReplicationJob
                {
                    Id = Guid.NewGuid(),
                    RepositoryId = placement.Id,
                    SourceNode = sourceNode,
                    TargetNode = replica.StorageNode,
                    Generation = placement.CurrentGeneration,
                    Status = EReplicationJobStatus.Pending,
                    NextAttemptAt = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
        }
    }
}

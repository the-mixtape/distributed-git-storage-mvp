using DistributedGitStorage.Data;
using DistributedGitStorage.Data.Models;
using DistributedGitStorage.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DistributedGitStorage.Services.Services;

internal sealed class ReplicationService(
    IDbContextFactory<DistributedGitStorageDbContext> dbContextFactory) : IReplicationService
{
    public async Task<IReadOnlyList<ReplicationJobInfo>> GetJobsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ReplicationJobs
            .AsNoTracking()
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new ReplicationJobInfo(
                item.Id,
                item.RepositoryId,
                item.SourceNode,
                item.TargetNode,
                item.Generation,
                item.Status.ToString(),
                item.Attempt,
                item.NextAttemptAt,
                item.LastError))
            .ToArrayAsync(cancellationToken);
    }

    public async Task RetryAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var job = await db.ReplicationJobs.SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Replication job '{jobId}' was not found.");
        job.Status = ReplicationJobStatus.Pending;
        job.NextAttemptAt = DateTimeOffset.UtcNow;
        job.LockedUntil = null;
        job.CompletedAt = null;
        job.LastError = null;
        await db.SaveChangesAsync(cancellationToken);
    }
}

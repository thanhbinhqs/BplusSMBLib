using System.Runtime.CompilerServices;
using SmbEnterprise.Core.Models;
using SmbEnterprise.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SmbEnterprise.Persistence;

/// <summary>
/// SQLite-backed job repository that persists all jobs and provides crash recovery.
/// </summary>
public sealed class SqliteJobRepository : IJobQueue
{
    private readonly IDbContextFactory<SmbJobsDbContext> _dbFactory;
    private readonly ILogger<SqliteJobRepository> _logger;

    public SqliteJobRepository(IDbContextFactory<SmbJobsDbContext> dbFactory, ILogger<SqliteJobRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reload pending/running jobs from DB after crash recovery.</summary>
    public async Task<IReadOnlyList<TransferJob>> RecoverJobsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entities = await db.Jobs
            .Where(j => j.Status == JobStatus.Running || j.Status == JobStatus.Queued)
            .OrderByDescending(j => j.Priority)
            .ThenBy(j => j.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Reset Running → Queued (was interrupted mid-transfer)
        foreach (var e in entities.Where(e => e.Status == JobStatus.Running))
        {
            e.Status = JobStatus.Queued;
            e.StartedAtUtc = null;
        }

        if (entities.Any(e => e.Status == JobStatus.Queued))
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var jobs = entities.Select(e => e.ToDomain()).ToList();
        _logger.LogInformation("Recovered {Count} pending jobs from database", jobs.Count);
        return jobs;
    }

    public async Task<TransferJob> EnqueueAsync(TransferJob job, CancellationToken cancellationToken = default)
    {
        job.Status = JobStatus.Queued;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Jobs.Add(TransferJobEntity.FromDomain(job));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Job persisted: {JobId}", job.JobId);
        return job;
    }

    public Task<TransferJob?> DequeueAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use InMemoryJobQueue for dequeue; SqliteJobRepository is for persistence only.");

    public async Task PauseAsync(Guid jobId, CancellationToken cancellationToken = default)
        => await UpdateStatusAsync(jobId, JobStatus.Paused, cancellationToken).ConfigureAwait(false);

    public async Task ResumeAsync(Guid jobId, CancellationToken cancellationToken = default)
        => await UpdateStatusAsync(jobId, JobStatus.Queued, cancellationToken).ConfigureAwait(false);

    public async Task CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
        => await UpdateStatusAsync(jobId, JobStatus.Cancelled, cancellationToken).ConfigureAwait(false);

    public async Task<TransferJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.Jobs.FindAsync([jobId], cancellationToken).ConfigureAwait(false);
        return entity?.ToDomain();
    }

    public async IAsyncEnumerable<TransferJob> GetAllJobsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var jobs = await db.Jobs
            .OrderByDescending(j => j.Priority).ThenBy(j => j.CreatedAtUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var job in jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return job.ToDomain();
        }
    }

    public async Task UpdateProgressAsync(Guid jobId, long bytesTransferred, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.Jobs.FindAsync([jobId], cancellationToken).ConfigureAwait(false);
        if (entity is null) return;
        entity.BytesTransferred = bytesTransferred;
        entity.Status = JobStatus.Running;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteJobAsync(Guid jobId, bool success, string? errorMessage = null, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.Jobs.FindAsync([jobId], cancellationToken).ConfigureAwait(false);
        if (entity is null) return;
        entity.Status = success ? JobStatus.Completed : JobStatus.Failed;
        entity.CompletedAtUtc = DateTime.UtcNow;
        entity.ErrorMessage = errorMessage;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateStatusAsync(Guid jobId, JobStatus status, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.Jobs.FindAsync([jobId], cancellationToken).ConfigureAwait(false);
        if (entity is null) return;
        entity.Status = status;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

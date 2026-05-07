using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SmbEnterprise.Core.Models;
using Microsoft.Extensions.Logging;

namespace SmbEnterprise.Jobs;

/// <summary>
/// In-memory job queue with persistence hook.
/// Priority ordering, pause/resume, and cancel support.
/// </summary>
public sealed class InMemoryJobQueue : IJobQueue, IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, TransferJob> _jobs = new();
    private readonly Channel<Guid> _readyChannel;
    private readonly ILogger<InMemoryJobQueue> _logger;
    private bool _disposed;

    public InMemoryJobQueue(ILogger<InMemoryJobQueue> logger)
    {
        _logger = logger;
        _readyChannel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = false
        });
    }

    public Task<TransferJob> EnqueueAsync(TransferJob job, CancellationToken cancellationToken = default)
    {
        job.Status = JobStatus.Queued;
        _jobs[job.JobId] = job;
        _readyChannel.Writer.TryWrite(job.JobId);
        _logger.LogInformation("Job enqueued: {JobId} {Source} → {Dest} priority={Priority}",
            job.JobId, job.SourcePath, job.DestinationPath, job.Priority);
        return Task.FromResult(job);
    }

    public async Task<TransferJob?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        while (await _readyChannel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_readyChannel.Reader.TryRead(out var jobId))
            {
                if (_jobs.TryGetValue(jobId, out var job) && job.Status == JobStatus.Queued)
                {
                    job.Status = JobStatus.Running;
                    job.StartedAtUtc = DateTime.UtcNow;
                    return job;
                }
            }
        }
        return null;
    }

    public Task PauseAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId, out var job) && job.Status == JobStatus.Running)
        {
            job.Status = JobStatus.Paused;
            _logger.LogInformation("Job paused: {JobId}", jobId);
        }
        return Task.CompletedTask;
    }

    public Task ResumeAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId, out var job) && job.Status == JobStatus.Paused)
        {
            job.Status = JobStatus.Queued;
            _readyChannel.Writer.TryWrite(jobId);
            _logger.LogInformation("Job resumed: {JobId}", jobId);
        }
        return Task.CompletedTask;
    }

    public Task CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = JobStatus.Cancelled;
            _logger.LogInformation("Job cancelled: {JobId}", jobId);
        }
        return Task.CompletedTask;
    }

    public Task<TransferJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        _jobs.TryGetValue(jobId, out var job);
        return Task.FromResult(job);
    }

    public async IAsyncEnumerable<TransferJob> GetAllJobsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var job in _jobs.Values.OrderByDescending(j => j.Priority).ThenBy(j => j.CreatedAtUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return job;
        }
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public Task UpdateProgressAsync(Guid jobId, long bytesTransferred, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId, out var job))
            job.BytesTransferred = bytesTransferred;
        return Task.CompletedTask;
    }

    public Task CompleteJobAsync(Guid jobId, bool success, string? errorMessage = null, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = success ? JobStatus.Completed : JobStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.ErrorMessage = errorMessage;
            _logger.LogInformation("Job complete: {JobId} success={Success}", jobId, success);
        }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _readyChannel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

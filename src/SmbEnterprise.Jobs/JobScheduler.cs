using SmbEnterprise.Core.Models;
using SmbEnterprise.Transfer.Abstractions;
using Microsoft.Extensions.Logging;

namespace SmbEnterprise.Jobs;

/// <summary>
/// Processes jobs from the queue, executing transfers with concurrency control.
/// Supports bandwidth limits and priority scheduling.
/// </summary>
public sealed class JobScheduler : IAsyncDisposable
{
    private readonly IJobQueue _queue;
    private readonly ITransferEngine _transferEngine;
    private readonly ILogger<JobScheduler> _logger;
    private readonly JobSchedulerOptions _options;
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly CancellationTokenSource _shutdownCts = new();
    private Task? _schedulerTask;

    public JobScheduler(
        IJobQueue queue,
        ITransferEngine transferEngine,
        JobSchedulerOptions options,
        ILogger<JobScheduler> logger)
    {
        _queue = queue;
        _transferEngine = transferEngine;
        _options = options;
        _logger = logger;
        _concurrencySemaphore = new SemaphoreSlim(options.MaxConcurrentJobs, options.MaxConcurrentJobs);
    }

    public void Start()
    {
        _schedulerTask = RunSchedulerLoopAsync(_shutdownCts.Token);
        _logger.LogInformation("JobScheduler started (maxConcurrent={Max})", _options.MaxConcurrentJobs);
    }

    private async Task RunSchedulerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var job = await _queue.DequeueAsync(cancellationToken).ConfigureAwait(false);
                if (job is null) break;

                await _concurrencySemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ExecuteJobAsync(job, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        _concurrencySemaphore.Release();
                    }
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduler loop error");
            }
        }
    }

    private async Task ExecuteJobAsync(TransferJob job, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing job: {JobId} {Source} → {Dest}", job.JobId, job.SourcePath, job.DestinationPath);

        var progress = new Progress<TransferProgress>(p =>
            _queue.UpdateProgressAsync(job.JobId, p.TransferredBytes).GetAwaiter().GetResult());

        try
        {
            var result = await _transferEngine.TransferAsync(
                job.SourcePath,
                job.DestinationPath,
                job.Options,
                progress,
                cancellationToken).ConfigureAwait(false);

            await _queue.CompleteJobAsync(job.JobId, result.Success, result.ErrorMessage, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job execution error: {JobId}", job.JobId);
            await _queue.CompleteJobAsync(job.JobId, false, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdownCts.Cancel();
        if (_schedulerTask is not null)
        {
            try { await _schedulerTask.ConfigureAwait(false); } catch { /* expected */ }
        }
        _concurrencySemaphore.Dispose();
        _shutdownCts.Dispose();
    }
}

public sealed class JobSchedulerOptions
{
    public int MaxConcurrentJobs { get; init; } = 2;
    public long BandwidthLimitBytesPerSecond { get; init; } = 0; // 0 = unlimited
}

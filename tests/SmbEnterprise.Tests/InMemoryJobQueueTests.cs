using SmbEnterprise.Core.Models;
using SmbEnterprise.Jobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmbEnterprise.Tests;

public class InMemoryJobQueueTests : IAsyncDisposable
{
    private readonly InMemoryJobQueue _queue = new(NullLogger<InMemoryJobQueue>.Instance);

    [Fact]
    public async Task EnqueueAndDequeue_ReturnsJob()
    {
        var job = MakeJob();
        await _queue.EnqueueAsync(job);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var dequeued = await _queue.DequeueAsync(cts.Token);

        Assert.NotNull(dequeued);
        Assert.Equal(job.JobId, dequeued!.JobId);
        Assert.Equal(JobStatus.Running, dequeued.Status);
    }

    [Fact]
    public async Task Cancel_SetsStatus()
    {
        var job = MakeJob();
        await _queue.EnqueueAsync(job);

        await _queue.CancelAsync(job.JobId);
        var fetched = await _queue.GetJobAsync(job.JobId);

        Assert.Equal(JobStatus.Cancelled, fetched!.Status);
    }

    [Fact]
    public async Task PauseResume_ChangesStatus()
    {
        var job = MakeJob();
        await _queue.EnqueueAsync(job);

        // manually set to running
        job.Status = JobStatus.Running;
        await _queue.PauseAsync(job.JobId);
        Assert.Equal(JobStatus.Paused, job.Status);

        await _queue.ResumeAsync(job.JobId);
        Assert.Equal(JobStatus.Queued, job.Status);
    }

    [Fact]
    public async Task CompleteJob_SetsSuccessStatus()
    {
        var job = MakeJob();
        await _queue.EnqueueAsync(job);

        await _queue.CompleteJobAsync(job.JobId, success: true);
        var fetched = await _queue.GetJobAsync(job.JobId);

        Assert.Equal(JobStatus.Completed, fetched!.Status);
    }

    [Fact]
    public async Task UpdateProgress_UpdatesBytesTransferred()
    {
        var job = MakeJob();
        await _queue.EnqueueAsync(job);

        await _queue.UpdateProgressAsync(job.JobId, 1024 * 1024);
        var fetched = await _queue.GetJobAsync(job.JobId);

        Assert.Equal(1024 * 1024, fetched!.BytesTransferred);
    }

    [Fact]
    public async Task GetAllJobs_ReturnsAllJobs()
    {
        var job1 = MakeJob(JobPriority.High);
        var job2 = MakeJob(JobPriority.Normal);
        await _queue.EnqueueAsync(job1);
        await _queue.EnqueueAsync(job2);

        var all = new List<TransferJob>();
        await foreach (var j in _queue.GetAllJobsAsync())
            all.Add(j);

        Assert.Equal(2, all.Count);
        // High priority should come first
        Assert.Equal(JobPriority.High, all[0].Priority);
    }

    private static TransferJob MakeJob(JobPriority priority = JobPriority.Normal) => new()
    {
        JobId = Guid.NewGuid(),
        SourcePath = @"\\server\share\file.txt",
        DestinationPath = @"\\dest\share\file.txt",
        Priority = priority,
        Options = new TransferOptions()
    };

    public async ValueTask DisposeAsync() => await _queue.DisposeAsync();
}

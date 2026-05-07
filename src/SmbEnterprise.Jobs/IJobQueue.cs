using SmbEnterprise.Core.Models;

namespace SmbEnterprise.Jobs;

/// <summary>Interface for managing the transfer job queue.</summary>
public interface IJobQueue
{
    Task<TransferJob> EnqueueAsync(TransferJob job, CancellationToken cancellationToken = default);
    Task<TransferJob?> DequeueAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task ResumeAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task CancelAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<TransferJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<TransferJob> GetAllJobsAsync(CancellationToken cancellationToken = default);
    Task UpdateProgressAsync(Guid jobId, long bytesTransferred, CancellationToken cancellationToken = default);
    Task CompleteJobAsync(Guid jobId, bool success, string? errorMessage = null, CancellationToken cancellationToken = default);
}

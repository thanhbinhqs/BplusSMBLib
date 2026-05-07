namespace SmbEnterprise.Core.Models;

public enum JobStatus
{
    Queued,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled,
    Retrying
}

public enum JobPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

/// <summary>A single transfer job descriptor stored persistently.</summary>
public sealed class TransferJob
{
    public Guid JobId { get; init; } = Guid.NewGuid();
    public required string SourcePath { get; set; }
    public required string DestinationPath { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public JobPriority Priority { get; set; } = JobPriority.Normal;
    public TransferOptions Options { get; set; } = new();
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int RetryCount { get; set; }
    public string? ErrorMessage { get; set; }
    public long BytesTransferred { get; set; }
    public long TotalBytes { get; set; }
    public string? CorrelationId { get; set; }
}

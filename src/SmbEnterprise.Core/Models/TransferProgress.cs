namespace SmbEnterprise.Core.Models;

/// <summary>Live snapshot of transfer progress for a single file.</summary>
public sealed class TransferProgress
{
    public Guid SessionId { get; init; }
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public long TotalBytes { get; set; }
    public long TransferredBytes { get; set; }
    public long VerifiedBytes { get; set; }
    public int RetryCount { get; set; }
    public int CorruptChunkCount { get; set; }
    public TransferStage Stage { get; set; } = TransferStage.Pending;
    public double SpeedBytesPerSecond { get; set; }
    public TimeSpan Elapsed { get; set; }
    public TimeSpan? Eta { get; set; }
    public double PercentComplete => TotalBytes == 0 ? 0 : Math.Min(100.0, TransferredBytes * 100.0 / TotalBytes);
}

public enum TransferStage
{
    Pending,
    Connecting,
    Reading,
    Writing,
    Verifying,
    Retrying,
    Paused,
    Completed,
    Failed,
    Cancelled
}

/// <summary>Groups multiple file transfers into a logical session.</summary>
public sealed class TransferSession
{
    public Guid SessionId { get; } = Guid.NewGuid();
    public DateTime StartedUtc { get; } = DateTime.UtcNow;
    public DateTime? CompletedUtc { get; set; }
    public string? CorrelationId { get; set; }
    public IReadOnlyList<TransferProgress> FileProgresses { get; set; } = [];
    public TransferStage OverallStage { get; set; } = TransferStage.Pending;
    public long TotalBytes => FileProgresses.Sum(p => p.TotalBytes);
    public long TransferredBytes => FileProgresses.Sum(p => p.TransferredBytes);
}

namespace SmbEnterprise.Core.Models;

/// <summary>
/// Progress tracking cho từng destination riêng biệt trong quá trình transfer multi-destination.
/// </summary>
public sealed class MultiDestinationProgress
{
    public Guid SessionId { get; init; }
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public int DestinationIndex { get; init; }
    public long TotalBytes { get; set; }
    public long TransferredBytes { get; set; }
    public TransferStage Stage { get; set; } = TransferStage.Pending;
    public double SpeedBytesPerSecond { get; set; }
    public TimeSpan Elapsed { get; set; }
    public TimeSpan? Eta { get; set; }
    public double PercentComplete => TotalBytes == 0 ? 0 : Math.Min(100.0, TransferredBytes * 100.0 / TotalBytes);
    public int RetryCount { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Cờ đánh dấu destination này bị throttled do quá chậm so với các destination khác.
    /// </summary>
    public bool IsThrottled { get; set; }

    /// <summary>
    /// Thời điểm bắt đầu transfer destination này.
    /// </summary>
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thời điểm hoàn thành (hoặc failed).
    /// </summary>
    public DateTime? CompletedUtc { get; set; }
}

/// <summary>
/// Tổng hợp progress của tất cả destinations trong một lần transfer multi-destination.
/// </summary>
public sealed class AggregatedMultiDestinationProgress
{
    public Guid SessionId { get; init; }
    public required string SourcePath { get; init; }
    public long TotalBytes { get; set; }
    public long SourceBytesRead { get; set; }
    public TimeSpan Elapsed { get; set; }
    public double SourceReadSpeedBytesPerSecond { get; set; }

    /// <summary>
    /// Progress riêng cho từng destination.
    /// </summary>
    public required List<MultiDestinationProgress> DestinationProgresses { get; init; }

    /// <summary>
    /// Số destination đã hoàn thành thành công.
    /// </summary>
    public int CompletedCount => DestinationProgresses.Count(d => d.Stage == TransferStage.Completed);

    /// <summary>
    /// Số destination đang bị throttled.
    /// </summary>
    public int ThrottledCount => DestinationProgresses.Count(d => d.IsThrottled);

    /// <summary>
    /// Số destination bị failed.
    /// </summary>
    public int FailedCount => DestinationProgresses.Count(d => d.Stage == TransferStage.Failed);

    /// <summary>
    /// Tốc độ trung bình của tất cả destinations (bytes/sec).
    /// </summary>
    public double AverageWriteSpeedBytesPerSecond => 
        DestinationProgresses.Where(d => d.Stage is TransferStage.Writing or TransferStage.Completed)
            .Average(d => d.SpeedBytesPerSecond);

    /// <summary>
    /// Tốc độ chậm nhất trong các destinations đang active (bytes/sec).
    /// </summary>
    public double SlowestWriteSpeedBytesPerSecond => 
        DestinationProgresses.Where(d => d.Stage == TransferStage.Writing)
            .DefaultIfEmpty()
            .Min(d => d?.SpeedBytesPerSecond ?? 0);
}

namespace SmbEnterprise.Core.Models;

/// <summary>
/// Result cho việc transfer directory đến nhiều destinations.
/// </summary>
public sealed class DirectoryMultiDestinationTransferResult
{
    public required List<FileTransferSummary> FileResults { get; init; }
    public long TotalBytesTransferred { get; set; }
    public TimeSpan Duration { get; set; }
    public int TotalFiles { get; set; }
    public int SuccessfulFiles { get; set; }
    public int FailedFiles { get; set; }
    public int DestinationCount { get; set; }
    public SlowConnectionStats? SlowConnectionStats { get; set; }
}

/// <summary>
/// Tóm tắt kết quả transfer cho một file đến nhiều destinations.
/// </summary>
public sealed class FileTransferSummary
{
    public required string SourcePath { get; init; }
    public required List<DestinationTransferResult> DestinationResults { get; init; }
    public long FileSize { get; set; }
    public TimeSpan Duration { get; set; }
    public bool AllDestinationsSucceeded => DestinationResults.All(r => r.Success);
    public int SuccessfulDestinations => DestinationResults.Count(r => r.Success);
}

/// <summary>
/// Result cho một destination cụ thể.
/// </summary>
public sealed class DestinationTransferResult
{
    public required string DestinationPath { get; init; }
    public int DestinationIndex { get; init; }
    public bool Success { get; init; }
    public long BytesTransferred { get; init; }
    public TimeSpan Duration { get; init; }
    public double AverageSpeedBytesPerSecond { get; init; }
    public bool WasThrottled { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Progress tracking cho directory transfer với nhiều destinations.
/// </summary>
public sealed class DirectoryTransferProgress
{
    public Guid SessionId { get; init; }
    public required string SourceDirectory { get; init; }
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public int SuccessfulFiles { get; set; }
    public int FailedFiles { get; set; }
    public long TotalBytes { get; set; }
    public long TransferredBytes { get; set; }
    public TimeSpan Elapsed { get; set; }
    public string? CurrentFile { get; set; }

    /// <summary>
    /// Progress của file đang được transfer hiện tại.
    /// </summary>
    public AggregatedMultiDestinationProgress? CurrentFileProgress { get; set; }

    /// <summary>
    /// Danh sách các file đã hoàn thành.
    /// </summary>
    public List<FileTransferSummary> CompletedFiles { get; set; } = new();

    public double OverallPercentComplete => TotalBytes == 0 ? 0 : Math.Min(100.0, TransferredBytes * 100.0 / TotalBytes);
}

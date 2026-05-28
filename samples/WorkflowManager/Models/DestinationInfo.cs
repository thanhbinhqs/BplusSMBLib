namespace WorkflowManager.Models;

/// <summary>
/// Thông tin về một destination để copy files đến
/// </summary>
public class DestinationInfo
{
    public required string Name { get; set; }
    public required string UncPath { get; set; }
    public required string Username { get; set; }
    public required string Password { get; set; }
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Kết quả copy đến một destination cụ thể
/// </summary>
public class DestinationCopyResult
{
    public required string DestinationName { get; set; }
    public required string DestinationPath { get; set; }
    public DestinationStatus Status { get; set; } = DestinationStatus.Pending;
    public int FilesTotal { get; set; }
    public int FilesCopied { get; set; }
    public int FilesVerified { get; set; }
    public int FilesFailed { get; set; }
    public List<FileDestinationCopyInfo> Files { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    public TimeSpan? Duration => EndTime.HasValue && StartTime.HasValue 
        ? EndTime.Value - StartTime.Value 
        : null;
}

/// <summary>
/// Status của việc copy đến một destination
/// </summary>
public enum DestinationStatus
{
    Pending,
    Connecting,
    Copying,
    Verifying,
    Completed,
    CompletedWithErrors,
    Failed
}

/// <summary>
/// Thông tin copy một file đến một destination cụ thể
/// </summary>
public class FileDestinationCopyInfo
{
    public required string FileName { get; set; }
    public long FileSize { get; set; }
    public long BytesCopied { get; set; }
    public string? ExpectedHash { get; set; }
    public string? ActualHash { get; set; }
    public FileCopyStatus Status { get; set; }
    public string? ErrorMessage { get; set; }

    public double Progress => FileSize > 0 ? (BytesCopied * 100.0 / FileSize) : 0;
    public bool IsHashMatch => !string.IsNullOrEmpty(ExpectedHash) 
        && !string.IsNullOrEmpty(ActualHash) 
        && ExpectedHash.Equals(ActualHash, StringComparison.OrdinalIgnoreCase);
}

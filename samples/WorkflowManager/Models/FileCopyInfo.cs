namespace WorkflowManager.Models;

/// <summary>
/// Thông tin về một file đang được copy
/// </summary>
public sealed class FileCopyInfo
{
    public required string FileName { get; init; }
    public required long FileSize { get; init; }
    public long BytesCopied { get; set; }
    public string? ExpectedHash { get; init; }
    public string? ActualHash { get; set; }
    public FileCopyStatus Status { get; set; } = FileCopyStatus.Pending;
    public string? ErrorMessage { get; set; }

    public double Progress => FileSize > 0 ? (double)BytesCopied / FileSize * 100 : 0;

    public bool IsVerified => Status == FileCopyStatus.Verified;

    public bool IsHashMatch => !string.IsNullOrEmpty(ExpectedHash) && 
                               !string.IsNullOrEmpty(ActualHash) && 
                               ExpectedHash.Equals(ActualHash, StringComparison.OrdinalIgnoreCase);
}

public enum FileCopyStatus
{
    Pending,
    Copying,
    CopyComplete,
    Verifying,
    Verified,
    Failed,
    HashMismatch
}

namespace SmbEnterprise.Core.Models;

/// <summary>Outcome of a single file transfer operation.</summary>
public sealed class TransferResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public long BytesTransferred { get; init; }
    public TimeSpan Duration { get; init; }
    public int RetryCount { get; init; }
    public bool ChecksumVerified { get; init; }
}

/// <summary>Outcome of a multi-destination transfer.</summary>
public sealed class MultiDestinationTransferResult
{
    public IReadOnlyList<(string Destination, TransferResult Result)> Results { get; init; } = [];
    public bool AllSucceeded => Results.All(r => r.Result.Success);
    public long BytesRead { get; init; }
    public TimeSpan Duration { get; init; }
}

namespace SmbEnterprise.Core.Models;

/// <summary>Options controlling transfer behavior.</summary>
public sealed class TransferOptions
{
    /// <summary>Initial chunk size in bytes. Adaptive engine may change this at runtime.</summary>
    public int ChunkSize { get; init; } = 1 * 1024 * 1024; // 1 MB default

    /// <summary>Maximum chunk size allowed by adaptive engine.</summary>
    public int MaxChunkSize { get; init; } = 16 * 1024 * 1024; // 16 MB

    /// <summary>Minimum chunk size allowed by adaptive engine.</summary>
    public int MinChunkSize { get; init; } = 64 * 1024; // 64 KB

    /// <summary>Number of parallel file transfer workers.</summary>
    public int MaxParallelWorkers { get; init; } = 4;

    /// <summary>Max retries per chunk on transient failure.</summary>
    public int MaxChunkRetries { get; init; } = 5;

    /// <summary>Max retries for full reconnect cycles.</summary>
    public int MaxReconnectRetries { get; init; } = 3;

    /// <summary>Enable checksum verification after copy.</summary>
    public bool VerifyAfterCopy { get; init; } = true;

    /// <summary>Algorithm to use for integrity checking.</summary>
    public ChecksumAlgorithm ChecksumAlgorithm { get; init; } = ChecksumAlgorithm.XxHash64;

    /// <summary>Max depth of the write-queue channel (backpressure).</summary>
    public int WriteQueueDepth { get; init; } = 16;

    /// <summary>Enable read-ahead pre-fetching.</summary>
    public bool EnableReadAhead { get; init; } = true;

    /// <summary>Overwrite if destination exists.</summary>
    public bool Overwrite { get; init; } = false;

    /// <summary>Resume transfer if partial destination exists.</summary>
    public bool Resume { get; init; } = true;

    /// <summary>Optional bandwidth throttle (bytes/second, 0 = unlimited).</summary>
    public long BandwidthLimitBytesPerSecond { get; init; } = 0;
}

public enum ChecksumAlgorithm
{
    None,
    Crc32,
    Md5,
    Sha256,
    XxHash64
}

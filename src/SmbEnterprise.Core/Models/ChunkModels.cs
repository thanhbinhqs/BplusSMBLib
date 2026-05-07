namespace SmbEnterprise.Core.Models;

/// <summary>Represents a single chunk of a file during transfer.</summary>
public sealed class ChunkInfo
{
    public required string FilePath { get; init; }
    public int Index { get; init; }
    public long Offset { get; init; }
    public int Length { get; init; }
    public bool IsLastChunk { get; init; }
}

/// <summary>Checksum result for a single chunk.</summary>
public sealed class ChunkChecksum
{
    public required string FilePath { get; init; }
    public int ChunkIndex { get; init; }
    public long Offset { get; init; }
    public int Length { get; init; }
    public ChecksumAlgorithm Algorithm { get; init; }
    public required byte[] Hash { get; init; }
    public required string HexHash { get; init; }
}

/// <summary>Full file checksum result.</summary>
public sealed class FileChecksum
{
    public required string FilePath { get; init; }
    public long FileSize { get; init; }
    public ChecksumAlgorithm Algorithm { get; init; }
    public required byte[] Hash { get; init; }
    public required string HexHash { get; init; }
    public DateTime ComputedAtUtc { get; } = DateTime.UtcNow;
}

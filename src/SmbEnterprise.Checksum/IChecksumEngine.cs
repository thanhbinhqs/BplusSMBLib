using SmbEnterprise.Core.Models;

namespace SmbEnterprise.Checksum;

/// <summary>Computes checksums for streams/buffers. Thread-safe; stateless per call.</summary>
public interface IChecksumEngine
{
    /// <summary>Compute checksum of a complete buffer.</summary>
    ChunkChecksum ComputeChunk(ReadOnlySpan<byte> data, int chunkIndex, long offset, string filePath);

    /// <summary>Compute checksum of a file by streaming it completely.</summary>
    Task<FileChecksum> ComputeFileAsync(string filePath, Func<long, Memory<byte>, CancellationToken, ValueTask<int>> reader, long fileSize, CancellationToken cancellationToken = default);

    ChecksumAlgorithm Algorithm { get; }
}

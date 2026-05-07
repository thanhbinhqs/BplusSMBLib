using System.Buffers;
using System.IO.Hashing;
using System.Security.Cryptography;
using K4os.Hash.xxHash;
using SmbEnterprise.Core.Models;

namespace SmbEnterprise.Checksum;

/// <summary>
/// xxHash64 — fastest, suitable for integrity checking of large files.
/// Uses System.IO.Hashing for .NET 8 and K4os for compatibility.
/// </summary>
public sealed class XxHash64ChecksumEngine : IChecksumEngine
{
    public ChecksumAlgorithm Algorithm => ChecksumAlgorithm.XxHash64;

    public ChunkChecksum ComputeChunk(ReadOnlySpan<byte> data, int chunkIndex, long offset, string filePath)
    {
        var hash = XXH64.DigestOf(data);
        var bytes = BitConverter.GetBytes(hash);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return new ChunkChecksum
        {
            FilePath = filePath,
            ChunkIndex = chunkIndex,
            Offset = offset,
            Length = data.Length,
            Algorithm = Algorithm,
            Hash = bytes,
            HexHash = Convert.ToHexString(bytes)
        };
    }

    public async Task<FileChecksum> ComputeFileAsync(
        string filePath,
        Func<long, Memory<byte>, CancellationToken, ValueTask<int>> reader,
        long fileSize,
        CancellationToken cancellationToken = default)
    {
        const int bufferSize = 1 * 1024 * 1024; // 1MB chunks
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        var hasher = new XxHash64();
        long offset = 0;

        try
        {
            while (offset < fileSize)
            {
                var toRead = (int)Math.Min(bufferSize, fileSize - offset);
                var bytesRead = await reader(offset, buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0) break;

                hasher.Append(buffer.AsSpan(0, bytesRead));
                offset += bytesRead;
            }

            var hashBytes = hasher.GetCurrentHash().ToArray();
            return new FileChecksum
            {
                FilePath = filePath,
                FileSize = fileSize,
                Algorithm = Algorithm,
                Hash = hashBytes,
                HexHash = Convert.ToHexString(hashBytes)
            };
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

/// <summary>CRC32 checksum engine using System.IO.Hashing.</summary>
public sealed class Crc32ChecksumEngine : IChecksumEngine
{
    public ChecksumAlgorithm Algorithm => ChecksumAlgorithm.Crc32;

    public ChunkChecksum ComputeChunk(ReadOnlySpan<byte> data, int chunkIndex, long offset, string filePath)
    {
        var hasher = new Crc32();
        hasher.Append(data);
        var bytes = hasher.GetCurrentHash().ToArray();
        return new ChunkChecksum
        {
            FilePath = filePath,
            ChunkIndex = chunkIndex,
            Offset = offset,
            Length = data.Length,
            Algorithm = Algorithm,
            Hash = bytes,
            HexHash = Convert.ToHexString(bytes)
        };
    }

    public async Task<FileChecksum> ComputeFileAsync(
        string filePath,
        Func<long, Memory<byte>, CancellationToken, ValueTask<int>> reader,
        long fileSize,
        CancellationToken cancellationToken = default)
    {
        const int bufferSize = 1 * 1024 * 1024;
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        var hasher = new Crc32();
        long offset = 0;

        try
        {
            while (offset < fileSize)
            {
                var toRead = (int)Math.Min(bufferSize, fileSize - offset);
                var bytesRead = await reader(offset, buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0) break;

                hasher.Append(buffer.AsSpan(0, bytesRead));
                offset += bytesRead;
            }

            var hashBytes = hasher.GetCurrentHash().ToArray();
            return new FileChecksum
            {
                FilePath = filePath,
                FileSize = fileSize,
                Algorithm = Algorithm,
                Hash = hashBytes,
                HexHash = Convert.ToHexString(hashBytes)
            };
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

/// <summary>SHA-256 checksum engine for cryptographic-grade integrity verification.</summary>
public sealed class Sha256ChecksumEngine : IChecksumEngine
{
    public ChecksumAlgorithm Algorithm => ChecksumAlgorithm.Sha256;

    public ChunkChecksum ComputeChunk(ReadOnlySpan<byte> data, int chunkIndex, long offset, string filePath)
    {
        var hash = SHA256.HashData(data);
        return new ChunkChecksum
        {
            FilePath = filePath,
            ChunkIndex = chunkIndex,
            Offset = offset,
            Length = data.Length,
            Algorithm = Algorithm,
            Hash = hash,
            HexHash = Convert.ToHexString(hash)
        };
    }

    public async Task<FileChecksum> ComputeFileAsync(
        string filePath,
        Func<long, Memory<byte>, CancellationToken, ValueTask<int>> reader,
        long fileSize,
        CancellationToken cancellationToken = default)
    {
        const int bufferSize = 1 * 1024 * 1024;
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long offset = 0;

        try
        {
            while (offset < fileSize)
            {
                var toRead = (int)Math.Min(bufferSize, fileSize - offset);
                var bytesRead = await reader(offset, buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0) break;

                hasher.AppendData(buffer, 0, bytesRead);
                offset += bytesRead;
            }

            var hashBytes = hasher.GetHashAndReset();
            return new FileChecksum
            {
                FilePath = filePath,
                FileSize = fileSize,
                Algorithm = Algorithm,
                Hash = hashBytes,
                HexHash = Convert.ToHexString(hashBytes)
            };
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

/// <summary>MD5 checksum engine (fast, not cryptographically secure).</summary>
public sealed class Md5ChecksumEngine : IChecksumEngine
{
    public ChecksumAlgorithm Algorithm => ChecksumAlgorithm.Md5;

    public ChunkChecksum ComputeChunk(ReadOnlySpan<byte> data, int chunkIndex, long offset, string filePath)
    {
        var hash = MD5.HashData(data);
        return new ChunkChecksum
        {
            FilePath = filePath,
            ChunkIndex = chunkIndex,
            Offset = offset,
            Length = data.Length,
            Algorithm = Algorithm,
            Hash = hash,
            HexHash = Convert.ToHexString(hash)
        };
    }

    public async Task<FileChecksum> ComputeFileAsync(
        string filePath,
        Func<long, Memory<byte>, CancellationToken, ValueTask<int>> reader,
        long fileSize,
        CancellationToken cancellationToken = default)
    {
        const int bufferSize = 1 * 1024 * 1024;
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        long offset = 0;

        try
        {
            while (offset < fileSize)
            {
                var toRead = (int)Math.Min(bufferSize, fileSize - offset);
                var bytesRead = await reader(offset, buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0) break;

                hasher.AppendData(buffer, 0, bytesRead);
                offset += bytesRead;
            }

            var hashBytes = hasher.GetHashAndReset();
            return new FileChecksum
            {
                FilePath = filePath,
                FileSize = fileSize,
                Algorithm = Algorithm,
                Hash = hashBytes,
                HexHash = Convert.ToHexString(hashBytes)
            };
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

using SmbEnterprise.Checksum;
using SmbEnterprise.Core.Models;

namespace SmbEnterprise.Tests;

public class ChecksumEngineTests
{
    [Theory]
    [InlineData(ChecksumAlgorithm.XxHash64)]
    [InlineData(ChecksumAlgorithm.Crc32)]
    [InlineData(ChecksumAlgorithm.Sha256)]
    [InlineData(ChecksumAlgorithm.Md5)]
    public void ComputeChunk_SameData_ReturnsSameHash(ChecksumAlgorithm algorithm)
    {
        var engine = ChecksumEngineFactory.Create(algorithm);
        var data = new byte[1024];
        new Random(42).NextBytes(data);

        var hash1 = engine.ComputeChunk(data, 0, 0, "test.bin");
        var hash2 = engine.ComputeChunk(data, 0, 0, "test.bin");

        Assert.Equal(hash1.HexHash, hash2.HexHash);
    }

    [Theory]
    [InlineData(ChecksumAlgorithm.XxHash64)]
    [InlineData(ChecksumAlgorithm.Crc32)]
    [InlineData(ChecksumAlgorithm.Sha256)]
    [InlineData(ChecksumAlgorithm.Md5)]
    public void ComputeChunk_DifferentData_ReturnsDifferentHash(ChecksumAlgorithm algorithm)
    {
        var engine = ChecksumEngineFactory.Create(algorithm);
        var data1 = new byte[1024];
        var data2 = new byte[1024];
        new Random(1).NextBytes(data1);
        new Random(2).NextBytes(data2);

        var hash1 = engine.ComputeChunk(data1, 0, 0, "test.bin");
        var hash2 = engine.ComputeChunk(data2, 0, 0, "test.bin");

        Assert.NotEqual(hash1.HexHash, hash2.HexHash);
    }

    [Theory]
    [InlineData(ChecksumAlgorithm.XxHash64)]
    [InlineData(ChecksumAlgorithm.Crc32)]
    [InlineData(ChecksumAlgorithm.Sha256)]
    [InlineData(ChecksumAlgorithm.Md5)]
    public async Task ComputeFileAsync_SmallFile_ReturnsConsistentHash(ChecksumAlgorithm algorithm)
    {
        var engine = ChecksumEngineFactory.Create(algorithm);
        var data = new byte[4096];
        new Random(99).NextBytes(data);

        ValueTask<int> Reader(long offset, Memory<byte> buffer, CancellationToken ct)
        {
            if (offset >= data.Length) return new ValueTask<int>(0);
            var toCopy = (int)Math.Min(buffer.Length, data.Length - (int)offset);
            data.AsSpan((int)offset, toCopy).CopyTo(buffer.Span);
            return new ValueTask<int>(toCopy);
        }

        var result1 = await engine.ComputeFileAsync("test.bin", Reader, data.Length, default);
        var result2 = await engine.ComputeFileAsync("test.bin", Reader, data.Length, default);

        Assert.Equal(result1.HexHash, result2.HexHash);
    }

    [Fact]
    public void ChecksumEngineFactory_UnknownAlgorithm_Throws()
    {
        Assert.Throws<NotSupportedException>(() => ChecksumEngineFactory.Create((ChecksumAlgorithm)999));
    }
}

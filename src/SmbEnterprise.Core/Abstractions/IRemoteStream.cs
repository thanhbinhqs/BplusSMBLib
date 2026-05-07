namespace SmbEnterprise.Core.Abstractions;

/// <summary>A read-only stream over a remote file, supporting async read with seek.</summary>
public interface IRemoteReadStream : IAsyncDisposable
{
    long Length { get; }
    long Position { get; }
    bool CanSeek { get; }

    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
    Task SeekAsync(long offset, CancellationToken cancellationToken = default);
}

/// <summary>A write-only stream to a remote file destination.</summary>
public interface IRemoteWriteStream : IAsyncDisposable
{
    long Position { get; }

    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
    Task FlushAsync(CancellationToken cancellationToken = default);
}

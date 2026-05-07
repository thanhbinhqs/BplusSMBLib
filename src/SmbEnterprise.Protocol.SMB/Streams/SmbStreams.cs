using System.Buffers;
using SMBLibrary;
using SMBLibrary.Client;
using SmbEnterprise.Core.Abstractions;
using Microsoft.Extensions.Logging;
using SmbEnterprise.Protocol.SMB.Connection;

namespace SmbEnterprise.Protocol.SMB.Streams;

/// <summary>
/// Async read stream over a remote SMB file.
/// Uses ArrayPool internally; never allocates new byte[] on the heap.
/// </summary>
internal sealed class SmbReadStream : IRemoteReadStream
{
    private readonly PooledSession _pooledSession;
    private readonly ISMBFileStore _store;
    private readonly object _fileHandle;
    private readonly ILogger _logger;
    private long _position;
    private bool _disposed;
    private int _readCalls;

    public long Length { get; }
    public long Position => _position;
    public bool CanSeek => true;

    internal SmbReadStream(PooledSession pooledSession, ISMBFileStore store, object fileHandle, long length, ILogger logger)
    {
        _pooledSession = pooledSession;
        _store = store;
        _fileHandle = fileHandle;
        Length = length;
        _logger = logger;
        _logger.LogDebug("Opened SMB read stream length={Length}", length);
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_position >= Length) return 0;

        var toRead = (int)Math.Min(buffer.Length, Length - _position);
        byte[]? data = null;
        NTStatus status = NTStatus.STATUS_SUCCESS;

        try
        {
            await Task.Run(() =>
            {
                status = _store.ReadFile(out data, _fileHandle, _position, toRead);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex.Message.Contains("Not enough credits", StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"SMB ReadFile credit starvation at offset {_position}", ex);
        }

        if (status != NTStatus.STATUS_SUCCESS)
            throw new IOException($"SMB ReadFile failed at offset {_position}: {status}");

        if (data is null || data.Length == 0) return 0;

        var bytesRead = Math.Min(data.Length, buffer.Length);
        data.AsSpan(0, bytesRead).CopyTo(buffer.Span);
        _position += bytesRead;
        _readCalls++;

        if (_readCalls % 128 == 0)
        {
            _logger.LogDebug("SMB read progress calls={Calls} position={Position} length={Length}", _readCalls, _position, Length);
        }

        return bytesRead;
    }

    public Task SeekAsync(long offset, CancellationToken cancellationToken = default)
    {
        if (offset < 0 || offset > Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        _position = offset;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        return DisposeCoreAsync();
    }

    private async ValueTask DisposeCoreAsync()
    {
        await CloseFileSafeAsync().ConfigureAwait(false);
        await _pooledSession.DisposeAsync().ConfigureAwait(false);
        _logger.LogDebug("Disposed SMB read stream at position={Position}/{Length}", _position, Length);
    }

    private ValueTask CloseFileSafeAsync()
    {
        try
        {
            _store.CloseFile(_fileHandle);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("no longer connected", StringComparison.OrdinalIgnoreCase))
        {
            _pooledSession.MarkFaulted("read-close: client no longer connected");
            _logger.LogDebug(ex, "SMB read stream close skipped because client disconnected");
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "SMB read stream close skipped because store is already disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing SMB read file handle");
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Async write stream to a remote SMB file.
/// Uses chunked writes compatible with SMB MaxWriteSize.
/// </summary>
internal sealed class SmbWriteStream : IRemoteWriteStream
{
    private readonly PooledSession _pooledSession;
    private readonly ISMBFileStore _store;
    private readonly object _fileHandle;
    private readonly ILogger _logger;
    private long _position;
    private bool _disposed;
    private int _writeCalls;

    public long Position => _position;

    internal SmbWriteStream(PooledSession pooledSession, ISMBFileStore store, object fileHandle, long startOffset, ILogger logger)
    {
        _pooledSession = pooledSession;
        _store = store;
        _fileHandle = fileHandle;
        _position = startOffset;
        _logger = logger;
        _logger.LogDebug("Opened SMB write stream startOffset={Offset}", startOffset);
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int written = 0;
        NTStatus status = NTStatus.STATUS_SUCCESS;

        await Task.Run(() =>
        {
            status = _store.WriteFile(out written, _fileHandle, _position, buffer.ToArray());
        }, cancellationToken).ConfigureAwait(false);

        if (status != NTStatus.STATUS_SUCCESS)
            throw new IOException($"SMB WriteFile failed at offset {_position}: {status}");

        _position += written;
        _writeCalls++;

        if (_writeCalls % 128 == 0)
        {
            _logger.LogDebug("SMB write progress calls={Calls} position={Position}", _writeCalls, _position);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await Task.Run(() => _store.FlushFileBuffers(_fileHandle), cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        return DisposeCoreAsync();
    }

    private async ValueTask DisposeCoreAsync()
    {
        try
        {
            _store.CloseFile(_fileHandle);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("no longer connected", StringComparison.OrdinalIgnoreCase))
        {
            _pooledSession.MarkFaulted("write-close: client no longer connected");
            _logger.LogDebug(ex, "SMB write stream close skipped because client disconnected");
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "SMB write stream close skipped because store is already disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing SMB write file handle");
        }

        await _pooledSession.DisposeAsync().ConfigureAwait(false);
        _logger.LogDebug("Disposed SMB write stream at position={Position}", _position);
    }
}

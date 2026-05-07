using System.Buffers;
using System.Threading.Channels;
using SmbEnterprise.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace SmbEnterprise.Cache;

/// <summary>
/// Read-ahead prefetcher: pre-loads the next N chunks in background while consumer reads current chunk.
/// Uses bounded channel for backpressure.
/// </summary>
public sealed class ReadAheadPrefetcher : IAsyncDisposable
{
    private readonly IRemoteReadStream _source;
    private readonly Channel<PrefetchedChunk> _channel;
    private readonly ReadAheadOptions _options;
    private readonly ILogger _logger;
    private readonly Task _prefetchTask;
    private readonly CancellationTokenSource _cts = new();

    public ReadAheadPrefetcher(
        IRemoteReadStream source,
        long fileSize,
        ReadAheadOptions options,
        ILogger logger)
    {
        _source = source;
        _options = options;
        _logger = logger;
        _channel = Channel.CreateBounded<PrefetchedChunk>(new BoundedChannelOptions(options.PrefetchDepth)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });
        _prefetchTask = PrefetchLoopAsync(fileSize, _cts.Token);
    }

    public ChannelReader<PrefetchedChunk> Reader => _channel.Reader;

    private async Task PrefetchLoopAsync(long fileSize, CancellationToken cancellationToken)
    {
        long offset = 0;
        try
        {
            while (offset < fileSize && !cancellationToken.IsCancellationRequested)
            {
                var remaining = fileSize - offset;
                var chunkSize = (int)Math.Min(_options.ChunkSize, remaining);
                var buffer = ArrayPool<byte>.Shared.Rent(chunkSize);
                var memory = buffer.AsMemory(0, chunkSize);

                await _source.SeekAsync(offset, cancellationToken).ConfigureAwait(false);
                var bytesRead = await _source.ReadAsync(memory, cancellationToken).ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    break;
                }

                var chunk = new PrefetchedChunk(buffer, bytesRead, offset);
                await _channel.Writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
                offset += bytesRead;
            }
        }
        catch (OperationCanceledException) { /* expected */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Prefetch error at offset {Offset}", offset);
        }
        finally
        {
            _channel.Writer.TryComplete();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _prefetchTask.ConfigureAwait(false); } catch { /* expected */ }
        _cts.Dispose();
    }
}

public sealed class PrefetchedChunk : IDisposable
{
    public byte[] Buffer { get; }
    public int BytesRead { get; }
    public long Offset { get; }
    public Memory<byte> Data => Buffer.AsMemory(0, BytesRead);
    private bool _returned;

    public PrefetchedChunk(byte[] buffer, int bytesRead, long offset)
    {
        Buffer = buffer;
        BytesRead = bytesRead;
        Offset = offset;
    }

    public void Dispose()
    {
        if (_returned) return;
        _returned = true;
        ArrayPool<byte>.Shared.Return(Buffer);
    }
}

public sealed class ReadAheadOptions
{
    public int PrefetchDepth { get; init; } = 4;
    public int ChunkSize { get; init; } = 1 * 1024 * 1024; // 1 MB
}

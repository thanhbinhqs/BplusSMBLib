using System.Buffers;
using System.Threading.Channels;
using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;
using Microsoft.Extensions.Logging;

namespace SmbEnterprise.Transfer.Pipeline;

/// <summary>
/// Represents a read chunk ready to be dispatched to writer workers.
/// </summary>
internal sealed class ReadChunk
{
    public required byte[] Buffer { get; init; }  // rented from ArrayPool
    public int BytesRead { get; init; }
    public long Offset { get; init; }
    public int ChunkIndex { get; init; }
    public bool IsLast { get; init; }
}
/// <summary>
/// Adaptive chunk-size transfer pipeline using System.Threading.Channels.
///
/// Architecture:
///   Reader Worker → Chunk Channel → Writer Workers → Verify Workers → Progress Tracker
/// </summary>
internal sealed class TransferPipeline : IAsyncDisposable
{
    private readonly IRemoteFileSystem _source;
    private readonly IReadOnlyList<IRemoteFileSystem> _destinations;
    private readonly TransferOptions _options;
    private readonly ILogger _logger;
    private readonly AdaptiveChunkSizer _chunkSizer;

    public TransferPipeline(
        IRemoteFileSystem source,
        IReadOnlyList<IRemoteFileSystem> destinations,
        TransferOptions options,
        ILogger logger)
    {
        _source = source;
        _destinations = destinations;
        _options = options;
        _logger = logger;
        _chunkSizer = new AdaptiveChunkSizer(options);
    }

    /// <summary>
    /// Execute the full pipeline: read from source, fan-out write to all destinations.
    /// </summary>
    public async Task<PipelineResult> ExecuteAsync(
        string sourcePath,
        IReadOnlyList<string> destinationPaths,
        long sourceSize,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        var opId = Guid.NewGuid().ToString("N")[..8];
        var sessionId = Guid.NewGuid();
        var transferProgress = new TransferProgress
        {
            SessionId = sessionId,
            SourcePath = sourcePath,
            DestinationPath = destinationPaths.Count == 1 ? destinationPaths[0] : "[multi]",
            TotalBytes = sourceSize,
            Stage = TransferStage.Reading
        };

        var startTime = DateTime.UtcNow;
        _logger.LogInformation("[{OpId}] Pipeline start source={Source} size={Size} destinations={Count} workers={Workers}",
            opId, sourcePath, sourceSize, destinationPaths.Count, _options.MaxParallelWorkers);

        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();

        // Start writer tasks (one per destination, all consuming from the same channel)
        // But channels are single-consumer for ordering, so we broadcast instead.
        var destChannels = destinationPaths
            .Select(_ => Channel.CreateBounded<ReadChunk>(new BoundedChannelOptions(_options.WriteQueueDepth)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true
            }))
            .ToList();

        var writerTasks = destinationPaths
            .Select((dest, i) => WriteDestinationAsync(
                destChannels[i].Reader, dest, errors, cancellationToken, opId, i))
            .ToList();

        // Reader worker: read source and broadcast to all destination channels
        try
        {
            await ReadAndBroadcastAsync(sourcePath, sourceSize, destChannels, transferProgress,
                progress, cancellationToken, opId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            errors.Add($"Read error: {ex.Message}");
            // Complete all dest channels with error
            foreach (var ch in destChannels)
                ch.Writer.TryComplete(ex);
        }
        finally
        {
            // Ensure all channels are completed
            foreach (var ch in destChannels)
                ch.Writer.TryComplete();
        }

        await Task.WhenAll(writerTasks).ConfigureAwait(false);

        var elapsed = DateTime.UtcNow - startTime;
        transferProgress.Stage = errors.IsEmpty ? TransferStage.Completed : TransferStage.Failed;
        transferProgress.Elapsed = elapsed;

        return new PipelineResult
        {
            Success = errors.IsEmpty,
            Errors = errors.ToList(),
            BytesTransferred = transferProgress.TransferredBytes,
            Duration = elapsed
        };
    }

    private async Task ReadAndBroadcastAsync(
        string sourcePath,
        long sourceSize,
        List<Channel<ReadChunk>> destChannels,
        TransferProgress transferProgress,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken,
        string opId)
    {
        IRemoteReadStream? readStream = null;
        readStream = await _source.OpenReadAsync(sourcePath, 0, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("[{OpId}] Reader opened source stream path={Path}", opId, sourcePath);

        int chunkIndex = 0;
        long offset = 0;
        var speedTracker = new SpeedTracker();
        var readStartedUtc = DateTime.UtcNow;

        try
        {
            while (offset < sourceSize)
            {
                var chunkSize = _chunkSizer.CurrentChunkSize;
                var remaining = sourceSize - offset;
                var toRead = (int)Math.Min(chunkSize, remaining);
                var isLast = (offset + toRead) >= sourceSize;

                var buffer = ArrayPool<byte>.Shared.Rent(toRead);

                try
                {
                    var readStart = DateTime.UtcNow;
                    var totalRead = 0;
                    var reachedEof = false;

                    while (totalRead < toRead)
                    {
                        var attempt = 0;

                        while (true)
                        {
                            try
                            {
                                var segment = buffer.AsMemory(totalRead, toRead - totalRead);
                                var bytesRead = await readStream.ReadAsync(segment, cancellationToken).ConfigureAwait(false);
                                if (bytesRead == 0)
                                {
                                    reachedEof = true;
                                    break;
                                }

                                totalRead += bytesRead;
                                break;
                            }
                            catch (Exception ex) when (IsTransientReadFailure(ex) && attempt < _options.MaxChunkRetries)
                            {
                                attempt++;
                                var absoluteOffset = offset + totalRead;
                                var delayMs = 150 * attempt;

                                _logger.LogWarning(ex,
                                    "[{OpId}] Transient read failure offset={Offset} chunkIndex={Chunk} attempt={Attempt}/{Max}; reopening stream",
                                    opId, absoluteOffset, chunkIndex, attempt, _options.MaxChunkRetries);

                                await readStream.DisposeAsync().ConfigureAwait(false);
                                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                                readStream = await _source.OpenReadAsync(sourcePath, absoluteOffset, cancellationToken).ConfigureAwait(false);
                            }
                        }

                        if (totalRead == 0)
                        {
                            break;
                        }

                        if (reachedEof)
                        {
                            break;
                        }

                        if (totalRead >= toRead)
                        {
                            break;
                        }
                    }

                    if (totalRead <= 0)
                    {
                        throw new IOException($"Unexpected EOF while reading source at offset {offset} (sourceSize={sourceSize})");
                    }

                    var readDuration = DateTime.UtcNow - readStart;
                    _chunkSizer.RecordMetrics(totalRead, readDuration);

                    var chunk = new ReadChunk
                    {
                        Buffer = buffer,
                        BytesRead = totalRead,
                        Offset = offset,
                        ChunkIndex = chunkIndex,
                        IsLast = isLast
                    };

                    // Broadcast the same chunk to all destination channels
                    // For multi-destination: each destination gets its own copy
                    for (int i = 0; i < destChannels.Count; i++)
                    {
                        if (i < destChannels.Count - 1)
                        {
                            // Copy buffer for all but the last destination
                            var copyBuffer = ArrayPool<byte>.Shared.Rent(totalRead);
                            buffer.AsSpan(0, totalRead).CopyTo(copyBuffer);
                            await destChannels[i].Writer.WriteAsync(new ReadChunk
                            {
                                Buffer = copyBuffer,
                                BytesRead = chunk.BytesRead,
                                Offset = chunk.Offset,
                                ChunkIndex = chunk.ChunkIndex,
                                IsLast = chunk.IsLast
                            }, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            await destChannels[i].Writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    offset += totalRead;
                    chunkIndex++;
                    transferProgress.TransferredBytes = offset;
                    speedTracker.Record(totalRead);
                    transferProgress.SpeedBytesPerSecond = speedTracker.BytesPerSecond;
                    transferProgress.Elapsed = DateTime.UtcNow - readStartedUtc;
                    transferProgress.Eta = speedTracker.EstimateEta(sourceSize - offset);

                    progress?.Report(transferProgress);

                    if (chunkIndex % 64 == 0 || isLast)
                    {
                        _logger.LogDebug("[{OpId}] Read chunkIndex={ChunkIndex} bytes={Bytes} offset={Offset}/{Total} speed={Speed:F2}MBps",
                            opId, chunkIndex, totalRead, offset, sourceSize, transferProgress.SpeedBytesPerSecond / 1024d / 1024d);
                    }
                }

                catch
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    throw;
                }
            }

            _logger.LogDebug("[{OpId}] Reader completed source={Source} totalBytes={Total}", opId, sourcePath, offset);
        }
        finally
        {
            if (readStream is not null)
            {
                await readStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransientReadFailure(Exception ex)
    {
        if (ex.Message.Contains("Not enough credits", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (ex.Message.Contains("credit starvation", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (ex is IOException ioEx)
        {
            var m = ioEx.Message;
            if (m.Contains("STATUS_IO_TIMEOUT", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("STATUS_CONNECTION", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("STATUS_NETWORK", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return ex is InvalidOperationException inv &&
               inv.Message.Contains("no longer connected", StringComparison.OrdinalIgnoreCase);
    }

    private async Task WriteDestinationAsync(
        ChannelReader<ReadChunk> reader,
        string destinationPath,
        System.Collections.Concurrent.ConcurrentBag<string> errors,
        CancellationToken cancellationToken,
        string opId,
        int destinationIndex)
    {
        var safeIndex = Math.Clamp(destinationIndex, 0, _destinations.Count - 1);
        var destFs = _destinations[safeIndex];
        IRemoteWriteStream? writeStream = null;
        long writtenBytes = 0;
        int chunkCount = 0;

        try
        {
            _logger.LogDebug("[{OpId}] Writer[{Index}] opening destination stream path={Dest}", opId, destinationIndex, destinationPath);
            writeStream = await destFs.OpenWriteAsync(destinationPath, 0, true, cancellationToken).ConfigureAwait(false);

            await foreach (var chunk in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await writeStream.WriteAsync(chunk.Buffer.AsMemory(0, chunk.BytesRead), cancellationToken).ConfigureAwait(false);
                    writtenBytes += chunk.BytesRead;
                    chunkCount++;

                    if (chunkCount % 64 == 0 || chunk.IsLast)
                    {
                        _logger.LogDebug("[{OpId}] Writer[{Index}] chunkIndex={ChunkIndex} bytes={Bytes} totalWritten={Total}",
                            opId, destinationIndex, chunk.ChunkIndex, chunk.BytesRead, writtenBytes);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(chunk.Buffer);
                }
            }

            await writeStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("[{OpId}] Writer[{Index}] completed destination={Dest} bytes={Bytes}",
                opId, destinationIndex, destinationPath, writtenBytes);
        }
        catch (Exception ex)
        {
            errors.Add($"Write error to {destinationPath}: {ex.Message}");
            _logger.LogError(ex, "[{OpId}] Write failed for destination[{Index}] {Dest}", opId, destinationIndex, destinationPath);
        }
        finally
        {
            if (writeStream is not null)
                await writeStream.DisposeAsync().ConfigureAwait(false);

            _logger.LogDebug("[{OpId}] Writer[{Index}] disposed destination stream path={Dest}", opId, destinationIndex, destinationPath);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class PipelineResult
{
    public bool Success { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public long BytesTransferred { get; init; }
    public TimeSpan Duration { get; init; }
}

using System.Buffers;
using System.Collections.Concurrent;
using System.Threading.Channels;
using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;
using Microsoft.Extensions.Logging;

namespace SmbEnterprise.Transfer.Pipeline;

/// <summary>
/// Pipeline nâng cao cho multi-destination transfer với progress tracking riêng cho từng destination
/// và xử lý slow connection.
/// </summary>
internal sealed class MultiDestinationTransferPipeline : IAsyncDisposable
{
    private readonly IRemoteFileSystem _source;
    private readonly IReadOnlyList<IRemoteFileSystem> _destinations;
    private readonly TransferOptions _options;
    private readonly SlowConnectionPolicy _slowPolicy;
    private readonly ILogger _logger;
    private readonly AdaptiveChunkSizer _chunkSizer;
    private readonly ConcurrentDictionary<int, DestinationWriterState> _writerStates = new();

    public MultiDestinationTransferPipeline(
        IRemoteFileSystem source,
        IReadOnlyList<IRemoteFileSystem> destinations,
        TransferOptions options,
        SlowConnectionPolicy slowPolicy,
        ILogger logger)
    {
        _source = source;
        _destinations = destinations;
        _options = options;
        _slowPolicy = slowPolicy;
        _logger = logger;
        _chunkSizer = new AdaptiveChunkSizer(options);
    }

    public async Task<PipelineResult> ExecuteAsync(
        string sourcePath,
        IReadOnlyList<string> destinationPaths,
        long sourceSize,
        IProgress<AggregatedMultiDestinationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        var opId = sessionId.ToString("N")[..8];
        var startTime = DateTime.UtcNow;

        _logger.LogInformation("[{OpId}] Multi-destination pipeline start: source={Source} size={Size:N0} destinations={Count}",
            opId, sourcePath, sourceSize, destinationPaths.Count);

        // Initialize progress tracking cho từng destination
        var destProgresses = destinationPaths.Select((dest, idx) => new MultiDestinationProgress
        {
            SessionId = sessionId,
            SourcePath = sourcePath,
            DestinationPath = dest,
            DestinationIndex = idx,
            TotalBytes = sourceSize,
            StartedUtc = DateTime.UtcNow
        }).ToList();

        var aggregatedProgress = new AggregatedMultiDestinationProgress
        {
            SessionId = sessionId,
            SourcePath = sourcePath,
            TotalBytes = sourceSize,
            DestinationProgresses = destProgresses
        };

        var errors = new ConcurrentBag<string>();
        var slowConnectionStats = new SlowConnectionStats
        {
            TotalDestinations = destinationPaths.Count
        };

        // Tạo channel riêng cho từng destination
        var destChannels = destinationPaths
            .Select((_, idx) => (
                Index: idx,
                Channel: Channel.CreateBounded<ReadChunk>(new BoundedChannelOptions(_options.WriteQueueDepth)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true
                })
            ))
            .ToList();

        // Khởi tạo writer state cho từng destination
        foreach (var (index, _) in destChannels)
        {
            _writerStates[index] = new DestinationWriterState
            {
                Index = index,
                DestinationPath = destinationPaths[index],
                Progress = destProgresses[index]
            };
        }

        // Start writer tasks
        var writerTasks = destChannels
            .Select(dc => WriteDestinationWithProgressAsync(
                dc.Channel.Reader,
                destinationPaths[dc.Index],
                dc.Index,
                destProgresses[dc.Index],
                errors,
                cancellationToken,
                opId))
            .ToList();

        // Start slow connection monitor task
        var monitorTask = MonitorSlowConnectionsAsync(
            aggregatedProgress,
            slowConnectionStats,
            cancellationToken);

        // Reader worker: read source và broadcast tới tất cả destination channels
        var readerTask = ReadAndBroadcastWithSlowHandlingAsync(
            sourcePath,
            sourceSize,
            destChannels,
            aggregatedProgress,
            progress,
            errors,
            cancellationToken,
            opId);

        try
        {
            await readerTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            errors.Add($"Read error: {ex.Message}");
            _logger.LogError(ex, "[{OpId}] Reader failed", opId);

            foreach (var dc in destChannels)
                dc.Channel.Writer.TryComplete(ex);
        }
        finally
        {
            foreach (var dc in destChannels)
                dc.Channel.Writer.TryComplete();
        }

        await Task.WhenAll(writerTasks).ConfigureAwait(false);

        var elapsed = DateTime.UtcNow - startTime;
        aggregatedProgress.Elapsed = elapsed;

        // Update final states
        foreach (var destProgress in destProgresses)
        {
            if (destProgress.Stage != TransferStage.Completed && destProgress.Stage != TransferStage.Failed)
            {
                destProgress.Stage = errors.IsEmpty ? TransferStage.Completed : TransferStage.Failed;
                destProgress.CompletedUtc = DateTime.UtcNow;
            }
        }

        // Report final progress
        progress?.Report(aggregatedProgress);

        _logger.LogInformation(
            "[{OpId}] Multi-destination pipeline complete: completed={Completed}/{Total} throttled={Throttled} failed={Failed} duration={Duration:g}",
            opId, aggregatedProgress.CompletedCount, destinationPaths.Count,
            aggregatedProgress.ThrottledCount, aggregatedProgress.FailedCount, elapsed);

        return new PipelineResult
        {
            Success = errors.IsEmpty || (_slowPolicy.ContinueOnSlowConnectionFailure && aggregatedProgress.CompletedCount > 0),
            Errors = errors.ToList(),
            BytesTransferred = aggregatedProgress.SourceBytesRead,
            Duration = elapsed
        };
    }

    private async Task ReadAndBroadcastWithSlowHandlingAsync(
        string sourcePath,
        long sourceSize,
        List<(int Index, Channel<ReadChunk> Channel)> destChannels,
        AggregatedMultiDestinationProgress aggregatedProgress,
        IProgress<AggregatedMultiDestinationProgress>? progress,
        ConcurrentBag<string> errors,
        CancellationToken cancellationToken,
        string opId)
    {
        IRemoteReadStream? readStream = null;

        try
        {
            readStream = await _source.OpenReadAsync(sourcePath, 0, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("[{OpId}] Reader opened source stream", opId);

            int chunkIndex = 0;
            long offset = 0;
            var speedTracker = new MultiDestSpeedTracker();
            var lastProgressReport = DateTime.UtcNow;

            while (offset < sourceSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chunkSize = _chunkSizer.CurrentChunkSize;
                var remaining = sourceSize - offset;
                var toRead = (int)Math.Min(chunkSize, remaining);
                var isLast = (offset + toRead) >= sourceSize;

                var buffer = ArrayPool<byte>.Shared.Rent(toRead);
                var readStartTime = DateTime.UtcNow;

                try
                {
                    var readStart = DateTime.UtcNow;
                    var segment = buffer.AsMemory(0, toRead);
                    var bytesRead = await readStream.ReadAsync(segment, cancellationToken).ConfigureAwait(false);

                    if (bytesRead == 0)
                        break;

                    var readDuration = DateTime.UtcNow - readStart;
                    speedTracker.RecordChunk(bytesRead, readDuration);
                    _chunkSizer.RecordMetrics(bytesRead, readDuration);

                    var chunk = new ReadChunk
                    {
                        Buffer = buffer,
                        BytesRead = bytesRead,
                        Offset = offset,
                        ChunkIndex = chunkIndex++,
                        IsLast = isLast
                    };

                    // Broadcast chunk đến các destination channels (chỉ những channel đang active)
                    var activeChannels = destChannels
                        .Where(dc => !_writerStates[dc.Index].IsThrottled || 
                                     _slowPolicy.Action != SlowConnectionAction.Pause)
                        .ToList();

                    foreach (var (index, channel) in activeChannels)
                    {
                        // Clone buffer cho mỗi destination (trừ destination cuối cùng dùng buffer gốc)
                        var destBuffer = index == activeChannels.Last().Index 
                            ? buffer 
                            : CloneBuffer(buffer, bytesRead);

                        var destChunk = new ReadChunk
                        {
                            Buffer = destBuffer,
                            BytesRead = bytesRead,
                            Offset = offset,
                            ChunkIndex = chunk.ChunkIndex,
                            IsLast = isLast
                        };

                        await channel.Writer.WriteAsync(destChunk, cancellationToken).ConfigureAwait(false);
                    }

                    offset += bytesRead;
                    aggregatedProgress.SourceBytesRead = offset;
                    aggregatedProgress.SourceReadSpeedBytesPerSecond = speedTracker.CurrentSpeedBps;

                    // Report progress định kỳ
                    if ((DateTime.UtcNow - lastProgressReport).TotalMilliseconds >= 500)
                    {
                        aggregatedProgress.Elapsed = DateTime.UtcNow - aggregatedProgress.DestinationProgresses[0].StartedUtc;
                        progress?.Report(aggregatedProgress);
                        lastProgressReport = DateTime.UtcNow;
                    }

                    if (isLast)
                        break;
                }
                catch
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    throw;
                }
            }

            _logger.LogInformation("[{OpId}] Reader complete: read {Bytes:N0} bytes in {Chunks} chunks",
                opId, offset, chunkIndex);
        }
        finally
        {
            if (readStream != null)
                await readStream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task WriteDestinationWithProgressAsync(
        ChannelReader<ReadChunk> reader,
        string destinationPath,
        int destinationIndex,
        MultiDestinationProgress destProgress,
        ConcurrentBag<string> errors,
        CancellationToken cancellationToken,
        string opId)
    {
        IRemoteWriteStream? writeStream = null;
        var state = _writerStates[destinationIndex];
        var speedTracker = new MultiDestSpeedTracker();

        try
        {
            destProgress.Stage = TransferStage.Connecting;
            writeStream = await _destinations[destinationIndex].OpenWriteAsync(
                destinationPath, 0, !_options.Overwrite, cancellationToken).ConfigureAwait(false);

            destProgress.Stage = TransferStage.Writing;
            _logger.LogDebug("[{OpId}] Writer[{Index}] opened destination stream: {Dest}",
                opId, destinationIndex, destinationPath);

            long writtenBytes = 0;

            await foreach (var chunk in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (state.IsThrottled && _slowPolicy.Action == SlowConnectionAction.Pause)
                {
                    _logger.LogDebug("[{OpId}] Writer[{Index}] paused due to slow connection", opId, destinationIndex);
                    ArrayPool<byte>.Shared.Return(chunk.Buffer);
                    continue;
                }

                var writeStartTime = DateTime.UtcNow;

                // Throttle nếu cần
                if (state.IsThrottled && _slowPolicy.ThrottleMaxBytesPerSecond > 0)
                {
                    var expectedDuration = TimeSpan.FromSeconds(
                        (double)chunk.BytesRead / _slowPolicy.ThrottleMaxBytesPerSecond);

                    await Task.Delay(expectedDuration, cancellationToken).ConfigureAwait(false);
                }

                var writeSegment = chunk.Buffer.AsMemory(0, chunk.BytesRead);
                await writeStream.WriteAsync(writeSegment, cancellationToken).ConfigureAwait(false);
                writtenBytes += chunk.BytesRead;

                var writeDuration = DateTime.UtcNow - writeStartTime;
                speedTracker.RecordChunk(chunk.BytesRead, writeDuration);

                // Update progress
                destProgress.TransferredBytes = writtenBytes;
                destProgress.SpeedBytesPerSecond = speedTracker.CurrentSpeedBps;
                destProgress.Elapsed = DateTime.UtcNow - destProgress.StartedUtc;

                if (speedTracker.CurrentSpeedBps > 0)
                {
                    var remaining = destProgress.TotalBytes - writtenBytes;
                    destProgress.Eta = TimeSpan.FromSeconds(remaining / speedTracker.CurrentSpeedBps);
                }

                state.LastChunkTime = DateTime.UtcNow;
                state.CurrentSpeed = speedTracker.CurrentSpeedBps;

                ArrayPool<byte>.Shared.Return(chunk.Buffer);

                if (chunk.IsLast)
                    break;
            }

            await writeStream.FlushAsync(cancellationToken).ConfigureAwait(false);

            destProgress.Stage = TransferStage.Completed;
            destProgress.CompletedUtc = DateTime.UtcNow;

            _logger.LogInformation("[{OpId}] Writer[{Index}] completed: {Dest} - {Bytes:N0} bytes @ {Speed:N2} MB/s",
                opId, destinationIndex, destinationPath, writtenBytes, speedTracker.CurrentSpeedBps / (1024.0 * 1024.0));
        }
        catch (Exception ex)
        {
            var errorMsg = $"Write error to destination[{destinationIndex}] {destinationPath}: {ex.Message}";
            errors.Add(errorMsg);
            destProgress.Stage = TransferStage.Failed;
            destProgress.ErrorMessage = ex.Message;
            destProgress.CompletedUtc = DateTime.UtcNow;

            _logger.LogError(ex, "[{OpId}] Writer[{Index}] failed: {Dest}", opId, destinationIndex, destinationPath);
        }
        finally
        {
            if (writeStream != null)
                await writeStream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task MonitorSlowConnectionsAsync(
        AggregatedMultiDestinationProgress aggregatedProgress,
        SlowConnectionStats stats,
        CancellationToken cancellationToken)
    {
        if (!_slowPolicy.EnableSlowConnectionHandling)
            return;

        var monitorInterval = TimeSpan.FromSeconds(2);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(monitorInterval, cancellationToken).ConfigureAwait(false);

                var elapsed = DateTime.UtcNow - aggregatedProgress.DestinationProgresses[0].StartedUtc;

                // Chỉ bắt đầu đánh giá sau một khoảng thời gian nhất định
                if (elapsed.TotalSeconds < _slowPolicy.MinimumEvaluationDurationSeconds)
                    continue;

                var activeWriters = _writerStates.Values
                    .Where(s => s.CurrentSpeed > 0 && s.Progress.Stage == TransferStage.Writing)
                    .ToList();

                if (activeWriters.Count < 2)
                    continue; // Cần ít nhất 2 writers để so sánh

                var avgSpeed = activeWriters.Average(w => w.CurrentSpeed);
                var slowThreshold = avgSpeed * (_slowPolicy.SlowSpeedThresholdPercent / 100.0);

                stats.AverageSpeed = avgSpeed;
                stats.FastestSpeed = activeWriters.Max(w => w.CurrentSpeed);
                stats.SlowestSpeed = activeWriters.Min(w => w.CurrentSpeed);

                foreach (var writer in activeWriters)
                {
                    stats.DestinationSpeeds[writer.DestinationPath] = writer.CurrentSpeed;

                    if (writer.CurrentSpeed < slowThreshold && !writer.IsThrottled)
                    {
                        writer.IsThrottled = true;
                        writer.Progress.IsThrottled = true;
                        stats.SlowDestinationCount++;

                        _logger.LogWarning(
                            "Slow connection detected: destination[{Index}] {Path} - speed {Speed:N2} MB/s < threshold {Threshold:N2} MB/s (avg: {Avg:N2} MB/s)",
                            writer.Index,
                            writer.DestinationPath,
                            writer.CurrentSpeed / (1024.0 * 1024.0),
                            slowThreshold / (1024.0 * 1024.0),
                            avgSpeed / (1024.0 * 1024.0));

                        // Thực hiện action theo policy
                        switch (_slowPolicy.Action)
                        {
                            case SlowConnectionAction.Throttle:
                                stats.ThrottledCount++;
                                _logger.LogInformation("Throttling destination[{Index}] to {MaxSpeed:N2} MB/s",
                                    writer.Index, _slowPolicy.ThrottleMaxBytesPerSecond / (1024.0 * 1024.0));
                                break;

                            case SlowConnectionAction.Pause:
                                _logger.LogInformation("Pausing destination[{Index}]", writer.Index);
                                break;

                            case SlowConnectionAction.Skip:
                                stats.SkippedCount++;
                                writer.Progress.Stage = TransferStage.Failed;
                                writer.Progress.ErrorMessage = "Skipped due to slow connection";
                                _logger.LogInformation("Skipping destination[{Index}]", writer.Index);
                                break;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
    }

    private static byte[] CloneBuffer(byte[] source, int length)
    {
        var clone = ArrayPool<byte>.Shared.Rent(length);
        Array.Copy(source, clone, length);
        return clone;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class DestinationWriterState
{
    public int Index { get; init; }
    public required string DestinationPath { get; init; }
    public required MultiDestinationProgress Progress { get; init; }
    public double CurrentSpeed { get; set; }
    public DateTime LastChunkTime { get; set; } = DateTime.UtcNow;
    public bool IsThrottled { get; set; }
}

internal sealed class MultiDestSpeedTracker
{
    private readonly Queue<(long Bytes, DateTime Time)> _samples = new();
    private const int MaxSamples = 10;

    public double CurrentSpeedBps { get; private set; }

    public void RecordChunk(int bytes, TimeSpan duration)
    {
        var now = DateTime.UtcNow;
        _samples.Enqueue((bytes, now));

        while (_samples.Count > MaxSamples)
            _samples.Dequeue();

        if (_samples.Count >= 2)
        {
            var first = _samples.First();
            var last = _samples.Last();
            var totalBytes = _samples.Sum(s => s.Bytes);
            var totalTime = (last.Time - first.Time).TotalSeconds;

            if (totalTime > 0)
                CurrentSpeedBps = totalBytes / totalTime;
        }
    }
}

using SmbEnterprise.Core.Models;

namespace SmbEnterprise.Transfer.Pipeline;

/// <summary>
/// Dynamically adjusts chunk size based on observed throughput and latency.
/// Increases chunk size when throughput is high; decreases on poor performance.
/// </summary>
public sealed class AdaptiveChunkSizer
{
    private readonly TransferOptions _options;
    private int _currentChunkSize;
    private readonly object _lock = new();

    // Moving window metrics
    private double _avgThroughput = 0;
    private int _sampleCount = 0;
    private int _consecutiveSlowSamples = 0;
    private const int SlowThresholdMBs = 5; // MB/s
    private const int FastThresholdMBs = 50; // MB/s

    public int CurrentChunkSize
    {
        get { lock (_lock) return _currentChunkSize; }
    }

    public AdaptiveChunkSizer(TransferOptions options)
    {
        _options = options;
        _currentChunkSize = options.ChunkSize;
    }

    /// <summary>Convenience constructor for testing without full TransferOptions.</summary>
    public AdaptiveChunkSizer(int initialChunkSize, int minChunkSize, int maxChunkSize)
    {
        _options = new TransferOptions
        {
            ChunkSize = initialChunkSize,
            MinChunkSize = minChunkSize,
            MaxChunkSize = maxChunkSize
        };
        _currentChunkSize = initialChunkSize;
    }

    public void RecordMetrics(int bytesTransferred, TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds <= 0) return;

        var throughputMBs = (bytesTransferred / elapsed.TotalSeconds) / (1024 * 1024);

        lock (_lock)
        {
            _sampleCount++;
            // Exponential moving average
            _avgThroughput = _sampleCount == 1
                ? throughputMBs
                : (_avgThroughput * 0.8) + (throughputMBs * 0.2);

            if (_avgThroughput >= FastThresholdMBs)
            {
                _consecutiveSlowSamples = 0;
                // Increase chunk size gradually
                var newSize = (int)Math.Min(_currentChunkSize * 1.5, _options.MaxChunkSize);
                _currentChunkSize = RoundToBlockSize(newSize);
            }
            else if (_avgThroughput < SlowThresholdMBs)
            {
                _consecutiveSlowSamples++;
                if (_consecutiveSlowSamples >= 3)
                {
                    // Decrease chunk size
                    var newSize = (int)Math.Max(_currentChunkSize * 0.75, _options.MinChunkSize);
                    _currentChunkSize = RoundToBlockSize(newSize);
                    _consecutiveSlowSamples = 0;
                }
            }
        }
    }

    private static int RoundToBlockSize(int size)
    {
        const int block = 64 * 1024; // 64KB alignment
        return Math.Max(block, (size / block) * block);
    }
}

/// <summary>Computes moving average transfer speed and ETA.</summary>
internal sealed class SpeedTracker
{
    private readonly Queue<(DateTime Time, long Bytes)> _samples = new();
    private const int WindowSeconds = 5;
    private long _totalBytes;

    public void Record(long bytes)
    {
        var now = DateTime.UtcNow;
        _samples.Enqueue((now, bytes));
        _totalBytes += bytes;

        // Remove samples older than window
        var cutoff = now.AddSeconds(-WindowSeconds);
        while (_samples.Count > 0 && _samples.Peek().Time < cutoff)
        {
            _totalBytes -= _samples.Dequeue().Bytes;
        }
    }

    public double BytesPerSecond
    {
        get
        {
            if (_samples.Count < 2) return 0;
            var span = (DateTime.UtcNow - _samples.Peek().Time).TotalSeconds;
            return span > 0 ? _totalBytes / span : 0;
        }
    }

    public TimeSpan? EstimateEta(long remainingBytes)
    {
        var speed = BytesPerSecond;
        if (speed <= 0) return null;
        return TimeSpan.FromSeconds(remainingBytes / speed);
    }
}

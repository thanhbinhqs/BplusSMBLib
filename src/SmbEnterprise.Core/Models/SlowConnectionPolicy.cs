namespace SmbEnterprise.Core.Models;

/// <summary>
/// Cấu hình để xử lý các destination có tốc độ chậm trong multi-destination transfer.
/// </summary>
public sealed class SlowConnectionPolicy
{
    /// <summary>
    /// Bật/tắt cơ chế xử lý connection chậm.
    /// Default: true
    /// </summary>
    public bool EnableSlowConnectionHandling { get; set; } = true;

    /// <summary>
    /// Ngưỡng tốc độ tối thiểu so với tốc độ trung bình (%).
    /// Nếu một destination chậm hơn ngưỡng này, nó sẽ bị coi là "slow".
    /// Default: 30% (nghĩa là nếu chậm hơn 70% so với trung bình thì bị coi là slow)
    /// </summary>
    public double SlowSpeedThresholdPercent { get; set; } = 30.0;

    /// <summary>
    /// Thời gian tối thiểu để đánh giá một connection là chậm (seconds).
    /// Tránh đánh giá sai do spike ngắn hạn.
    /// Default: 10 seconds
    /// </summary>
    public int MinimumEvaluationDurationSeconds { get; set; } = 10;

    /// <summary>
    /// Hành động khi phát hiện slow connection.
    /// </summary>
    public SlowConnectionAction Action { get; set; } = SlowConnectionAction.Throttle;

    /// <summary>
    /// Giới hạn tốc độ tối đa cho slow connection khi action = Throttle (bytes/sec).
    /// 0 = unlimited (nhưng vẫn đánh dấu là slow để theo dõi).
    /// Default: 5 MB/s
    /// </summary>
    public long ThrottleMaxBytesPerSecond { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Số lần retry tối đa cho một destination bị fail do slow connection.
    /// Default: 2
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// Có tiếp tục transfer các destination khác nếu một destination bị skip/fail không?
    /// Default: true (tiếp tục các destination còn lại)
    /// </summary>
    public bool ContinueOnSlowConnectionFailure { get; set; } = true;
}

/// <summary>
/// Hành động khi phát hiện slow connection.
/// </summary>
public enum SlowConnectionAction
{
    /// <summary>
    /// Không làm gì, chỉ log warning.
    /// </summary>
    LogOnly,

    /// <summary>
    /// Giới hạn tốc độ của connection chậm để không ảnh hưởng nguồn đọc.
    /// </summary>
    Throttle,

    /// <summary>
    /// Pause destination chậm, tiếp tục các destination khác.
    /// </summary>
    Pause,

    /// <summary>
    /// Skip destination chậm hoàn toàn, đánh dấu là failed.
    /// </summary>
    Skip,

    /// <summary>
    /// Retry connection với timeout ngắn hơn.
    /// </summary>
    Retry
}

/// <summary>
/// Thống kê về slow connection trong một session transfer.
/// </summary>
public sealed class SlowConnectionStats
{
    public int TotalDestinations { get; set; }
    public int SlowDestinationCount { get; set; }
    public int ThrottledCount { get; set; }
    public int SkippedCount { get; set; }
    public int RetryCount { get; set; }
    public Dictionary<string, double> DestinationSpeeds { get; set; } = new();
    public double AverageSpeed { get; set; }
    public double SlowestSpeed { get; set; }
    public double FastestSpeed { get; set; }
}

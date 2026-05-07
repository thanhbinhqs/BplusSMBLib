using SmbEnterprise.Core.Models;

namespace SmbEnterprise.Diagnostics;

/// <summary>
/// Renders live transfer progress as a formatted console dashboard.
/// </summary>
public sealed class TransferDashboard
{
    private readonly object _lock = new();

    public void Render(TransferProgress progress)
    {
        lock (_lock)
        {
            var percent = progress.PercentComplete;
            var bar = BuildProgressBar(percent, 40);
            var speed = FormatSpeed(progress.SpeedBytesPerSecond);
            var eta = progress.Eta.HasValue
                ? FormatDuration(progress.Eta.Value)
                : "N/A";

            Console.Write("\r");
            Console.Write($"[{progress.Stage,-10}] {bar} {percent,5:F1}% | {speed,12} | ETA: {eta,8} | Retries: {progress.RetryCount} | {FormatBytes(progress.TransferredBytes)}/{FormatBytes(progress.TotalBytes)}");
        }
    }

    public void RenderSummary(TransferResult result)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 80));
        Console.WriteLine($"Transfer {(result.Success ? "SUCCEEDED" : "FAILED")}");
        Console.WriteLine($"  Bytes transferred : {FormatBytes(result.BytesTransferred)}");
        Console.WriteLine($"  Duration          : {FormatDuration(result.Duration)}");
        Console.WriteLine($"  Avg speed         : {FormatSpeed(result.BytesTransferred / Math.Max(1, result.Duration.TotalSeconds))}");
        Console.WriteLine($"  Checksum verified : {result.ChecksumVerified}");
        if (!result.Success && result.ErrorMessage != null)
            Console.WriteLine($"  Error             : {result.ErrorMessage}");
        Console.WriteLine(new string('=', 80));
    }

    public void RenderTelemetry(TelemetrySummary summary)
    {
        Console.WriteLine();
        Console.WriteLine("--- Telemetry ---");
        Console.WriteLine($"  Total bytes      : {FormatBytes(summary.TotalBytesTransferred)}");
        Console.WriteLine($"  Files completed  : {summary.TotalFilesCompleted}");
        Console.WriteLine($"  Retries          : {summary.TotalRetries}");
        Console.WriteLine($"  Reconnects       : {summary.TotalReconnects}");
        Console.WriteLine($"  Corruptions      : {summary.TotalCorruptions}");
        Console.WriteLine($"  Errors           : {summary.TotalErrors}");
        Console.WriteLine($"  Active sessions  : {summary.ActiveSessions}");
    }

    private static string BuildProgressBar(double percent, int width)
    {
        var filled = (int)(percent / 100 * width);
        filled = Math.Clamp(filled, 0, width);
        return "[" + new string('#', filled) + new string('-', width - filled) + "]";
    }

    private static string FormatSpeed(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1_073_741_824 => $"{bytesPerSecond / 1_073_741_824:F1} GB/s",
        >= 1_048_576 => $"{bytesPerSecond / 1_048_576:F1} MB/s",
        >= 1024 => $"{bytesPerSecond / 1024:F1} KB/s",
        _ => $"{bytesPerSecond:F0} B/s"
    };

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F2} MB",
        >= 1024 => $"{bytes / 1024.0:F2} KB",
        _ => $"{bytes} B"
    };

    private static string FormatDuration(TimeSpan ts) =>
        ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
}

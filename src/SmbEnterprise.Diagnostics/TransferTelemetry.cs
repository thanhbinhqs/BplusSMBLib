using System.Collections.Concurrent;
using SmbEnterprise.Core.Models;
using Microsoft.Extensions.Logging;

namespace SmbEnterprise.Diagnostics;

/// <summary>
/// Aggregates and exposes real-time transfer telemetry.
/// Thread-safe; suitable for multi-file parallel transfers.
/// </summary>
public sealed class TransferTelemetry
{
    private readonly ConcurrentDictionary<Guid, SessionTelemetry> _sessions = new();
    private readonly ILogger<TransferTelemetry> _logger;

    // Global counters
    private long _totalBytesTransferred;
    private long _totalFilesCompleted;
    private long _totalRetries;
    private long _totalReconnects;
    private long _totalCorruptions;
    private long _totalErrors;

    public TransferTelemetry(ILogger<TransferTelemetry> logger)
    {
        _logger = logger;
    }

    public long TotalBytesTransferred => Interlocked.Read(ref _totalBytesTransferred);
    public long TotalFilesCompleted => Interlocked.Read(ref _totalFilesCompleted);
    public long TotalRetries => Interlocked.Read(ref _totalRetries);
    public long TotalReconnects => Interlocked.Read(ref _totalReconnects);
    public long TotalCorruptions => Interlocked.Read(ref _totalCorruptions);
    public long TotalErrors => Interlocked.Read(ref _totalErrors);

    public SessionTelemetry StartSession(Guid sessionId, string correlationId)
    {
        var telemetry = new SessionTelemetry(sessionId, correlationId);
        _sessions[sessionId] = telemetry;
        _logger.LogDebug("Telemetry session started: {SessionId} [{CorrelationId}]", sessionId, correlationId);
        return telemetry;
    }

    public void RecordProgress(Guid sessionId, TransferProgress progress)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return;
        session.Update(progress);
    }

    public void RecordRetry(Guid sessionId)
    {
        Interlocked.Increment(ref _totalRetries);
        if (_sessions.TryGetValue(sessionId, out var s))
            Interlocked.Increment(ref s.RetryCount);
    }

    public void RecordReconnect(Guid sessionId)
    {
        Interlocked.Increment(ref _totalReconnects);
        if (_sessions.TryGetValue(sessionId, out var s))
            Interlocked.Increment(ref s.ReconnectCount);
    }

    public void RecordCorruption(Guid sessionId)
    {
        Interlocked.Increment(ref _totalCorruptions);
        if (_sessions.TryGetValue(sessionId, out var s))
            Interlocked.Increment(ref s.CorruptChunkCount);
    }

    public void RecordError(Guid sessionId, string error)
    {
        Interlocked.Increment(ref _totalErrors);
        _logger.LogWarning("Transfer error [{SessionId}]: {Error}", sessionId, error);
    }

    public void CompleteSession(Guid sessionId, bool success)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return;
        session.CompletedAt = DateTime.UtcNow;
        session.Succeeded = success;
        Interlocked.Increment(ref _totalFilesCompleted);
        Interlocked.Add(ref _totalBytesTransferred, session.BytesTransferred);

        _logger.LogInformation(
            "Session complete: {SessionId} success={Success} bytes={Bytes:N0} retries={Retries} reconnects={Reconnects}",
            sessionId, success, session.BytesTransferred, session.RetryCount, session.ReconnectCount);
    }

    public TelemetrySummary GetSummary() => new()
    {
        TotalBytesTransferred = TotalBytesTransferred,
        TotalFilesCompleted = TotalFilesCompleted,
        TotalRetries = TotalRetries,
        TotalReconnects = TotalReconnects,
        TotalCorruptions = TotalCorruptions,
        TotalErrors = TotalErrors,
        ActiveSessions = _sessions.Values.Count(s => s.CompletedAt == null)
    };
}

public sealed class SessionTelemetry
{
    public Guid SessionId { get; }
    public string CorrelationId { get; }
    public DateTime StartedAt { get; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public bool Succeeded { get; set; }
    public long BytesTransferred { get; private set; }
    public long TotalBytes { get; private set; }
    public double SpeedBytesPerSecond { get; private set; }
    public long RetryCount;
    public long ReconnectCount;
    public long CorruptChunkCount;
    public TransferStage Stage { get; private set; }

    public SessionTelemetry(Guid sessionId, string correlationId)
    {
        SessionId = sessionId;
        CorrelationId = correlationId;
    }

    internal void Update(TransferProgress progress)
    {
        BytesTransferred = progress.TransferredBytes;
        TotalBytes = progress.TotalBytes;
        SpeedBytesPerSecond = progress.SpeedBytesPerSecond;
        Stage = progress.Stage;
    }
}

public sealed class TelemetrySummary
{
    public long TotalBytesTransferred { get; init; }
    public long TotalFilesCompleted { get; init; }
    public long TotalRetries { get; init; }
    public long TotalReconnects { get; init; }
    public long TotalCorruptions { get; init; }
    public long TotalErrors { get; init; }
    public int ActiveSessions { get; init; }
}

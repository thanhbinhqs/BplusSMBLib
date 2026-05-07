using System.Collections.Concurrent;
using SmbEnterprise.Core.Models;
using Microsoft.Extensions.Logging;

namespace SmbEnterprise.Protocol.SMB.Connection;

/// <summary>
/// Thread-safe SMB session pool keyed by server + share + username.
/// Sessions are reused across concurrent operations to avoid reconnect overhead.
/// </summary>
public sealed class SmbSessionPool : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, SmbSessionPoolEntry> _pool = new();
    private readonly ILogger<SmbSessionPool> _logger;
    private readonly SmbSessionPoolOptions _options;
    private readonly Timer _idleTimer;
    private bool _disposed;

    public SmbSessionPool(ILogger<SmbSessionPool> logger, SmbSessionPoolOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new SmbSessionPoolOptions();
        _idleTimer = new Timer(PurgeIdleSessions, null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Acquire a session for the given credential.
    /// Creates a new session if none available or pool limit not reached.
    /// </summary>
    public async Task<PooledSession> AcquireAsync(RemoteCredential credential, CancellationToken cancellationToken)
    {
        var key = BuildKey(credential);
        var entry = _pool.GetOrAdd(key, _ => new SmbSessionPoolEntry(_options.MaxSessionsPerShare));
        var waitStart = DateTime.UtcNow;

        await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        var waitedMs = (DateTime.UtcNow - waitStart).TotalMilliseconds;
        _logger.LogDebug("AcquireAsync key={Key} waitedMs={WaitedMs:F0} idleSessions={IdleCount}",
            key, waitedMs, entry.IdleSessions.Count);

        try
        {
            // Try to get an idle connected session from the pool.
            while (entry.IdleSessions.TryDequeue(out var existing))
            {
                if (existing.IsConnected)
                {
                    existing.Touch();
                    _logger.LogDebug("Reusing existing SMB session for {Key}. idleAfterDequeue={IdleCount}", key, entry.IdleSessions.Count);
                    return new PooledSession(existing, entry, _logger);
                }

                _logger.LogDebug("Discarding disconnected SMB session from idle queue for {Key}", key);
                await existing.DisposeAsync().ConfigureAwait(false);
            }

            // Create a new session
            _logger.LogDebug("Creating new SMB session for {Key}", key);
            var session = await SmbSession.ConnectAsync(credential, _logger, cancellationToken).ConfigureAwait(false);
            return new PooledSession(session, entry, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AcquireAsync failed for key={Key}", key);
            entry.Semaphore.Release();
            throw;
        }
    }

    private void PurgeIdleSessions(object? state)
    {
        var cutoff = DateTime.UtcNow - _options.IdleTimeout;
        foreach (var (key, entry) in _pool)
        {
            var toDispose = new List<SmbSession>();
            var remaining = new Queue<SmbSession>();

            while (entry.IdleSessions.TryDequeue(out var s))
            {
                if (s.LastActivity < cutoff || !s.IsConnected)
                    toDispose.Add(s);
                else
                    remaining.Enqueue(s);
            }

            foreach (var s in remaining)
                entry.IdleSessions.Enqueue(s);

            foreach (var s in toDispose)
            {
                _logger.LogDebug("Disposing idle SMB session for {Key}. idleTimeout={IdleTimeout}s", key, _options.IdleTimeout.TotalSeconds);
                _ = s.DisposeAsync().AsTask();
            }
        }
    }

    private static string BuildKey(RemoteCredential credential) =>
        $"{credential.Server.ToLowerInvariant()}|{credential.Share.ToLowerInvariant()}|{(credential.Username ?? string.Empty).ToLowerInvariant()}";

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _logger.LogDebug("Disposing SmbSessionPool entries={Count}", _pool.Count);
        await _idleTimer.DisposeAsync().ConfigureAwait(false);

        foreach (var entry in _pool.Values)
        {
            while (entry.IdleSessions.TryDequeue(out var s))
                await s.DisposeAsync().ConfigureAwait(false);
        }

        _logger.LogDebug("Disposed SmbSessionPool");
    }
}

public sealed class SmbSessionPoolEntry
{
    internal ConcurrentQueue<SmbSession> IdleSessions { get; } = new();
    public SemaphoreSlim Semaphore { get; }

    public SmbSessionPoolEntry(int maxConcurrent)
    {
        Semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }
}

/// <summary>An acquired session that returns itself to the pool on dispose.</summary>
public sealed class PooledSession : IAsyncDisposable
{
    private readonly SmbSessionPoolEntry _entry;
    private readonly ILogger<SmbSessionPool>? _logger;
    private bool _disposed;

    internal SmbSession Session { get; }

    internal PooledSession(SmbSession session, SmbSessionPoolEntry entry, ILogger<SmbSessionPool>? logger = null)
    {
        Session = session;
        _entry = entry;
        _logger = logger;
    }

    internal void MarkFaulted(string reason)
    {
        Session.MarkDisconnected(reason);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (Session.IsConnected)
        {
            _entry.IdleSessions.Enqueue(Session);
            _logger?.LogDebug("PooledSession returned to idle queue. idleCount={IdleCount}", _entry.IdleSessions.Count);
        }
        else
        {
            try
            {
                await Session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "PooledSession dispose ignored because session already disconnected");
            }
            _logger?.LogDebug("PooledSession disposed because underlying session is disconnected");
        }

        _entry.Semaphore.Release();
        _logger?.LogDebug("PooledSession semaphore released");
    }
}

public sealed class SmbSessionPoolOptions
{
    public int MaxSessionsPerShare { get; init; } = 8;
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan KeepaliveInterval { get; init; } = TimeSpan.FromSeconds(30);
}

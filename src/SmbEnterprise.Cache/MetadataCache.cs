using System.Collections.Concurrent;
using SmbEnterprise.Core.Models;
using Microsoft.Extensions.Logging;

namespace SmbEnterprise.Cache;

/// <summary>
/// Thread-safe in-memory cache for file metadata and directory listings.
/// Uses absolute expiry + sliding expiry with LRU eviction.
/// </summary>
public sealed class MetadataCache : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, CacheEntry<FileMetadata>> _fileCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CacheEntry<List<FileItem>>> _dirCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly MetadataCacheOptions _options;
    private readonly ILogger<MetadataCache> _logger;
    private readonly Timer _evictionTimer;

    public MetadataCache(MetadataCacheOptions options, ILogger<MetadataCache> logger)
    {
        _options = options;
        _logger = logger;
        _evictionTimer = new Timer(EvictExpired, null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public bool TryGetMetadata(string path, out FileMetadata? metadata)
    {
        if (_fileCache.TryGetValue(path, out var entry) && !entry.IsExpired(_options.MetadataTtl))
        {
            entry.Touch();
            metadata = entry.Value;
            return true;
        }
        metadata = null;
        return false;
    }

    public void SetMetadata(string path, FileMetadata metadata)
    {
        _fileCache[path] = new CacheEntry<FileMetadata>(metadata);
        EnforceMaxSize(_fileCache, _options.MaxMetadataEntries);
    }

    public bool TryGetDirectoryListing(string path, out List<FileItem>? items)
    {
        if (_dirCache.TryGetValue(path, out var entry) && !entry.IsExpired(_options.DirectoryTtl))
        {
            entry.Touch();
            items = entry.Value;
            return true;
        }
        items = null;
        return false;
    }

    public void SetDirectoryListing(string path, List<FileItem> items)
    {
        _dirCache[path] = new CacheEntry<List<FileItem>>(items);
        EnforceMaxSize(_dirCache, _options.MaxDirectoryEntries);
    }

    public void Invalidate(string path)
    {
        _fileCache.TryRemove(path, out _);
        _dirCache.TryRemove(path, out _);
    }

    public void InvalidateDirectory(string directoryPath)
    {
        _dirCache.TryRemove(directoryPath, out _);
        var prefix = directoryPath.TrimEnd('\\') + "\\";
        foreach (var key in _fileCache.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            _fileCache.TryRemove(key, out _);
    }

    private void EvictExpired(object? state)
    {
        var evicted = 0;
        foreach (var key in _fileCache.Keys)
        {
            if (_fileCache.TryGetValue(key, out var e) && e.IsExpired(_options.MetadataTtl))
            {
                _fileCache.TryRemove(key, out _);
                evicted++;
            }
        }
        foreach (var key in _dirCache.Keys)
        {
            if (_dirCache.TryGetValue(key, out var e) && e.IsExpired(_options.DirectoryTtl))
            {
                _dirCache.TryRemove(key, out _);
                evicted++;
            }
        }
        if (evicted > 0)
            _logger.LogDebug("Evicted {Count} expired cache entries", evicted);
    }

    private static void EnforceMaxSize<T>(ConcurrentDictionary<string, CacheEntry<T>> dict, int maxSize)
    {
        if (dict.Count <= maxSize) return;
        var oldest = dict.OrderBy(kv => kv.Value.LastAccessed).Take(dict.Count - maxSize);
        foreach (var (key, _) in oldest)
            dict.TryRemove(key, out _);
    }

    public ValueTask DisposeAsync()
    {
        _evictionTimer.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class CacheEntry<T>
{
    public T Value { get; }
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public DateTime LastAccessed { get; private set; } = DateTime.UtcNow;

    public CacheEntry(T value) => Value = value;

    public void Touch() => LastAccessed = DateTime.UtcNow;
    public bool IsExpired(TimeSpan ttl) => DateTime.UtcNow - CreatedAt > ttl;
}

public sealed class MetadataCacheOptions
{
    public TimeSpan MetadataTtl { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan DirectoryTtl { get; init; } = TimeSpan.FromSeconds(15);
    public int MaxMetadataEntries { get; init; } = 10_000;
    public int MaxDirectoryEntries { get; init; } = 1_000;
}

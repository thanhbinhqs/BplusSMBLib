using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;

namespace SmbEnterprise.WinFormsApp.Transfer;

public enum TransferQueueState
{
    Queued,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public sealed class TransferQueueItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required IRemoteFileSystem FileSystem { get; init; }
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public bool VerifyChecksum { get; init; }
    public TransferQueueState State { get; set; } = TransferQueueState.Queued;
    public TransferProgress? Progress { get; set; }
    public string? Error { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public CancellationTokenSource CancellationTokenSource { get; private set; } = new();

    public void ResetCancellation()
    {
        if (!CancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        CancellationTokenSource.Dispose();
        CancellationTokenSource = new CancellationTokenSource();
    }
}

public sealed class TransferQueueController : IAsyncDisposable
{
    private readonly TransferViewModel _viewModel;
    private readonly ILogger<TransferQueueController> _logger;
    private readonly Channel<TransferQueueItem> _queue;
    private readonly ConcurrentDictionary<Guid, TransferQueueItem> _items = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<Task> _workers;

    public event EventHandler<TransferQueueItem>? ItemUpdated;

    public TransferQueueController(TransferViewModel viewModel, ILogger<TransferQueueController> logger)
    {
        _viewModel = viewModel;
        _logger = logger;
        _queue = Channel.CreateUnbounded<TransferQueueItem>();
        _workers = [
            Task.Run(() => WorkerLoopAsync(_shutdown.Token)),
            Task.Run(() => WorkerLoopAsync(_shutdown.Token))
        ];
    }

    public IReadOnlyCollection<TransferQueueItem> Items => _items.Values.ToArray();

    public Guid Enqueue(IRemoteFileSystem fileSystem, string sourcePath, string destinationPath, bool verifyChecksum)
    {
        var item = new TransferQueueItem
        {
            FileSystem = fileSystem,
            SourcePath = sourcePath,
            DestinationPath = destinationPath,
            VerifyChecksum = verifyChecksum
        };

        _items[item.Id] = item;
        _queue.Writer.TryWrite(item);
        Publish(item);

        _logger.LogInformation("Queued transfer {Id}: {Source} -> {Destination}", item.Id, sourcePath, destinationPath);

        return item.Id;
    }

    public bool TryCancel(Guid id)
    {
        if (!_items.TryGetValue(id, out var item))
        {
            return false;
        }

        item.CancellationTokenSource.Cancel();
        item.State = TransferQueueState.Cancelled;
        Publish(item);
        return true;
    }

    public bool TryPause(Guid id)
    {
        if (!_items.TryGetValue(id, out var item))
        {
            return false;
        }

        if (item.State is TransferQueueState.Queued or TransferQueueState.Running)
        {
            item.CancellationTokenSource.Cancel();
            item.State = TransferQueueState.Paused;
            Publish(item);
            return true;
        }

        return false;
    }

    public bool TryResume(Guid id)
    {
        if (!_items.TryGetValue(id, out var item))
        {
            return false;
        }

        if (item.State != TransferQueueState.Paused)
        {
            return false;
        }

        item.State = TransferQueueState.Queued;
        item.Error = null;
        item.Progress = null;
        item.ResetCancellation();
        Publish(item);
        _queue.Writer.TryWrite(item);
        return true;
    }

    public bool TryRetry(Guid id)
    {
        if (!_items.TryGetValue(id, out var item))
        {
            return false;
        }

        if (item.State is not (TransferQueueState.Failed or TransferQueueState.Cancelled))
        {
            return false;
        }

        item.RetryCount++;
        item.State = TransferQueueState.Queued;
        item.Error = null;
        item.Progress = null;
        item.ResetCancellation();
        Publish(item);
        _queue.Writer.TryWrite(item);
        return true;
    }

    private async Task WorkerLoopAsync(CancellationToken token)
    {
        while (await _queue.Reader.WaitToReadAsync(token).ConfigureAwait(false))
        {
            while (_queue.Reader.TryRead(out var item))
            {
                if (item.State != TransferQueueState.Queued)
                {
                    continue;
                }

                item.State = TransferQueueState.Running;
                Publish(item);

                try
                {
                    var progress = new Progress<TransferProgress>(p =>
                    {
                        item.Progress = p;
                        Publish(item);
                    });

                    var result = await _viewModel.TransferFileAsync(
                        item.FileSystem,
                        item.SourcePath,
                        item.DestinationPath,
                        item.VerifyChecksum,
                        progress,
                        item.CancellationTokenSource.Token).ConfigureAwait(false);

                    if (result.Success)
                    {
                        item.State = TransferQueueState.Completed;
                    }
                    else if (item.CancellationTokenSource.IsCancellationRequested)
                    {
                        item.State = item.State == TransferQueueState.Paused ? TransferQueueState.Paused : TransferQueueState.Cancelled;
                    }
                    else
                    {
                        item.State = TransferQueueState.Failed;
                        item.Error = result.ErrorMessage;
                    }
                }
                catch (OperationCanceledException)
                {
                    if (item.State != TransferQueueState.Paused)
                    {
                        item.State = TransferQueueState.Cancelled;
                    }
                }
                catch (Exception ex)
                {
                    item.State = TransferQueueState.Failed;
                    item.Error = ex.Message;
                    _logger.LogError(ex, "Transfer queue item failed: {Id}", item.Id);
                }

                Publish(item);
            }
        }
    }

    private void Publish(TransferQueueItem item)
    {
        ItemUpdated?.Invoke(this, item);
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _queue.Writer.TryComplete();
        try
        {
            await Task.WhenAll(_workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        _shutdown.Dispose();

        foreach (var item in _items.Values)
        {
            item.CancellationTokenSource.Dispose();
        }
    }
}

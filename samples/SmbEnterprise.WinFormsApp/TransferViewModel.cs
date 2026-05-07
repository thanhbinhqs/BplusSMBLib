using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;
using SmbEnterprise.Transfer;
using SmbEnterprise.Transfer.Abstractions;
using SmbEnterprise.Diagnostics;
using SmbEnterprise.Checksum;
using SmbEnterprise.Cache;
using Microsoft.Extensions.Logging;
using SmbEnterprise.WinFormsApp.Services;

namespace SmbEnterprise.WinFormsApp;

public sealed class TransferViewModel
{
    private readonly IFileSystemProvider _smbProvider;
    private readonly LocalFileSystem _localDestinationFs;
    private readonly IChecksumEngine _checksumEngine;
    private readonly TransferTelemetry _telemetry;
    private readonly MetadataCache _cache;
    private readonly ILogger<TransferViewModel> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public event EventHandler<TransferProgress>? OnProgress;
    public event EventHandler<string>? OnError;
    public event EventHandler? OnCompleted;

    public TransferViewModel(
        IFileSystemProvider smbProvider,
        TransferTelemetry telemetry,
        MetadataCache cache,
        ILogger<TransferViewModel> logger,
        ILoggerFactory loggerFactory)
    {
        _smbProvider = smbProvider;
        _localDestinationFs = new LocalFileSystem();
        _checksumEngine = ChecksumEngineFactory.Create(ChecksumAlgorithm.XxHash64);
        _telemetry = telemetry;
        _cache = cache;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    private ITransferEngine CreateTransferEngine(IRemoteFileSystem sourceFs)
    {
        // Destination in WinForms app is local machine folder, so use LocalFileSystem.
        var transferLogger = _loggerFactory.CreateLogger<TransferEngine>();
        return new TransferEngine(sourceFs, new IRemoteFileSystem[] { _localDestinationFs }, transferLogger);
    }

    public async Task<IRemoteFileSystem> ConnectAsync(RemoteCredential credential, CancellationToken ct = default)
    {
        try
        {
            var fs = _smbProvider.CreateFileSystem();
            await fs.ConnectAsync(credential, ct);
            _logger.LogInformation("Connected to {Server}\\{Share}", credential.Server, credential.Share);
            return fs;
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, $"Connection failed: {ex.Message}");
            throw;
        }
    }

    public async Task<List<FileItem>> ListDirectoryAsync(
        IRemoteFileSystem fs,
        string path,
        CancellationToken ct = default)
    {
        try
        {
            // Try cache first
            if (_cache.TryGetDirectoryListing(path, out var cached))
                return cached!;

            var items = new List<FileItem>();
            await foreach (var item in fs.ListDirectoryAsync(path, ct))
                items.Add(item);

            _cache.SetDirectoryListing(path, items);
            return items;
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, $"List directory failed: {ex.Message}");
            return [];
        }
    }

    public async Task<TransferResult> TransferFileAsync(
        IRemoteFileSystem sourceFs,
        string sourcePath,
        string destinationPath,
        bool verifyChecksum = true,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var transferEngine = CreateTransferEngine(sourceFs);

        var sessionId = Guid.NewGuid();
        _ = _telemetry.StartSession(sessionId, sourcePath);

        try
        {
            var options = new TransferOptions
            {
                VerifyAfterCopy = verifyChecksum,
                ChecksumAlgorithm = ChecksumAlgorithm.XxHash64,
                MaxParallelWorkers = 1,
                ChunkSize = 256 * 1024,
                MaxChunkSize = 1024 * 1024,
                MinChunkSize = 64 * 1024,
                MaxChunkRetries = 8
            };

            var progressAdapter = new Progress<TransferProgress>(p =>
            {
                progress?.Report(p);
                OnProgress?.Invoke(this, p);
            });

            var result = await transferEngine.TransferAsync(
                sourcePath,
                destinationPath,
                options,
                progressAdapter,
                cancellationToken);

            _telemetry.CompleteSession(sessionId, result.Success);

            if (result.Success)
            {
                _logger.LogInformation("Transfer completed: {Path} ({Bytes} bytes, {Duration}s)",
                    sourcePath, result.BytesTransferred, result.Duration.TotalSeconds);
                OnCompleted?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                OnError?.Invoke(this, result.ErrorMessage ?? "Transfer failed");
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            _telemetry.CompleteSession(sessionId, false);
            OnError?.Invoke(this, "Transfer cancelled");
            return new TransferResult
            {
                Success = false,
                ErrorMessage = "Transfer cancelled"
            };
        }
        catch (Exception ex)
        {
            _telemetry.CompleteSession(sessionId, false);
            OnError?.Invoke(this, $"Transfer error: {ex.Message}");
            _logger.LogError(ex, "Transfer error");
            return new TransferResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<string?> ComputeChecksumAsync(IRemoteFileSystem fs, string filePath)
    {
        try
        {
            await using var stream = await fs.OpenReadAsync(filePath, 0);
            var meta = await fs.GetMetadataAsync(filePath);

            var checksum = await _checksumEngine.ComputeFileAsync(
                filePath,
                async (offset, buffer, ct) =>
                {
                    await stream.SeekAsync(offset, ct);
                    return await stream.ReadAsync(buffer, ct);
                },
                meta.Size);

            return checksum.HexHash;
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, $"Checksum error: {ex.Message}");
            return null;
        }
    }

    public TelemetrySummary GetTelemetrySummary() => _telemetry.GetSummary();

    public void CancelTransfer()
    {
        // Intentionally empty: cancellation is now controlled per transfer/job via CancellationToken.
    }
}

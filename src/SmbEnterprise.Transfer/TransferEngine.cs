using System.Diagnostics;
using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;
using SmbEnterprise.Transfer.Abstractions;
using SmbEnterprise.Transfer.Pipeline;
using Microsoft.Extensions.Logging;

namespace SmbEnterprise.Transfer;

/// <summary>
/// Main transfer engine implementation.
/// Coordinates source/destination file systems and pipeline execution.
/// </summary>
public sealed class TransferEngine : ITransferEngine
{
    private readonly IRemoteFileSystem _sourceFs;
    private readonly IReadOnlyList<IRemoteFileSystem> _destinationFs;
    private readonly ILogger<TransferEngine> _logger;

    public TransferEngine(
        IRemoteFileSystem sourceFs,
        IReadOnlyList<IRemoteFileSystem> destinationFs,
        ILogger<TransferEngine> logger)
    {
        _sourceFs = sourceFs;
        _destinationFs = destinationFs;
        _logger = logger;
    }

    public async Task<TransferResult> TransferAsync(
        string sourcePath,
        string destinationPath,
        TransferOptions options,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Starting transfer: {Source} → {Dest}", sourcePath, destinationPath);

        try
        {
            // Check resume: get existing destination size for offset
            long resumeOffset = 0;
            if (options.Resume && !options.Overwrite)
            {
                try
                {
                    if (await _destinationFs[0].ExistsAsync(destinationPath, cancellationToken).ConfigureAwait(false))
                    {
                        var destMeta = await _destinationFs[0].GetMetadataAsync(destinationPath, cancellationToken).ConfigureAwait(false);
                        resumeOffset = destMeta.Size;
                        _logger.LogInformation("Resuming from offset {Offset} bytes", resumeOffset);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not determine resume offset; starting from beginning");
                }
            }

            var sourceMeta = await _sourceFs.GetMetadataAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            var sourceSize = sourceMeta.Size;

            if (resumeOffset >= sourceSize)
            {
                _logger.LogInformation("File already fully transferred: {Path}", destinationPath);
                return new TransferResult { Success = true, BytesTransferred = sourceSize, Duration = sw.Elapsed };
            }

            // Ensure destination directory exists
            var destDir = GetParentPath(destinationPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                try
                {
                    await _destinationFs[0].CreateDirectoryAsync(destDir, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not create directory {Dir}", destDir);
                }
            }

            await using var pipeline = new TransferPipeline(_sourceFs, _destinationFs, options, _logger);

            var result = await pipeline.ExecuteAsync(
                sourcePath, [destinationPath], sourceSize - resumeOffset, progress, cancellationToken)
                .ConfigureAwait(false);

            sw.Stop();

            if (result.Success)
                _logger.LogInformation("Transfer complete: {Source} → {Dest} ({Bytes:N0} bytes in {Duration:g})",
                    sourcePath, destinationPath, result.BytesTransferred, sw.Elapsed);
            else
                _logger.LogError("Transfer failed: {Source} → {Dest}: {Errors}",
                    sourcePath, destinationPath, string.Join("; ", result.Errors));

            return new TransferResult
            {
                Success = result.Success,
                ErrorMessage = result.Errors.Count > 0 ? string.Join("; ", result.Errors) : null,
                BytesTransferred = result.BytesTransferred,
                Duration = sw.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Transfer cancelled: {Source}", sourcePath);
            return new TransferResult
            {
                Success = false,
                ErrorMessage = "Transfer cancelled",
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transfer error: {Source} → {Dest}", sourcePath, destinationPath);
            return new TransferResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Duration = sw.Elapsed
            };
        }
    }

    public async Task<MultiDestinationTransferResult> TransferMultiDestinationAsync(
        string sourcePath,
        IReadOnlyList<string> destinationPaths,
        TransferOptions options,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Starting multi-destination transfer: {Source} → {Count} destinations",
            sourcePath, destinationPaths.Count);

        var sourceMeta = await _sourceFs.GetMetadataAsync(sourcePath, cancellationToken).ConfigureAwait(false);

        await using var pipeline = new TransferPipeline(_sourceFs, _destinationFs, options, _logger);
        var pipelineResult = await pipeline.ExecuteAsync(sourcePath, destinationPaths, sourceMeta.Size, progress, cancellationToken).ConfigureAwait(false);

        sw.Stop();

        var results = destinationPaths.Select(dest => (dest, new TransferResult
        {
            Success = pipelineResult.Success,
            BytesTransferred = pipelineResult.BytesTransferred,
            Duration = sw.Elapsed,
            ErrorMessage = pipelineResult.Success ? null : string.Join("; ", pipelineResult.Errors)
        })).ToList();

        return new MultiDestinationTransferResult
        {
            Results = results,
            BytesRead = pipelineResult.BytesTransferred,
            Duration = sw.Elapsed
        };
    }

    public async Task<TransferResult> TransferDirectoryAsync(
        string sourceDirectory,
        string destinationDirectory,
        TransferOptions options,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        long totalBytes = 0;
        int errors = 0;

        await foreach (var item in _sourceFs.ListDirectoryAsync(sourceDirectory, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destPath = destinationDirectory.TrimEnd('\\') + "\\" + item.Name;

            if (item.IsDirectory)
            {
                var subResult = await TransferDirectoryAsync(item.FullPath, destPath, options, progress, cancellationToken).ConfigureAwait(false);
                totalBytes += subResult.BytesTransferred;
                if (!subResult.Success) errors++;
            }
            else
            {
                var fileResult = await TransferAsync(item.FullPath, destPath, options, progress, cancellationToken).ConfigureAwait(false);
                totalBytes += fileResult.BytesTransferred;
                if (!fileResult.Success) errors++;
            }
        }

        return new TransferResult
        {
            Success = errors == 0,
            BytesTransferred = totalBytes,
            Duration = sw.Elapsed,
            ErrorMessage = errors > 0 ? $"{errors} file(s) failed" : null
        };
    }

    public async Task<MultiDestinationTransferResult> TransferMultiDestinationWithIndividualProgressAsync(
        string sourcePath,
        IReadOnlyList<string> destinationPaths,
        TransferOptions options,
        SlowConnectionPolicy? slowPolicy = null,
        IProgress<AggregatedMultiDestinationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var policy = slowPolicy ?? new SlowConnectionPolicy();

        _logger.LogInformation(
            "Starting multi-destination transfer with individual progress: {Source} → {Count} destinations, slow handling={Enabled}",
            sourcePath, destinationPaths.Count, policy.EnableSlowConnectionHandling);

        try
        {
            var sourceMeta = await _sourceFs.GetMetadataAsync(sourcePath, cancellationToken).ConfigureAwait(false);

            // Ensure destination directories exist
            for (int i = 0; i < destinationPaths.Count; i++)
            {
                var destDir = GetParentPath(destinationPaths[i]);
                if (!string.IsNullOrEmpty(destDir))
                {
                    try
                    {
                        await _destinationFs[Math.Min(i, _destinationFs.Count - 1)]
                            .CreateDirectoryAsync(destDir, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not create directory {Dir} for destination[{Index}]", destDir, i);
                    }
                }
            }

            await using var pipeline = new MultiDestinationTransferPipeline(
                _sourceFs, _destinationFs, options, policy, _logger);

            var pipelineResult = await pipeline.ExecuteAsync(
                sourcePath, destinationPaths, sourceMeta.Size, progress, cancellationToken)
                .ConfigureAwait(false);

            sw.Stop();

            // Build results từ aggregated progress
            var results = destinationPaths.Select((dest, idx) => (dest, new TransferResult
            {
                Success = pipelineResult.Success,
                BytesTransferred = pipelineResult.BytesTransferred,
                Duration = sw.Elapsed,
                ErrorMessage = pipelineResult.Success ? null : string.Join("; ", pipelineResult.Errors)
            })).ToList();

            return new MultiDestinationTransferResult
            {
                Results = results,
                BytesRead = pipelineResult.BytesTransferred,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Multi-destination transfer with individual progress failed: {Source}", sourcePath);
            sw.Stop();

            var errorResults = destinationPaths.Select(dest => (dest, new TransferResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Duration = sw.Elapsed
            })).ToList();

            return new MultiDestinationTransferResult
            {
                Results = errorResults,
                BytesRead = 0,
                Duration = sw.Elapsed
            };
        }
    }

    public async Task<DirectoryMultiDestinationTransferResult> TransferDirectoryMultiDestinationAsync(
        string sourceDirectory,
        IReadOnlyList<string> destinationDirectories,
        TransferOptions options,
        SlowConnectionPolicy? slowPolicy = null,
        IProgress<DirectoryTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var sessionId = Guid.NewGuid();
        var policy = slowPolicy ?? new SlowConnectionPolicy();

        _logger.LogInformation(
            "Starting directory multi-destination transfer: {Source} → {Count} destinations",
            sourceDirectory, destinationDirectories.Count);

        var directoryProgress = new DirectoryTransferProgress
        {
            SessionId = sessionId,
            SourceDirectory = sourceDirectory
        };

        var fileResults = new List<FileTransferSummary>();
        long totalBytes = 0;
        int successfulFiles = 0;
        int failedFiles = 0;

        try
        {
            // Collect tất cả files cần transfer
            var filesToTransfer = new List<(string SourcePath, string RelativePath, long Size)>();
            await CollectFilesRecursiveAsync(sourceDirectory, "", filesToTransfer, cancellationToken).ConfigureAwait(false);

            directoryProgress.TotalFiles = filesToTransfer.Count;
            directoryProgress.TotalBytes = filesToTransfer.Sum(f => f.Size);
            progress?.Report(directoryProgress);

            _logger.LogInformation("Found {Count} files to transfer, total size: {Size:N0} bytes",
                filesToTransfer.Count, directoryProgress.TotalBytes);

            // Transfer từng file
            foreach (var (sourcePath, relativePath, size) in filesToTransfer)
            {
                cancellationToken.ThrowIfCancellationRequested();

                directoryProgress.CurrentFile = relativePath;
                progress?.Report(directoryProgress);

                var destPaths = destinationDirectories
                    .Select(destDir => Path.Combine(destDir, relativePath).Replace('/', '\\'))
                    .ToList();

                // Create subdirectories for all destinations
                foreach (var destPath in destPaths)
                {
                    var destDir = GetParentPath(destPath);
                    if (!string.IsNullOrEmpty(destDir))
                    {
                        try
                        {
                            await _destinationFs[0].CreateDirectoryAsync(destDir, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Could not create directory {Dir}", destDir);
                        }
                    }
                }

                // Transfer file với progress tracking
                var fileProgressReporter = new Progress<AggregatedMultiDestinationProgress>(fileProgress =>
                {
                    directoryProgress.CurrentFileProgress = fileProgress;
                    progress?.Report(directoryProgress);
                });

                var fileResult = await TransferMultiDestinationWithIndividualProgressAsync(
                    sourcePath,
                    destPaths,
                    options,
                    policy,
                    fileProgressReporter,
                    cancellationToken).ConfigureAwait(false);

                // Build file summary
                var destResults = fileResult.Results.Select((r, idx) => new DestinationTransferResult
                {
                    DestinationPath = r.Destination,
                    DestinationIndex = idx,
                    Success = r.Result.Success,
                    BytesTransferred = r.Result.BytesTransferred,
                    Duration = r.Result.Duration,
                    AverageSpeedBytesPerSecond = r.Result.Duration.TotalSeconds > 0 
                        ? r.Result.BytesTransferred / r.Result.Duration.TotalSeconds 
                        : 0,
                    WasThrottled = false, // TODO: get from progress
                    ErrorMessage = r.Result.ErrorMessage
                }).ToList();

                var fileSummary = new FileTransferSummary
                {
                    SourcePath = sourcePath,
                    DestinationResults = destResults,
                    FileSize = size,
                    Duration = fileResult.Duration
                };

                fileResults.Add(fileSummary);
                directoryProgress.CompletedFiles.Add(fileSummary);

                if (fileSummary.AllDestinationsSucceeded)
                    successfulFiles++;
                else
                    failedFiles++;

                directoryProgress.ProcessedFiles++;
                directoryProgress.SuccessfulFiles = successfulFiles;
                directoryProgress.FailedFiles = failedFiles;
                totalBytes += fileResult.BytesRead;
                directoryProgress.TransferredBytes = totalBytes;
                directoryProgress.Elapsed = sw.Elapsed;
                directoryProgress.CurrentFileProgress = null;

                progress?.Report(directoryProgress);

                _logger.LogInformation(
                    "File transfer complete: {File} → {Success}/{Total} destinations succeeded",
                    relativePath, fileSummary.SuccessfulDestinations, destinationDirectories.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Directory multi-destination transfer failed: {Source}", sourceDirectory);
        }

        sw.Stop();

        return new DirectoryMultiDestinationTransferResult
        {
            FileResults = fileResults,
            TotalBytesTransferred = totalBytes,
            Duration = sw.Elapsed,
            TotalFiles = directoryProgress.TotalFiles,
            SuccessfulFiles = successfulFiles,
            FailedFiles = failedFiles,
            DestinationCount = destinationDirectories.Count
        };
    }

    private async Task CollectFilesRecursiveAsync(
        string currentPath,
        string relativePath,
        List<(string SourcePath, string RelativePath, long Size)> files,
        CancellationToken cancellationToken)
    {
        await foreach (var item in _sourceFs.ListDirectoryAsync(currentPath, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var itemRelativePath = string.IsNullOrEmpty(relativePath) 
                ? item.Name 
                : Path.Combine(relativePath, item.Name);

            if (item.IsDirectory)
            {
                await CollectFilesRecursiveAsync(
                    item.FullPath,
                    itemRelativePath,
                    files,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                files.Add((item.FullPath, itemRelativePath, item.Size));
            }
        }
    }

    private static string GetParentPath(string path)
    {
        var normalized = path.Replace('/', '\\').TrimEnd('\\');
        var lastSep = normalized.LastIndexOf('\\');
        return lastSep <= 0 ? string.Empty : normalized[..lastSep];
    }
}

using Microsoft.Extensions.Logging;
using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;
using WorkflowManager.Models;

namespace WorkflowManager.Services;

/// <summary>
/// Service để copy files đến nhiều destinations đồng thời
/// </summary>
public class MultiDestinationCopyService
{
    private readonly ILogger<MultiDestinationCopyService> _logger;
    private readonly HashFileReader _hashFileReader;
    private readonly Func<IRemoteFileSystem> _fileSystemFactory;

    public MultiDestinationCopyService(
        ILogger<MultiDestinationCopyService> logger,
        HashFileReader hashFileReader,
        Func<IRemoteFileSystem> fileSystemFactory)
    {
        _logger = logger;
        _hashFileReader = hashFileReader;
        _fileSystemFactory = fileSystemFactory;
    }

    /// <summary>
    /// Copy files từ local folder đến nhiều SMB destinations đồng thời
    /// </summary>
    public async Task<List<DestinationCopyResult>> CopyToMultipleDestinationsAsync(
        string sourceLocalPath,
        string packageName,
        List<DestinationInfo> destinations,
        IProgress<List<DestinationCopyResult>>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<DestinationCopyResult>();

        // Get source files
        var sourceFiles = Directory.GetFiles(sourceLocalPath);
        if (sourceFiles.Length == 0)
        {
            _logger.LogWarning("No files found in source path: {Path}", sourceLocalPath);
            return results;
        }

        // Read expected hashes
        var hashFilePath = Path.Combine(sourceLocalPath, $"{packageName}.hash");
        Dictionary<string, string> expectedHashes = new();

        if (File.Exists(hashFilePath))
        {
            try
            {
                expectedHashes = await _hashFileReader.ReadHashFileAsync(hashFilePath, cancellationToken);
                _logger.LogInformation("Loaded {Count} expected hashes from {File}", 
                    expectedHashes.Count, hashFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading hash file: {Path}", hashFilePath);
            }
        }

        // Initialize results for each destination
        foreach (var dest in destinations.Where(d => d.IsEnabled))
        {
            var result = new DestinationCopyResult
            {
                DestinationName = dest.Name,
                DestinationPath = dest.UncPath,
                FilesTotal = sourceFiles.Length,
                Status = DestinationStatus.Pending
            };

            foreach (var filePath in sourceFiles)
            {
                var fileName = Path.GetFileName(filePath);
                var fileInfo = new FileInfo(filePath);
                var ext = Path.GetExtension(fileName).TrimStart('.');

                result.Files.Add(new FileDestinationCopyInfo
                {
                    FileName = fileName,
                    FileSize = fileInfo.Length,
                    ExpectedHash = expectedHashes.GetValueOrDefault(ext),
                    Status = FileCopyStatus.Pending
                });
            }

            results.Add(result);
        }

        // Report initial state
        progress?.Report(results);

        // Copy to each destination in parallel
        var copyTasks = results.Select(result => 
            CopyToDestinationAsync(
                sourceLocalPath, 
                packageName,
                result, 
                destinations.First(d => d.Name == result.DestinationName),
                () => progress?.Report(results),
                cancellationToken))
            .ToList();

        await Task.WhenAll(copyTasks);

        return results;
    }

    private async Task CopyToDestinationAsync(
        string sourceLocalPath,
        string packageName,
        DestinationCopyResult result,
        DestinationInfo destination,
        Action notifyProgress,
        CancellationToken cancellationToken)
    {
        result.StartTime = DateTime.Now;
        result.Status = DestinationStatus.Connecting;
        notifyProgress();

        IRemoteFileSystem? fileSystem = null;

        try
        {
            // Create and connect file system
            fileSystem = _fileSystemFactory();

            var (server, share, _) = ParseUncPathParts(destination.UncPath);

            var credential = new RemoteCredential
            {
                Server = server,
                Share = share,
                Username = destination.Username,
                Password = destination.Password
            };

            _logger.LogInformation("Connecting to {Destination} at \\\\{Server}\\{Share} with user {User}", 
                result.DestinationName, server, share, destination.Username);

            try
            {
                await fileSystem.ConnectAsync(credential, cancellationToken);
                _logger.LogInformation("Successfully connected to {Destination}", result.DestinationName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection failed to {Destination} at \\\\{Server}\\{Share}", 
                    result.DestinationName, server, share);
                throw new InvalidOperationException(
                    $"Cannot connect to {destination.UncPath}: {ex.Message}", ex);
            }

            // Get the relative path from UNC destination
            var (_, _, basePath) = ParseUncPathParts(destination.UncPath);

            // Create directory path relative to share: basePath\packageName
            var relativeDestPath = string.IsNullOrEmpty(basePath) 
                ? packageName 
                : $"{basePath}\\{packageName}";

            _logger.LogInformation("Creating directory: {RelativePath} on {Server}\\{Share}", 
                relativeDestPath, server, share);

            await EnsureDirectoryExistsAsync(fileSystem, relativeDestPath, cancellationToken);

            // Full UNC path for logging
            var destPath = CombinePath(destination.UncPath, packageName);

            // Copy files
            result.Status = DestinationStatus.Copying;
            notifyProgress();

            foreach (var fileInfo in result.Files)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    await CopyFileToDestinationAsync(
                        sourceLocalPath,
                        relativeDestPath, // Use relative path for API
                        fileInfo,
                        fileSystem,
                        notifyProgress,
                        cancellationToken);

                    result.FilesCopied++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error copying {File} to {Dest}", 
                        fileInfo.FileName, result.DestinationName);
                    fileInfo.Status = FileCopyStatus.Failed;
                    fileInfo.ErrorMessage = ex.Message;
                    result.FilesFailed++;
                    notifyProgress();
                }
            }

            // Verify files
            result.Status = DestinationStatus.Verifying;
            notifyProgress();

            foreach (var fileInfo in result.Files.Where(f => f.Status == FileCopyStatus.CopyComplete))
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    await VerifyFileOnDestinationAsync(
                        relativeDestPath, // Use relative path for API
                        fileInfo,
                        fileSystem,
                        notifyProgress,
                        cancellationToken);

                    if (fileInfo.Status == FileCopyStatus.Verified)
                    {
                        result.FilesVerified++;
                    }
                    else if (fileInfo.Status == FileCopyStatus.Failed)
                    {
                        result.FilesFailed++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error verifying {File} on {Dest}", 
                        fileInfo.FileName, result.DestinationName);
                    fileInfo.Status = FileCopyStatus.Failed;
                    fileInfo.ErrorMessage = $"Verification failed: {ex.Message}";
                    result.FilesFailed++;
                    notifyProgress();
                }
            }

            // Determine final status
            if (result.FilesFailed > 0)
            {
                result.Status = DestinationStatus.CompletedWithErrors;
            }
            else
            {
                result.Status = DestinationStatus.Completed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy to destination: {Dest} at {Path}", 
                result.DestinationName, destination.UncPath);
            result.Status = DestinationStatus.Failed;
            result.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";

            // Mark all pending files as failed
            foreach (var file in result.Files.Where(f => f.Status == FileCopyStatus.Pending || f.Status == FileCopyStatus.Copying))
            {
                file.Status = FileCopyStatus.Failed;
                file.ErrorMessage = "Destination failed";
                result.FilesFailed++;
            }
        }
        finally
        {
            if (fileSystem != null)
            {
                await fileSystem.DisconnectAsync(cancellationToken);
            }

            result.EndTime = DateTime.Now;
            notifyProgress();
        }
    }

    private async Task CopyFileToDestinationAsync(
        string sourceLocalPath,
        string destRemotePath, // Relative path: "A1\packageName" or "packageName"
        FileDestinationCopyInfo fileInfo,
        IRemoteFileSystem fileSystem,
        Action notifyProgress,
        CancellationToken cancellationToken)
    {
        fileInfo.Status = FileCopyStatus.Copying;
        fileInfo.BytesCopied = 0;
        notifyProgress();

        var sourceFilePath = Path.Combine(sourceLocalPath, fileInfo.FileName);
        // Combine relative path with filename using backslash
        var destFilePath = $"{destRemotePath}\\{fileInfo.FileName}";

        _logger.LogDebug("Copying {Source} to {Dest}", sourceFilePath, destFilePath);

        // Copy file with progress tracking
        {
            await using var localStream = new FileStream(
                sourceFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);

            // Use createNew = false to allow overwriting existing files
            await using var remoteStream = await fileSystem.OpenWriteAsync(destFilePath, 0, false, cancellationToken);

            var buffer = new byte[81920];
            int bytesRead;

            while ((bytesRead = await localStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await remoteStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);

                fileInfo.BytesCopied += bytesRead;

                // Report progress periodically
                if (fileInfo.BytesCopied % (1024 * 1024) == 0 || fileInfo.BytesCopied == fileInfo.FileSize)
                {
                    notifyProgress();
                }
            }

            await remoteStream.FlushAsync(cancellationToken);
        } // Streams disposed here

        fileInfo.Status = FileCopyStatus.CopyComplete;
        notifyProgress();
    }

    private async Task VerifyFileOnDestinationAsync(
        string destRemotePath, // Relative path: "A1\packageName" or "packageName"
        FileDestinationCopyInfo fileInfo,
        IRemoteFileSystem fileSystem,
        Action notifyProgress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(fileInfo.ExpectedHash))
        {
            fileInfo.Status = FileCopyStatus.Verified;
            notifyProgress();
            return;
        }

        fileInfo.Status = FileCopyStatus.Verifying;
        notifyProgress();

        // Combine relative path with filename using backslash
        var destFilePath = $"{destRemotePath}\\{fileInfo.FileName}";

        // Read file and compute MD5
        using var md5 = System.Security.Cryptography.MD5.Create();
        await using var remoteStream = await fileSystem.OpenReadAsync(destFilePath, 0, cancellationToken);

        var buffer = new byte[81920];
        int bytesRead;

        while ((bytesRead = await remoteStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            md5.TransformBlock(buffer, 0, bytesRead, null, 0);

            if (cancellationToken.IsCancellationRequested)
                break;
        }

        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        var hash = md5.Hash;
        if (hash != null)
        {
            fileInfo.ActualHash = BitConverter.ToString(hash).Replace("-", "");

            if (fileInfo.IsHashMatch)
            {
                fileInfo.Status = FileCopyStatus.Verified;
                _logger.LogInformation("Hash verified for {File}: {Hash}", 
                    fileInfo.FileName, fileInfo.ActualHash);
            }
            else
            {
                fileInfo.Status = FileCopyStatus.HashMismatch;
                _logger.LogWarning("Hash mismatch for {File}! Expected: {Expected}, Actual: {Actual}",
                    fileInfo.FileName, fileInfo.ExpectedHash, fileInfo.ActualHash);
            }
        }

        notifyProgress();
    }

    private async Task EnsureDirectoryExistsAsync(
        IRemoteFileSystem fileSystem,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            // Split path into parts and create each level
            var parts = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            var currentPath = string.Empty;

            foreach (var part in parts)
            {
                currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}\\{part}";

                try
                {
                    // Check if directory already exists before trying to create
                    bool exists = await fileSystem.ExistsAsync(currentPath, cancellationToken);
                    if (!exists)
                    {
                        await fileSystem.CreateDirectoryAsync(currentPath, cancellationToken);
                        _logger.LogInformation("Directory created: {Path}", currentPath);
                    }
                    else
                    {
                        _logger.LogDebug("Directory already exists: {Path}", currentPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking/creating directory: {Path}", currentPath);
                    // Directory might already exist or be created by file write, continue
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not create directory structure: {Path}", path);
            // Continue anyway - might exist or be created by file write
        }
    }

    private string CombinePath(string uncPath, string relativePath)
    {
        if (uncPath.EndsWith("\\"))
            uncPath = uncPath.TrimEnd('\\');

        return $"{uncPath}\\{relativePath}";
    }

    private (string server, string share, string path) ParseUncPathParts(string uncPath)
    {
        if (!uncPath.StartsWith("\\\\"))
            throw new ArgumentException("Invalid UNC path format");

        var parts = uncPath.Substring(2).Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            throw new ArgumentException("UNC path must include server and share");

        var server = parts[0];
        var share = parts[1];
        var path = parts.Length > 2 ? string.Join("\\", parts.Skip(2)) : string.Empty;

        return (server, share, path);
    }
}

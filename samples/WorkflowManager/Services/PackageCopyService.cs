using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;
using Microsoft.Extensions.Logging;
using WorkflowManager.Models;

namespace WorkflowManager.Services;

/// <summary>
/// Service để copy package từ SMB share xuống local với verification
/// </summary>
public sealed class PackageCopyService
{
    private readonly ILogger<PackageCopyService> _logger;
    private readonly Func<IRemoteFileSystem> _fileSystemFactory;
    private readonly HashFileReader _hashFileReader;

    public PackageCopyService(
        ILogger<PackageCopyService> logger,
        Func<IRemoteFileSystem> fileSystemFactory,
        HashFileReader hashFileReader)
    {
        _logger = logger;
        _fileSystemFactory = fileSystemFactory;
        _hashFileReader = hashFileReader;
    }

    public async Task<CopyResult> CopyPackageWithVerificationAsync(
        SmbConnectionConfig config,
        PackageInfo package,
        string destinationPath,
        IProgress<FileCopyInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new CopyResult
        {
            PackageName = package.FolderName,
            DestinationPath = destinationPath,
            StartTime = DateTime.Now
        };

        try
        {
            _logger.LogInformation("Starting copy of package {FolderName} to {DestinationPath}", 
                package.FolderName, destinationPath);

            var (server, share, _) = ParseUncPath(config.SharePath);
            var credential = new RemoteCredential
            {
                Server = server,
                Share = share,
                Username = config.Username,
                Password = config.Password
            };

            var fileSystem = _fileSystemFactory();
            await fileSystem.ConnectAsync(credential, cancellationToken);

            // Tạo thư mục đích
            var packageDestPath = Path.Combine(destinationPath, package.FolderName);
            if (!Directory.Exists(packageDestPath))
            {
                Directory.CreateDirectory(packageDestPath);
            }

            // Liệt kê tất cả file trong package
            var fileList = new List<FileItem>();
            await foreach (var file in fileSystem.ListDirectoryAsync(package.FullPath, cancellationToken))
            {
                if (!file.IsDirectory && file.Name != "." && file.Name != "..")
                {
                    fileList.Add(file);
                }
            }

            // Đọc file .hash để lấy expected checksums
            var hashFile = fileList.FirstOrDefault(f => f.Name.EndsWith(".hash", StringComparison.OrdinalIgnoreCase));
            Dictionary<string, string> expectedHashes = new();

            if (hashFile != null)
            {
                // Copy hash file first
                var hashSourcePath = CombinePath(package.FullPath, hashFile.Name);
                var hashDestPath = Path.Combine(packageDestPath, hashFile.Name);

                // Copy hash file trong scope riêng để đảm bảo stream được đóng
                {
                    await using var remoteHashStream = await fileSystem.OpenReadAsync(hashSourcePath, 0, cancellationToken);

                    // Xóa file cũ nếu tồn tại để tránh lock
                    if (File.Exists(hashDestPath))
                    {
                        try
                        {
                            File.Delete(hashDestPath);
                            await Task.Delay(100, cancellationToken); // Đợi OS release handle
                        }
                        catch (IOException ex)
                        {
                            _logger.LogWarning(ex, "Could not delete existing hash file, will overwrite");
                        }
                    }

                    await using var localHashStream = new FileStream(
                        hashDestPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 81920,
                        useAsync: true);

                    var buffer = new byte[81920];
                    int bytesRead;
                    while ((bytesRead = await remoteHashStream.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await localHashStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    }

                    await localHashStream.FlushAsync(cancellationToken);
                }
                // Stream đã được đóng ở đây

                // Đợi một chút để đảm bảo OS đã release file handle
                await Task.Delay(100, cancellationToken);

                // Parse hash file
                expectedHashes = await _hashFileReader.ReadHashFileAsync(hashDestPath, cancellationToken);
                _logger.LogInformation("Loaded {Count} expected hashes from .hash file", expectedHashes.Count);
            }

            // Copy and verify each file
            foreach (var file in fileList)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var fileInfo = new FileCopyInfo
                {
                    FileName = file.Name,
                    FileSize = file.Size,
                    ExpectedHash = GetExpectedHash(file.Name, expectedHashes),
                    Status = FileCopyStatus.Copying
                };

                try
                {
                    var sourceFilePath = CombinePath(package.FullPath, file.Name);
                    var destFilePath = Path.Combine(packageDestPath, file.Name);

                    // Skip if already copied (hash file)
                    if (file.Name.Equals(hashFile?.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        fileInfo.Status = FileCopyStatus.CopyComplete;
                        fileInfo.BytesCopied = file.Size;
                        progress?.Report(fileInfo);
                        result.FilesProcessed.Add(fileInfo);
                        continue;
                    }

                    _logger.LogInformation("Copying file: {FileName}", file.Name);
                    progress?.Report(fileInfo);

                    // Copy file - ensure streams are properly closed before verification
                    {
                        await using var remoteStream = await fileSystem.OpenReadAsync(sourceFilePath, 0, cancellationToken);
                        await using var localStream = new FileStream(
                            destFilePath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            bufferSize: 81920,
                            useAsync: true);

                        var buffer = new byte[81920];
                        int bytesRead;

                        while ((bytesRead = await remoteStream.ReadAsync(buffer, cancellationToken)) > 0)
                        {
                            await localStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                            fileInfo.BytesCopied += bytesRead;
                            progress?.Report(fileInfo);
                        }

                        // Explicitly flush and close
                        await localStream.FlushAsync(cancellationToken);
                    } // Streams are disposed here

                    fileInfo.Status = FileCopyStatus.CopyComplete;
                    progress?.Report(fileInfo);

                    // Verify hash if expected
                    if (!string.IsNullOrEmpty(fileInfo.ExpectedHash))
                    {
                        try
                        {
                            fileInfo.Status = FileCopyStatus.Verifying;
                            progress?.Report(fileInfo);

                            _logger.LogInformation("Verifying MD5 for: {FileName}", file.Name);

                            var actualHash = await _hashFileReader.ComputeMd5Async(destFilePath, null, cancellationToken);
                            fileInfo.ActualHash = actualHash;

                            if (fileInfo.IsHashMatch)
                            {
                                fileInfo.Status = FileCopyStatus.Verified;
                                _logger.LogInformation("Hash verified for {FileName}: {Hash}", file.Name, actualHash);
                            }
                            else
                            {
                                fileInfo.Status = FileCopyStatus.HashMismatch;
                                _logger.LogWarning("Hash mismatch for {FileName}! Expected: {Expected}, Actual: {Actual}",
                                    file.Name, fileInfo.ExpectedHash, actualHash);
                            }

                            progress?.Report(fileInfo);
                        }
                        catch (Exception hashEx)
                        {
                            _logger.LogError(hashEx, "Error computing hash for {FileName}", file.Name);
                            fileInfo.Status = FileCopyStatus.Failed;
                            fileInfo.ErrorMessage = $"Hash computation failed: {hashEx.Message}";
                            progress?.Report(fileInfo);
                        }
                    }
                    else
                    {
                        fileInfo.Status = FileCopyStatus.Verified;
                        progress?.Report(fileInfo);
                    }

                    result.FilesProcessed.Add(fileInfo);
                }
                catch (Exception ex)
                {
                    fileInfo.Status = FileCopyStatus.Failed;
                    fileInfo.ErrorMessage = ex.Message;
                    progress?.Report(fileInfo);
                    result.FilesProcessed.Add(fileInfo);
                    _logger.LogError(ex, "Error copying file: {FileName}", file.Name);
                }
            }

            await fileSystem.DisconnectAsync(cancellationToken);

            result.EndTime = DateTime.Now;
            result.Success = result.FilesProcessed.All(f => 
                f.Status == FileCopyStatus.Verified || 
                f.Status == FileCopyStatus.CopyComplete);

            _logger.LogInformation("Copy completed for package {FolderName}. Success: {Success}, Files: {Count}",
                package.FolderName, result.Success, result.FilesProcessed.Count);

            return result;
        }
        catch (Exception ex)
        {
            result.EndTime = DateTime.Now;
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error copying package {FolderName}", package.FolderName);
            throw;
        }
    }

    private static string? GetExpectedHash(string fileName, Dictionary<string, string> hashes)
    {
        // Extract extension without dot (e.g., "whd", "wcl", "w01")
        var extension = Path.GetExtension(fileName).TrimStart('.');

        if (string.IsNullOrEmpty(extension))
            return null;

        // Try direct lookup by extension
        if (hashes.TryGetValue(extension, out var hash))
            return hash;

        // For backwards compatibility, also try the old logic
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var parts = nameWithoutExt.Split('.');
        if (parts.Length > 0)
        {
            var key = parts[^1];
            if (hashes.TryGetValue(key, out var hash2))
                return hash2;
        }

        return null;
    }

    private static (string Server, string Share, string BasePath) ParseUncPath(string uncPath)
    {
        var parts = uncPath.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);

        var server = parts.Length > 0 ? parts[0] : string.Empty;
        var share = parts.Length > 1 ? parts[1] : string.Empty;
        var basePath = parts.Length > 2 ? "\\" + string.Join("\\", parts.Skip(2)) : string.Empty;

        return (server, share, basePath);
    }

    private static string CombinePath(string basePath, string name)
    {
        if (basePath == "\\")
            return "\\" + name;

        return basePath.TrimEnd('\\') + "\\" + name;
    }
}

public sealed class CopyResult
{
    public required string PackageName { get; init; }
    public required string DestinationPath { get; init; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<FileCopyInfo> FilesProcessed { get; } = new();

    public int TotalFiles => FilesProcessed.Count;
    public int VerifiedFiles => FilesProcessed.Count(f => f.Status == FileCopyStatus.Verified);
    public int FailedFiles => FilesProcessed.Count(f => f.Status == FileCopyStatus.Failed || f.Status == FileCopyStatus.HashMismatch);
    public TimeSpan Duration => EndTime - StartTime;
}


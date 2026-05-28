using System.Text.RegularExpressions;
using WorkflowManager.Models;
using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;
using Microsoft.Extensions.Logging;

namespace WorkflowManager.Services;

/// <summary>
/// Service để quét và validate các package trên SMB share
/// </summary>
public sealed partial class PackageScannerService
{
    private readonly ILogger<PackageScannerService> _logger;
    private readonly Func<IRemoteFileSystem> _fileSystemFactory;

    [GeneratedRegex(@"^[A-Z0-9-]+$", RegexOptions.Compiled)]
    private static partial Regex ValidFolderNameRegex();

    public PackageScannerService(
        ILogger<PackageScannerService> logger,
        Func<IRemoteFileSystem> fileSystemFactory)
    {
        _logger = logger;
        _fileSystemFactory = fileSystemFactory;
    }

    /// <summary>
    /// Quét tất cả các folder trong share path và trả về danh sách packages hợp lệ (đệ quy)
    /// </summary>
    public async Task<List<PackageInfo>> ScanPackagesAsync(
        SmbConnectionConfig config,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var packages = new List<PackageInfo>();
        var scanStats = new ScanStatistics();

        try
        {
            _logger.LogInformation("Connecting to SMB share: {SharePath}", config.SharePath);
            progress?.Report(new ScanProgress { Status = "Connecting to SMB share...", ScannedDirectories = 0 });

            var (server, share, basePath) = ParseUncPath(config.SharePath);
            var credential = new RemoteCredential
            {
                Server = server,
                Share = share,
                Username = config.Username,
                Password = config.Password
            };

            var fileSystem = _fileSystemFactory();
            await fileSystem.ConnectAsync(credential, cancellationToken);

            _logger.LogInformation("Connected successfully");
            progress?.Report(new ScanProgress { Status = "Connected, starting scan...", ScannedDirectories = 0 });

            // Scan đệ quy từ basePath
            var scanPath = string.IsNullOrEmpty(basePath) ? "\\" : basePath;
            await ScanDirectoryRecursiveAsync(fileSystem, scanPath, packages, scanStats, progress, cancellationToken);

            await fileSystem.DisconnectAsync(cancellationToken);

            _logger.LogInformation("Scan completed: Found {Count} packages from {ScannedDirs} directories", 
                packages.Count, scanStats.ScannedDirectories);

            progress?.Report(new ScanProgress 
            { 
                Status = $"Scan completed: {packages.Count} packages found", 
                ScannedDirectories = scanStats.ScannedDirectories,
                FoundPackages = packages.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning packages from {SharePath}", config.SharePath);
            throw;
        }

        return packages;
    }

    /// <summary>
    /// Scan đệ quy các thư mục
    /// </summary>
    private async Task ScanDirectoryRecursiveAsync(
        IRemoteFileSystem fileSystem,
        string currentPath,
        List<PackageInfo> packages,
        ScanStatistics scanStats,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken,
        int depth = 0)
    {
        // Giới hạn độ sâu để tránh infinite recursion
        const int MaxDepth = 10;
        if (depth > MaxDepth)
        {
            _logger.LogWarning("Max depth {MaxDepth} reached at path: {Path}", MaxDepth, currentPath);
            return;
        }

        try
        {
            scanStats.ScannedDirectories++;
            _logger.LogDebug("Scanning directory: {Path} (depth: {Depth})", currentPath, depth);

            progress?.Report(new ScanProgress 
            { 
                Status = $"Scanning: {currentPath}", 
                ScannedDirectories = scanStats.ScannedDirectories,
                CurrentDepth = depth,
                FoundPackages = packages.Count
            });

            var entries = new List<FileItem>();
            await foreach (var entry in fileSystem.ListDirectoryAsync(currentPath, cancellationToken))
            {
                if (entry.Name is "." or "..")
                    continue;

                entries.Add(entry);
            }

            // Phân loại entries
            var directories = entries.Where(e => e.IsDirectory).ToList();
            var files = entries.Where(e => !e.IsDirectory).ToList();

            // Kiểm tra xem thư mục hiện tại có phải là package không
            if (await IsPackageDirectoryAsync(files, currentPath))
            {
                var folderName = Path.GetFileName(currentPath.TrimEnd('\\'));
                var packageInfo = await AnalyzePackageAsync(fileSystem, currentPath, folderName, files, cancellationToken);

                if (packageInfo != null)
                {
                    packages.Add(packageInfo);

                    if (packageInfo.IsValid)
                        _logger.LogInformation("Found valid package: {Path}", currentPath);
                    else
                        _logger.LogDebug("Found invalid package: {Path}", currentPath);

                    progress?.Report(new ScanProgress 
                    { 
                        Status = $"Found package: {folderName}", 
                        ScannedDirectories = scanStats.ScannedDirectories,
                        CurrentDepth = depth,
                        FoundPackages = packages.Count
                    });
                }

                // Nếu là package, không scan con nữa (packages không nested)
                return;
            }

            // Không phải package, tiếp tục scan các thư mục con
            foreach (var dir in directories)
            {
                try
                {
                    var subPath = CombinePath(currentPath, dir.Name);
                    await ScanDirectoryRecursiveAsync(fileSystem, subPath, packages, scanStats, progress, cancellationToken, depth + 1);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to scan subdirectory: {DirName}", dir.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error scanning directory: {Path}", currentPath);
        }
    }

    /// <summary>
    /// Statistics for scanning
    /// </summary>
    private sealed class ScanStatistics
    {
        public int ScannedDirectories { get; set; }
    }

    /// <summary>
    /// Kiểm tra xem thư mục có phải là package directory không (có file .hash)
    /// </summary>
    private Task<bool> IsPackageDirectoryAsync(List<FileItem> files, string path)
    {
        var hasHashFile = files.Any(f => f.Name.EndsWith(".hash", StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(hasHashFile);
    }

    /// <summary>
    /// Phân tích package với danh sách files đã có
    /// </summary>
    private async Task<PackageInfo?> AnalyzePackageAsync(
        IRemoteFileSystem fileSystem,
        string packagePath,
        string folderName,
        List<FileItem> fileList,
        CancellationToken cancellationToken)
    {
        // Validate folder name format
        if (!IsValidFolderName(folderName))
        {
            _logger.LogDebug("Invalid folder name format: {FolderName}", folderName);
            return null;
        }

        if (fileList.Count == 0)
            return null;

        // Tìm base name từ file .hash
        var hashFile = fileList.FirstOrDefault(f => f.Name.EndsWith(".hash", StringComparison.OrdinalIgnoreCase));
        if (hashFile == null)
            return null;

        var baseName = Path.GetFileNameWithoutExtension(hashFile.Name);

        // Validate base name format
        if (!IsValidBaseName(baseName))
        {
            _logger.LogDebug("Invalid base name format: {BaseName}", baseName);
            return null;
        }

        // Kiểm tra các file cần thiết
        var hasWhd = fileList.Any(f => f.Name.Equals($"{baseName}.whd", StringComparison.OrdinalIgnoreCase));
        var hasWcl = fileList.Any(f => f.Name.Equals($"{baseName}.wcl", StringComparison.OrdinalIgnoreCase));
        var wxxFiles = fileList
            .Where(f => f.Name.StartsWith($"{baseName}.w", StringComparison.OrdinalIgnoreCase) &&
                       f.Name.Length == baseName.Length + 4 &&
                       char.IsDigit(f.Name[^2]) &&
                       char.IsDigit(f.Name[^1]))
            .ToList();

        var totalSize = fileList.Sum(f => f.Size);
        var fileCount = fileList.Count;
        var wxxCount = wxxFiles.Count;
        var fileNames = fileList.Select(f => f.Name).ToList();

        return new PackageInfo
        {
            FolderName = folderName,
            BaseName = baseName,
            FullPath = packagePath,
            TotalSize = totalSize,
            FileCount = fileCount,
            HasHashFile = true,
            HasWhdFile = hasWhd,
            HasWclFile = hasWcl,
            WxxFileCount = wxxCount,
            Files = fileNames
        };
    }

    private static bool IsValidFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return ValidFolderNameRegex().IsMatch(name);
    }

    private static bool IsValidBaseName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return ValidFolderNameRegex().IsMatch(name);
    }

    private static (string Server, string Share, string BasePath) ParseUncPath(string uncPath)
    {
        // \\192.168.1.250\share\image -> (192.168.1.250, share, \image)
        var parts = uncPath.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);

        var server = parts.Length > 0 ? parts[0] : string.Empty;
        var share = parts.Length > 1 ? parts[1] : string.Empty;
        var basePath = parts.Length > 2 ? "\\" + string.Join("\\", parts.Skip(2)) : string.Empty;

        return (server, share, basePath);
    }

    private static string CombinePath(string basePath, string name)
    {
        // Ensure proper path combination for SMB
        if (basePath == "\\")
            return "\\" + name;

        return basePath.TrimEnd('\\') + "\\" + name;
    }
}

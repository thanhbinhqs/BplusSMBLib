using System.Runtime.CompilerServices;
using SMBLibrary;
using SMBLibrary.Client;
using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;
using SmbEnterprise.Protocol.SMB.Connection;
using SmbEnterprise.Protocol.SMB.Streams;
using Microsoft.Extensions.Logging;
using IoFileAttributes = System.IO.FileAttributes;
using SmbFileAttr = SMBLibrary.FileAttributes;

namespace SmbEnterprise.Protocol.SMB;

/// <summary>
/// SMB implementation of IRemoteFileSystem.
/// All SMBLibrary calls are contained within this class; nothing leaks upward.
/// </summary>
public sealed class SmbFileSystem : IRemoteFileSystem
{
    private readonly ILogger<SmbFileSystem> _logger;
    private readonly SmbSessionPool _sessionPool;
    private RemoteCredential? _credential;
    private bool _disposed;

    public SmbFileSystem(SmbSessionPool sessionPool, ILogger<SmbFileSystem> logger)
    {
        _sessionPool = sessionPool;
        _logger = logger;
    }

    public async Task ConnectAsync(RemoteCredential credential, CancellationToken cancellationToken = default)
    {
        var opId = Guid.NewGuid().ToString("N")[..8];
        _credential = credential;
        _logger.LogDebug("[{OpId}] ConnectAsync start server={Server} share={Share} user={User}",
            opId, credential.Server, credential.Share, credential.Username);
        // Eagerly test the connection
        await using var session = await _sessionPool.AcquireAsync(credential, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("[{OpId}] SMB filesystem connected to \\\\{Server}\\{Share}", opId, credential.Server, credential.Share);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("DisconnectAsync called for server={Server} share={Share}", _credential?.Server, _credential?.Share);
        _credential = null;
        return Task.CompletedTask;
    }

    public async Task<bool> ExistsAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var opId = Guid.NewGuid().ToString("N")[..8];
        EnsureConnected();
        _logger.LogDebug("[{OpId}] ExistsAsync path={Path}", opId, remotePath);
        await using var pooled = await _sessionPool.AcquireAsync(_credential!, cancellationToken).ConfigureAwait(false);
        var store = pooled.Session.FileStore!;
        var normalized = NormalizePath(remotePath);
        var status = store.CreateFile(out var handle, out _,
            normalized,
            AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
            SmbFileAttr.Normal,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_SYNCHRONOUS_IO_NONALERT,
            null);

        if (status == NTStatus.STATUS_SUCCESS && handle != null)
        {
            store.CloseFile(handle);
            _logger.LogDebug("[{OpId}] ExistsAsync found file path={Path}", opId, remotePath);
            return true;
        }

        if (status == NTStatus.STATUS_OBJECT_NAME_NOT_FOUND ||
            status == NTStatus.STATUS_OBJECT_PATH_NOT_FOUND)
            return false;

        // Try as directory
        status = store.CreateFile(out handle, out _,
            normalized,
            AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
            SmbFileAttr.Directory,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_NONALERT,
            null);

        if (status == NTStatus.STATUS_SUCCESS && handle != null)
        {
            store.CloseFile(handle);
            _logger.LogDebug("[{OpId}] ExistsAsync found directory path={Path}", opId, remotePath);
            return true;
        }

        _logger.LogDebug("[{OpId}] ExistsAsync not found path={Path}", opId, remotePath);
        return false;
    }

    public async IAsyncEnumerable<FileItem> ListDirectoryAsync(
        string remotePath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var opId = Guid.NewGuid().ToString("N")[..8];
        EnsureConnected();
        _logger.LogDebug("[{OpId}] ListDirectoryAsync start path={Path}", opId, remotePath);
        await using var pooled = await _sessionPool.AcquireAsync(_credential!, cancellationToken).ConfigureAwait(false);
        var store = pooled.Session.FileStore!;
        var normalized = NormalizePath(remotePath);

        var status = store.CreateFile(out var dirHandle, out _,
            normalized,
            AccessMask.GENERIC_READ,
            SmbFileAttr.Directory,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE,
            null);

        if (status != NTStatus.STATUS_SUCCESS)
        {
            _logger.LogWarning("[{OpId}] ListDirectoryAsync open failed path={Path} status={Status}", opId, remotePath, status);
            throw new DirectoryNotFoundException($"Directory not found: {remotePath} ({status})");
        }

        try
        {
            List<QueryDirectoryFileInformation>? entries;
            status = store.QueryDirectory(out entries, dirHandle, "*",
                FileInformationClass.FileIdFullDirectoryInformation);

            if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_NO_MORE_FILES)
                throw new IOException($"QueryDirectory failed: {status}");

            if (entries is null) yield break;

            _logger.LogDebug("[{OpId}] ListDirectoryAsync entries={Count} path={Path}", opId, entries.Count, remotePath);

            foreach (var entry in entries)
            {
                if (entry is FileIdFullDirectoryInformation fi)
                {
                    if (fi.FileName == "." || fi.FileName == "..") continue;
                    var isDir = (fi.FileAttributes & SmbFileAttr.Directory) != 0;
                    yield return new FileItem
                    {
                        Name = fi.FileName,
                        FullPath = remotePath.TrimEnd('\\') + "\\" + fi.FileName,
                        IsDirectory = isDir,
                        Size = fi.EndOfFile,
                        CreatedUtc = fi.CreationTime.ToUniversalTime(),
                        ModifiedUtc = fi.LastWriteTime.ToUniversalTime(),
                        Attributes = (IoFileAttributes)fi.FileAttributes
                    };
                }
            }
        }
        finally
        {
            store.CloseFile(dirHandle);
            _logger.LogDebug("[{OpId}] ListDirectoryAsync close directory handle path={Path}", opId, remotePath);
        }
    }

    public async Task<FileMetadata> GetMetadataAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var opId = Guid.NewGuid().ToString("N")[..8];
        EnsureConnected();
        _logger.LogDebug("[{OpId}] GetMetadataAsync path={Path}", opId, remotePath);
        await using var pooled = await _sessionPool.AcquireAsync(_credential!, cancellationToken).ConfigureAwait(false);
        var store = pooled.Session.FileStore!;
        var normalized = NormalizePath(remotePath);

        var status = store.CreateFile(out var handle, out _,
            normalized,
            AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
            SmbFileAttr.Normal,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_SYNCHRONOUS_IO_NONALERT,
            null);

        if (status != NTStatus.STATUS_SUCCESS)
        {
            _logger.LogWarning("[{OpId}] GetMetadataAsync open failed path={Path} status={Status}", opId, remotePath, status);
            throw new FileNotFoundException($"File not found: {remotePath} ({status})");
        }

        try
        {
            status = store.GetFileInformation(out var fileInfo, handle,
                FileInformationClass.FileAllInformation);

            if (status != NTStatus.STATUS_SUCCESS || fileInfo is not FileAllInformation all)
                throw new IOException($"GetFileInformation failed: {status}");

            var isDir = (all.BasicInformation.FileAttributes & SmbFileAttr.Directory) != 0;
            return new FileMetadata
            {
                FullPath = remotePath,
                Size = all.StandardInformation.EndOfFile,
                CreatedUtc = (all.BasicInformation.CreationTime.Time ?? DateTime.UtcNow).ToUniversalTime(),
                ModifiedUtc = (all.BasicInformation.LastWriteTime.Time ?? DateTime.UtcNow).ToUniversalTime(),
                AccessedUtc = (all.BasicInformation.LastAccessTime.Time ?? DateTime.UtcNow).ToUniversalTime(),
                Attributes = (IoFileAttributes)all.BasicInformation.FileAttributes,
                IsDirectory = isDir
            };
        }
        finally
        {
            store.CloseFile(handle);
            _logger.LogDebug("[{OpId}] GetMetadataAsync close handle path={Path}", opId, remotePath);
        }
    }

    public async Task<IRemoteReadStream> OpenReadAsync(string remotePath, long offset = 0, CancellationToken cancellationToken = default)
    {
        var opId = Guid.NewGuid().ToString("N")[..8];
        EnsureConnected();
        var pooled = await _sessionPool.AcquireAsync(_credential!, cancellationToken).ConfigureAwait(false);
        var store = pooled.Session.FileStore!;
        var normalized = NormalizePath(remotePath);

        _logger.LogDebug("[{OpId}] OpenReadAsync path={Path} offset={Offset}", opId, remotePath, offset);

        var status = store.CreateFile(out var handle, out _,
            normalized,
            AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
            SmbFileAttr.Normal,
            ShareAccess.Read,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_SYNCHRONOUS_IO_NONALERT | CreateOptions.FILE_NON_DIRECTORY_FILE,
            null);

        if (status != NTStatus.STATUS_SUCCESS)
        {
            _logger.LogWarning("[{OpId}] OpenReadAsync failed path={Path} status={Status}", opId, remotePath, status);
            await pooled.DisposeAsync().ConfigureAwait(false);
            throw new FileNotFoundException($"Cannot open for read: {remotePath} ({status})");
        }

        // Get file size
        store.GetFileInformation(out var fi, handle, FileInformationClass.FileStandardInformation);
        var size = fi is FileStandardInformation std ? std.EndOfFile : 0;

        var stream = new SmbReadStream(pooled, store, handle, size, _logger);
        if (offset > 0)
            await stream.SeekAsync(offset, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("[{OpId}] OpenReadAsync success path={Path} size={Size}", opId, remotePath, size);
        return stream;
    }

    public async Task<IRemoteWriteStream> OpenWriteAsync(string remotePath, long offset = 0, bool createNew = false, CancellationToken cancellationToken = default)
    {
        var opId = Guid.NewGuid().ToString("N")[..8];
        EnsureConnected();
        var pooled = await _sessionPool.AcquireAsync(_credential!, cancellationToken).ConfigureAwait(false);
        var store = pooled.Session.FileStore!;
        var normalized = NormalizePath(remotePath);

        _logger.LogDebug("[{OpId}] OpenWriteAsync path={Path} offset={Offset} createNew={CreateNew}", opId, remotePath, offset, createNew);

        var disposition = createNew
            ? CreateDisposition.FILE_CREATE
            : (offset > 0 ? CreateDisposition.FILE_OPEN_IF : CreateDisposition.FILE_SUPERSEDE);

        var status = store.CreateFile(out var handle, out _,
            normalized,
            AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE,
            SmbFileAttr.Normal,
            ShareAccess.None,
            disposition,
            CreateOptions.FILE_SYNCHRONOUS_IO_NONALERT | CreateOptions.FILE_NON_DIRECTORY_FILE,
            null);

        if (status != NTStatus.STATUS_SUCCESS)
        {
            _logger.LogWarning("[{OpId}] OpenWriteAsync failed path={Path} status={Status}", opId, remotePath, status);
            await pooled.DisposeAsync().ConfigureAwait(false);
            throw new IOException($"Cannot open for write: {remotePath} ({status})");
        }

        _logger.LogDebug("[{OpId}] OpenWriteAsync success path={Path}", opId, remotePath);
        return new SmbWriteStream(pooled, store, handle, offset, _logger);
    }

    public async Task CreateDirectoryAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var opId = Guid.NewGuid().ToString("N")[..8];
        EnsureConnected();
        _logger.LogDebug("[{OpId}] CreateDirectoryAsync path={Path}", opId, remotePath);
        await using var pooled = await _sessionPool.AcquireAsync(_credential!, cancellationToken).ConfigureAwait(false);
        var store = pooled.Session.FileStore!;
        var normalized = NormalizePath(remotePath);

        var status = store.CreateFile(out var handle, out _,
            normalized,
            AccessMask.GENERIC_READ | AccessMask.GENERIC_WRITE,
            SmbFileAttr.Directory,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN_IF,
            CreateOptions.FILE_DIRECTORY_FILE,
            null);

        if (status != NTStatus.STATUS_SUCCESS)
            throw new IOException($"CreateDirectory failed: {remotePath} ({status})");

        store.CloseFile(handle);
        _logger.LogDebug("[{OpId}] CreateDirectoryAsync success path={Path}", opId, remotePath);
    }

    public async Task DeleteFileAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var opId = Guid.NewGuid().ToString("N")[..8];
        EnsureConnected();
        _logger.LogDebug("[{OpId}] DeleteFileAsync path={Path}", opId, remotePath);
        await using var pooled = await _sessionPool.AcquireAsync(_credential!, cancellationToken).ConfigureAwait(false);
        var store = pooled.Session.FileStore!;
        var normalized = NormalizePath(remotePath);

        var status = store.CreateFile(out var handle, out _,
            normalized,
            AccessMask.GENERIC_WRITE | AccessMask.DELETE,
            SmbFileAttr.Normal,
            ShareAccess.None,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_DELETE_ON_CLOSE,
            null);

        if (status != NTStatus.STATUS_SUCCESS)
            throw new FileNotFoundException($"DeleteFile failed: {remotePath} ({status})");

        store.CloseFile(handle);
        _logger.LogDebug("[{OpId}] DeleteFileAsync success path={Path}", opId, remotePath);
    }

    public async Task DeleteDirectoryAsync(string remotePath, bool recursive, CancellationToken cancellationToken = default)
    {
        var opId = Guid.NewGuid().ToString("N")[..8];
        EnsureConnected();
        _logger.LogDebug("[{OpId}] DeleteDirectoryAsync path={Path} recursive={Recursive}", opId, remotePath, recursive);

        if (recursive)
        {
            var children = new List<FileItem>();
            await foreach (var child in ListDirectoryAsync(remotePath, cancellationToken).ConfigureAwait(false))
            {
                children.Add(child);
            }

            foreach (var child in children)
            {
                if (child.IsDirectory)
                {
                    await DeleteDirectoryAsync(child.FullPath, true, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await DeleteFileAsync(child.FullPath, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        await using var pooled = await _sessionPool.AcquireAsync(_credential!, cancellationToken).ConfigureAwait(false);
        var store = pooled.Session.FileStore!;
        var normalized = NormalizePath(remotePath);

        var status = store.CreateFile(out var handle, out _,
            normalized,
            AccessMask.DELETE | AccessMask.SYNCHRONIZE,
            SmbFileAttr.Directory,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE | CreateOptions.FILE_DELETE_ON_CLOSE,
            null);

        if (status != NTStatus.STATUS_SUCCESS)
            throw new IOException($"DeleteDirectory failed: {remotePath} ({status})");

        store.CloseFile(handle);
        _logger.LogDebug("[{OpId}] DeleteDirectoryAsync success path={Path}", opId, remotePath);
    }

    public async Task RenameAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        var opId = Guid.NewGuid().ToString("N")[..8];
        EnsureConnected();
        _logger.LogDebug("[{OpId}] RenameAsync source={Source} destination={Destination}", opId, sourcePath, destinationPath);
        await using var pooled = await _sessionPool.AcquireAsync(_credential!, cancellationToken).ConfigureAwait(false);
        var store = pooled.Session.FileStore!;
        var normSrc = NormalizePath(sourcePath);

        var status = store.CreateFile(out var handle, out _,
            normSrc,
            AccessMask.GENERIC_WRITE | AccessMask.DELETE,
            SmbFileAttr.Normal,
            ShareAccess.None,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_NON_DIRECTORY_FILE,
            null);

        if (status != NTStatus.STATUS_SUCCESS)
        {
            status = store.CreateFile(out handle, out _,
                normSrc,
                AccessMask.GENERIC_WRITE | AccessMask.DELETE,
                SmbFileAttr.Directory,
                ShareAccess.Read | ShareAccess.Write,
                CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_DIRECTORY_FILE,
                null);
        }

        if (status != NTStatus.STATUS_SUCCESS || handle is null)
            throw new FileNotFoundException($"RenameAsync source not found: {sourcePath} ({status})");

        try
        {
            var renameInfo = new FileRenameInformationType2
            {
                FileName = NormalizePath(destinationPath),
                ReplaceIfExists = false
            };
            status = store.SetFileInformation(handle, renameInfo);
            if (status != NTStatus.STATUS_SUCCESS)
                throw new IOException($"RenameAsync failed: {status}");

            _logger.LogDebug("[{OpId}] RenameAsync success source={Source} destination={Destination}", opId, sourcePath, destinationPath);
        }
        finally
        {
            store.CloseFile(handle);
        }
    }

    public async Task SetAttributesAsync(string remotePath, FileMetadata metadata, CancellationToken cancellationToken = default)
    {
        var opId = Guid.NewGuid().ToString("N")[..8];
        EnsureConnected();
        _logger.LogDebug("[{OpId}] SetAttributesAsync path={Path}", opId, remotePath);
        await using var pooled = await _sessionPool.AcquireAsync(_credential!, cancellationToken).ConfigureAwait(false);
        var store = pooled.Session.FileStore!;
        var normalized = NormalizePath(remotePath);

        var status = store.CreateFile(out var handle, out _,
            normalized,
            AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE,
            SmbFileAttr.Normal,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_SYNCHRONOUS_IO_NONALERT,
            null);

        if (status != NTStatus.STATUS_SUCCESS)
            throw new FileNotFoundException($"SetAttributes: file not found: {remotePath}");

        try
        {
            var basicInfo = new FileBasicInformation
            {
                FileAttributes = (SmbFileAttr)metadata.Attributes,
                LastWriteTime = metadata.ModifiedUtc,
                CreationTime = metadata.CreatedUtc,
                LastAccessTime = metadata.AccessedUtc
            };
            store.SetFileInformation(handle, basicInfo);
            _logger.LogDebug("[{OpId}] SetAttributesAsync success path={Path}", opId, remotePath);
        }
        finally
        {
            store.CloseFile(handle);
        }
    }

    private static string NormalizePath(string path)
    {
        // SMBLibrary expects paths without leading backslash
        return path.Replace('/', '\\').TrimStart('\\');
    }

    private void EnsureConnected()
    {
        if (_credential is null)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _logger.LogDebug("Disposing SmbFileSystem and session pool");
        await _sessionPool.DisposeAsync().ConfigureAwait(false);
    }
}

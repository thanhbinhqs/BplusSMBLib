using SmbEnterprise.Core.Models;

namespace SmbEnterprise.Core.Abstractions;

/// <summary>
/// Abstraction for a remote (or local) file system provider.
/// SMBLibrary must NOT leak through this interface.
/// </summary>
public interface IRemoteFileSystem : IAsyncDisposable
{
    /// <summary>Connect / authenticate to the remote share.</summary>
    Task ConnectAsync(RemoteCredential credential, CancellationToken cancellationToken = default);

    /// <summary>Disconnect and release the session.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns true if the path exists (file or directory).</summary>
    Task<bool> ExistsAsync(string remotePath, CancellationToken cancellationToken = default);

    /// <summary>List all entries in a directory.</summary>
    IAsyncEnumerable<FileItem> ListDirectoryAsync(string remotePath, CancellationToken cancellationToken = default);

    /// <summary>Get metadata about a single file or directory entry.</summary>
    Task<FileMetadata> GetMetadataAsync(string remotePath, CancellationToken cancellationToken = default);

    /// <summary>Open a readable stream for the remote file.</summary>
    Task<IRemoteReadStream> OpenReadAsync(string remotePath, long offset = 0, CancellationToken cancellationToken = default);

    /// <summary>Open a writable stream for the remote file.</summary>
    Task<IRemoteWriteStream> OpenWriteAsync(string remotePath, long offset = 0, bool createNew = false, CancellationToken cancellationToken = default);

    /// <summary>Create a directory (including all intermediary directories).</summary>
    Task CreateDirectoryAsync(string remotePath, CancellationToken cancellationToken = default);

    /// <summary>Delete a file.</summary>
    Task DeleteFileAsync(string remotePath, CancellationToken cancellationToken = default);

    /// <summary>Delete a directory recursively.</summary>
    Task DeleteDirectoryAsync(string remotePath, bool recursive, CancellationToken cancellationToken = default);

    /// <summary>Rename / move a remote path.</summary>
    Task RenameAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>Set file attributes (timestamps, readonly, etc.).</summary>
    Task SetAttributesAsync(string remotePath, FileMetadata metadata, CancellationToken cancellationToken = default);
}

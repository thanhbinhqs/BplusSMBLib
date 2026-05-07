using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;

namespace SmbEnterprise.WinFormsApp.Services;

public sealed class LocalFileSystem : IRemoteFileSystem
{
    public Task ConnectAsync(RemoteCredential credential, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> ExistsAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var path = NormalizePath(remotePath);
        return Task.FromResult(File.Exists(path) || Directory.Exists(path));
    }

    public async IAsyncEnumerable<FileItem> ListDirectoryAsync(string remotePath, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var path = NormalizePath(remotePath);

        if (string.IsNullOrWhiteSpace(path) || path == "\\")
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new FileItem
                {
                    Name = drive.Name,
                    FullPath = drive.RootDirectory.FullName,
                    IsDirectory = true,
                    Size = 0,
                    CreatedUtc = DateTime.UtcNow,
                    ModifiedUtc = DateTime.UtcNow,
                    Attributes = FileAttributes.Directory
                };
            }

            yield break;
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var isDir = Directory.Exists(entry);
            var attr = File.GetAttributes(entry);
            var created = File.GetCreationTimeUtc(entry);
            var modified = File.GetLastWriteTimeUtc(entry);
            var size = isDir ? 0L : new FileInfo(entry).Length;

            yield return new FileItem
            {
                Name = Path.GetFileName(entry),
                FullPath = entry,
                IsDirectory = isDir,
                Size = size,
                CreatedUtc = created,
                ModifiedUtc = modified,
                Attributes = attr
            };
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public Task<FileMetadata> GetMetadataAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var path = NormalizePath(remotePath);

        if (File.Exists(path))
        {
            var fi = new FileInfo(path);
            return Task.FromResult(new FileMetadata
            {
                FullPath = fi.FullName,
                Size = fi.Length,
                CreatedUtc = fi.CreationTimeUtc,
                ModifiedUtc = fi.LastWriteTimeUtc,
                AccessedUtc = fi.LastAccessTimeUtc,
                Attributes = fi.Attributes,
                IsDirectory = false
            });
        }

        if (Directory.Exists(path))
        {
            var di = new DirectoryInfo(path);
            return Task.FromResult(new FileMetadata
            {
                FullPath = di.FullName,
                Size = 0,
                CreatedUtc = di.CreationTimeUtc,
                ModifiedUtc = di.LastWriteTimeUtc,
                AccessedUtc = di.LastAccessTimeUtc,
                Attributes = di.Attributes,
                IsDirectory = true
            });
        }

        throw new FileNotFoundException($"Path not found: {path}");
    }

    public Task<IRemoteReadStream> OpenReadAsync(string remotePath, long offset = 0, CancellationToken cancellationToken = default)
    {
        var path = NormalizePath(remotePath);
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (offset > 0)
        {
            fs.Seek(offset, SeekOrigin.Begin);
        }

        return Task.FromResult<IRemoteReadStream>(new LocalReadStream(fs));
    }

    public Task<IRemoteWriteStream> OpenWriteAsync(string remotePath, long offset = 0, bool createNew = false, CancellationToken cancellationToken = default)
    {
        var path = NormalizePath(remotePath);
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var mode = createNew
            ? FileMode.Create
            : (offset > 0 ? FileMode.OpenOrCreate : FileMode.Create);

        var fs = new FileStream(path, mode, FileAccess.Write, FileShare.None, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (offset > 0)
        {
            fs.Seek(offset, SeekOrigin.Begin);
        }

        return Task.FromResult<IRemoteWriteStream>(new LocalWriteStream(fs));
    }

    public Task CreateDirectoryAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var path = NormalizePath(remotePath);
        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var path = NormalizePath(remotePath);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string remotePath, bool recursive, CancellationToken cancellationToken = default)
    {
        var path = NormalizePath(remotePath);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive);
        }

        return Task.CompletedTask;
    }

    public Task RenameAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        var src = NormalizePath(sourcePath);
        var dest = NormalizePath(destinationPath);

        if (File.Exists(src))
        {
            var parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Move(src, dest);
            return Task.CompletedTask;
        }

        if (Directory.Exists(src))
        {
            Directory.Move(src, dest);
            return Task.CompletedTask;
        }

        throw new FileNotFoundException($"Path not found: {src}");
    }

    public Task SetAttributesAsync(string remotePath, FileMetadata metadata, CancellationToken cancellationToken = default)
    {
        var path = NormalizePath(remotePath);
        File.SetAttributes(path, metadata.Attributes);

        if (!metadata.IsDirectory)
        {
            File.SetCreationTimeUtc(path, metadata.CreatedUtc);
            File.SetLastWriteTimeUtc(path, metadata.ModifiedUtc);
            File.SetLastAccessTimeUtc(path, metadata.AccessedUtc);
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static string NormalizePath(string path)
    {
        return path.Replace('/', '\\').Trim();
    }
}

internal sealed class LocalReadStream : IRemoteReadStream
{
    private readonly FileStream _stream;

    public LocalReadStream(FileStream stream)
    {
        _stream = stream;
    }

    public long Length => _stream.Length;
    public long Position => _stream.Position;
    public bool CanSeek => _stream.CanSeek;

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => _stream.ReadAsync(buffer, cancellationToken);

    public Task SeekAsync(long offset, CancellationToken cancellationToken = default)
    {
        _stream.Seek(offset, SeekOrigin.Begin);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => _stream.DisposeAsync();
}

internal sealed class LocalWriteStream : IRemoteWriteStream
{
    private readonly FileStream _stream;

    public LocalWriteStream(FileStream stream)
    {
        _stream = stream;
    }

    public long Position => _stream.Position;

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => _stream.WriteAsync(buffer, cancellationToken);

    public Task FlushAsync(CancellationToken cancellationToken = default)
        => _stream.FlushAsync(cancellationToken);

    public ValueTask DisposeAsync() => _stream.DisposeAsync();
}

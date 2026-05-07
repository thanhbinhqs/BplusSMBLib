namespace SmbEnterprise.Core.Models;

/// <summary>Represents a file or directory entry returned from a directory listing.</summary>
public sealed class FileItem
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public FileAttributes Attributes { get; init; }
}

/// <summary>Detailed metadata for a single file.</summary>
public sealed class FileMetadata
{
    public required string FullPath { get; init; }
    public long Size { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public DateTime AccessedUtc { get; init; }
    public FileAttributes Attributes { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsReadOnly => (Attributes & FileAttributes.ReadOnly) != 0;
    public bool IsHidden => (Attributes & FileAttributes.Hidden) != 0;
}

/// <summary>Metadata for a directory (recursive summary).</summary>
public sealed class DirectoryMetadata
{
    public required string FullPath { get; init; }
    public long TotalBytes { get; init; }
    public long FileCount { get; init; }
    public long DirectoryCount { get; init; }
    public DateTime ModifiedUtc { get; init; }
}

/// <summary>Credentials for connecting to a remote share.</summary>
public sealed class RemoteCredential
{
    public required string Server { get; init; }
    public required string Share { get; init; }
    public string? Username { get; init; }
    public string? Domain { get; init; }
    public string? Password { get; init; }
    public int Port { get; init; } = 445;

    /// <summary>True if using anonymous / guest access.</summary>
    public bool IsAnonymous => string.IsNullOrEmpty(Username);
}

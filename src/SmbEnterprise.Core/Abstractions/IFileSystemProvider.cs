namespace SmbEnterprise.Core.Abstractions;

/// <summary>
/// Factory that creates IRemoteFileSystem instances for a given provider type.
/// Enables provider-agnostic architecture (SMB, SFTP, local, cloud).
/// </summary>
public interface IFileSystemProvider
{
    string ProviderName { get; }

    IRemoteFileSystem CreateFileSystem();
}

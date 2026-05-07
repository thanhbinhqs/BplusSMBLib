using SmbEnterprise.Core.Models;

namespace SmbEnterprise.Transfer.Abstractions;

/// <summary>
/// Main entry point for initiating file transfers.
/// Upper layers only depend on this interface.
/// </summary>
public interface ITransferEngine
{
    /// <summary>Transfer a single file from source to one destination.</summary>
    Task<TransferResult> TransferAsync(
        string sourcePath,
        string destinationPath,
        TransferOptions options,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Transfer a single file to multiple destinations simultaneously.</summary>
    Task<MultiDestinationTransferResult> TransferMultiDestinationAsync(
        string sourcePath,
        IReadOnlyList<string> destinationPaths,
        TransferOptions options,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Transfer all files in a directory recursively.</summary>
    Task<TransferResult> TransferDirectoryAsync(
        string sourceDirectory,
        string destinationDirectory,
        TransferOptions options,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

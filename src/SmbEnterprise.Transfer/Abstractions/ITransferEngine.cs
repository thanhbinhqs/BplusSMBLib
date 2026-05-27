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

    /// <summary>
    /// Transfer một file đến nhiều destinations với progress tracking riêng cho từng destination.
    /// Hỗ trợ xử lý slow connection để tránh ảnh hưởng đến các destination khác.
    /// </summary>
    Task<MultiDestinationTransferResult> TransferMultiDestinationWithIndividualProgressAsync(
        string sourcePath,
        IReadOnlyList<string> destinationPaths,
        TransferOptions options,
        SlowConnectionPolicy? slowPolicy = null,
        IProgress<AggregatedMultiDestinationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transfer toàn bộ directory đến nhiều destinations với progress tracking riêng.
    /// Các file được transfer song song tới tất cả destinations.
    /// </summary>
    Task<DirectoryMultiDestinationTransferResult> TransferDirectoryMultiDestinationAsync(
        string sourceDirectory,
        IReadOnlyList<string> destinationDirectories,
        TransferOptions options,
        SlowConnectionPolicy? slowPolicy = null,
        IProgress<DirectoryTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

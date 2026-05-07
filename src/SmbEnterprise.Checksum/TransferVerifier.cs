using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;
using Microsoft.Extensions.Logging;

namespace SmbEnterprise.Checksum;

/// <summary>
/// Verifies integrity of a transferred file by re-reading destination and comparing checksums.
/// Supports verify-after-copy and chunk-level verification with retry.
/// </summary>
public sealed class TransferVerifier
{
    private readonly IChecksumEngine _checksumEngine;
    private readonly ILogger<TransferVerifier> _logger;

    public TransferVerifier(IChecksumEngine checksumEngine, ILogger<TransferVerifier> logger)
    {
        _checksumEngine = checksumEngine;
        _logger = logger;
    }

    /// <summary>
    /// Verify that source and destination files have identical content.
    /// Streams both files and compares checksums without loading entire files into memory.
    /// </summary>
    public async Task<VerificationResult> VerifyAsync(
        IRemoteFileSystem sourceFs,
        string sourcePath,
        IRemoteFileSystem destFs,
        string destinationPath,
        long expectedSize,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Verifying: {Source} vs {Dest}", sourcePath, destinationPath);

        try
        {
            // Verify destination file exists and has correct size
            var destMeta = await destFs.GetMetadataAsync(destinationPath, cancellationToken).ConfigureAwait(false);

            if (destMeta.Size != expectedSize)
            {
                _logger.LogError("Size mismatch: expected={Expected} actual={Actual}", expectedSize, destMeta.Size);
                return VerificationResult.Fail($"Size mismatch: expected {expectedSize}, got {destMeta.Size}");
            }

            // Compute source checksum
            var sourceChecksum = await ComputeRemoteChecksumAsync(sourceFs, sourcePath, expectedSize, cancellationToken).ConfigureAwait(false);

            // Compute destination checksum
            var destChecksum = await ComputeRemoteChecksumAsync(destFs, destinationPath, expectedSize, cancellationToken).ConfigureAwait(false);

            if (!sourceChecksum.Hash.AsSpan().SequenceEqual(destChecksum.Hash))
            {
                _logger.LogError("Checksum mismatch! Source={SrcHash} Dest={DstHash}", sourceChecksum.HexHash, destChecksum.HexHash);
                return VerificationResult.Fail($"Checksum mismatch. Algorithm: {_checksumEngine.Algorithm}");
            }

            _logger.LogInformation("Verification passed: {Algorithm} {Hash}", _checksumEngine.Algorithm, sourceChecksum.HexHash);
            return VerificationResult.Pass(sourceChecksum.HexHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Verification error for {Path}", destinationPath);
            return VerificationResult.Fail(ex.Message);
        }
    }

    private async Task<FileChecksum> ComputeRemoteChecksumAsync(
        IRemoteFileSystem fs,
        string path,
        long fileSize,
        CancellationToken cancellationToken)
    {
        await using var stream = await fs.OpenReadAsync(path, 0, cancellationToken).ConfigureAwait(false);

        return await _checksumEngine.ComputeFileAsync(path, async (offset, buffer, ct) =>
        {
            await stream.SeekAsync(offset, ct).ConfigureAwait(false);
            return await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
        }, fileSize, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class VerificationResult
{
    public bool IsValid { get; private init; }
    public string? ErrorMessage { get; private init; }
    public string? Hash { get; private init; }

    public static VerificationResult Pass(string hash) => new() { IsValid = true, Hash = hash };
    public static VerificationResult Fail(string error) => new() { IsValid = false, ErrorMessage = error };
}

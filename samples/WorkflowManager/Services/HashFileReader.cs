using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace WorkflowManager.Services;

/// <summary>
/// Service để đọc và parse file .hash
/// </summary>
public sealed partial class HashFileReader
{
    private readonly ILogger<HashFileReader> _logger;

    private static readonly Regex _hashLineRegex = new Regex(@"^(\w+)=([a-fA-F0-9]{32})$", RegexOptions.Compiled);

    public HashFileReader(ILogger<HashFileReader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Đọc file .hash và trả về dictionary của checksums
    /// </summary>
    public async Task<Dictionary<string, string>> ReadHashFileAsync(
        string hashFilePath,
        CancellationToken cancellationToken = default)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!File.Exists(hashFilePath))
            {
                _logger.LogWarning("Hash file not found: {Path}", hashFilePath);
                return hashes;
            }

            var lines = await File.ReadAllLinesAsync(hashFilePath, cancellationToken);
            var inHashSection = false;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // Check for [HASH] section
                if (trimmed.Equals("[HASH]", StringComparison.OrdinalIgnoreCase))
                {
                    inHashSection = true;
                    continue;
                }

                // Skip empty lines and comments
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith(';'))
                    continue;

                // Parse hash lines
                if (inHashSection)
                {
                    var match = _hashLineRegex.Match(trimmed);
                    if (match.Success)
                    {
                        var key = match.Groups[1].Value;
                        var hash = match.Groups[2].Value.ToUpperInvariant();
                        hashes[key] = hash;
                        _logger.LogDebug("Parsed hash: {Key} = {Hash}", key, hash);
                    }
                }
            }

            _logger.LogInformation("Read {Count} hashes from {Path}", hashes.Count, hashFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading hash file: {Path}", hashFilePath);
            throw;
        }

        return hashes;
    }

    /// <summary>
    /// Tính MD5 hash của một file
    /// </summary>
    public async Task<string> ComputeMd5Async(
        string filePath,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                md5.TransformBlock(buffer, 0, bytesRead, null, 0);
                totalRead += bytesRead;
                progress?.Report(totalRead);
            }

            md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            var hash = BitConverter.ToString(md5.Hash!).Replace("-", "");

            return hash;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing MD5 for file: {Path}", filePath);
            throw;
        }
    }
}

using System.Text;
using System.Text.RegularExpressions;

namespace SmbEnterprise.Core.Paths;

/// <summary>
/// Represents a validated, normalized remote path.
/// Supports UNC paths (\\server\share\...) and relative share paths (share/folder/file).
/// Immutable value type.
/// </summary>
public sealed class SmbPath : IEquatable<SmbPath>
{
    private static readonly char[] InvalidChars = ['<', '>', '"', '|', '?', '*', '\0'];
    private static readonly Regex UncRegex = new(@"^\\\\(?<server>[^\\]+)\\(?<share>[^\\]+)(?<path>(\\[^\\]*)*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Server { get; }
    public string Share { get; }
    public string RelativePath { get; }
    public bool IsRoot => string.IsNullOrEmpty(RelativePath) || RelativePath == "\\";

    private SmbPath(string server, string share, string relativePath)
    {
        Server = server;
        Share = share;
        RelativePath = NormalizeSeparators(relativePath);
    }

    /// <summary>Parse a UNC path like \\server\share\folder\file.txt</summary>
    public static SmbPath Parse(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be null or empty.", nameof(path));

        // Try UNC format first: \\server\share\...
        var match = UncRegex.Match(path);
        if (match.Success)
        {
            var server = match.Groups["server"].Value;
            var share = match.Groups["share"].Value;
            var rel = match.Groups["path"].Value;
            Validate(rel);
            return new SmbPath(server, share, rel);
        }

        // Try relative format: share/folder/file.txt (server must be supplied separately)
        var normalized = path.Replace('/', '\\').TrimStart('\\');
        var parts = normalized.Split('\\', 2);
        if (parts.Length >= 1)
        {
            var share = parts[0];
            var rel = parts.Length > 1 ? "\\" + parts[1] : string.Empty;
            Validate(rel);
            return new SmbPath(string.Empty, share, rel);
        }

        throw new FormatException($"Cannot parse SMB path: '{path}'");
    }

    /// <summary>Create a path explicitly from components.</summary>
    public static SmbPath From(string server, string share, string relativePath = "")
    {
        if (string.IsNullOrWhiteSpace(share))
            throw new ArgumentException("Share cannot be empty.", nameof(share));
        Validate(relativePath);
        return new SmbPath(server, share, relativePath);
    }

    /// <summary>Combine this path with a relative segment.</summary>
    public SmbPath Combine(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return this;

        var norm = NormalizeSeparators(segment);
        var combined = RelativePath.TrimEnd('\\') + "\\" + norm.TrimStart('\\');
        return new SmbPath(Server, Share, combined);
    }

    /// <summary>Get the parent directory path.</summary>
    public SmbPath? Parent()
    {
        if (IsRoot) return null;
        var lastSep = RelativePath.TrimEnd('\\').LastIndexOf('\\');
        var parent = lastSep <= 0 ? string.Empty : RelativePath[..lastSep];
        return new SmbPath(Server, Share, parent);
    }

    /// <summary>File/directory name without path.</summary>
    public string FileName
    {
        get
        {
            var trimmed = RelativePath.TrimEnd('\\');
            var lastSep = trimmed.LastIndexOf('\\');
            return lastSep < 0 ? trimmed : trimmed[(lastSep + 1)..];
        }
    }

    /// <summary>UNC representation: \\server\share\relative</summary>
    public string ToUncPath()
    {
        if (string.IsNullOrEmpty(Server))
            return $"\\\\?\\{Share}{RelativePath}";
        return $"\\\\{Server}\\{Share}{RelativePath}";
    }

    public override string ToString() => ToUncPath();

    public bool Equals(SmbPath? other)
    {
        if (other is null) return false;
        return string.Equals(Server, other.Server, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Share, other.Share, StringComparison.OrdinalIgnoreCase)
            && string.Equals(RelativePath, other.RelativePath, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => obj is SmbPath p && Equals(p);
    public override int GetHashCode() => HashCode.Combine(
        Server.ToUpperInvariant(),
        Share.ToUpperInvariant(),
        RelativePath.ToUpperInvariant());

    private static string NormalizeSeparators(string path) =>
        path.Replace('/', '\\');

    private static void Validate(string path)
    {
        foreach (var ch in InvalidChars)
        {
            if (path.Contains(ch, StringComparison.Ordinal))
                throw new ArgumentException($"Path contains invalid character '{ch}': '{path}'");
        }
    }
}

namespace WorkflowManager.Services;

/// <summary>
/// Progress information for package scanning
/// </summary>
public sealed class ScanProgress
{
    public required string Status { get; init; }
    public int ScannedDirectories { get; init; }
    public int FoundPackages { get; init; }
    public int CurrentDepth { get; init; }
}

namespace DHCP.Core.Actions;

/// <summary>
/// Represents a remote command/action to be executed on a client
/// Used for pushing commands to clients in Windows Audit Mode
/// </summary>
public sealed class ClientActionPayload
{
    /// <summary>
    /// Unique identifier for this action
    /// </summary>
    public Guid ActionId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Type/category of command (e.g., "SCRIPT", "CONFIG", "INSTALL", "REBOOT")
    /// </summary>
    public string CommandType { get; init; } = string.Empty;

    /// <summary>
    /// Command payload data (JSON, script content, or other format)
    /// </summary>
    public string Payload { get; init; } = string.Empty;

    /// <summary>
    /// Target client MAC address
    /// </summary>
    public string? TargetMacAddress { get; init; }

    /// <summary>
    /// Target client IP address
    /// </summary>
    public string? TargetIpAddress { get; init; }

    /// <summary>
    /// Timestamp when action was created
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when action should expire
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    /// Priority level (higher = more urgent)
    /// </summary>
    public int Priority { get; init; } = 0;

    /// <summary>
    /// Additional metadata (as JSON)
    /// </summary>
    public string? Metadata { get; init; }

    /// <summary>
    /// Check if action is expired
    /// </summary>
    public bool IsExpired()
    {
        return ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;
    }

    public override string ToString()
    {
        return $"{CommandType} [{ActionId}] -> {TargetIpAddress ?? TargetMacAddress ?? "Broadcast"}";
    }
}

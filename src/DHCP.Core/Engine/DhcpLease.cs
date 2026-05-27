using System.Net;

namespace DHCP.Core.Engine;

/// <summary>
/// Represents a DHCP lease assignment
/// </summary>
public sealed class DhcpLease
{
    /// <summary>
    /// Client MAC address (hardware address)
    /// </summary>
    public string MacAddress { get; init; } = string.Empty;

    /// <summary>
    /// Assigned IP address
    /// </summary>
    public IPAddress IpAddress { get; init; } = IPAddress.Any;

    /// <summary>
    /// Client hostname (if provided)
    /// </summary>
    public string? Hostname { get; init; }

    /// <summary>
    /// Lease start time (UTC)
    /// </summary>
    public DateTime LeaseStartTime { get; init; }

    /// <summary>
    /// Lease expiry time (UTC)
    /// </summary>
    public DateTime ExpiryTime { get; init; }

    /// <summary>
    /// Indicates if this is a static binding (reserved IP)
    /// </summary>
    public bool IsStatic { get; init; }

    /// <summary>
    /// Transaction ID of the last request
    /// </summary>
    public uint LastTransactionId { get; init; }

    /// <summary>
    /// Check if lease is expired
    /// </summary>
    public bool IsExpired()
    {
        return !IsStatic && DateTime.UtcNow >= ExpiryTime;
    }

    /// <summary>
    /// Get remaining lease time in seconds
    /// </summary>
    public uint GetRemainingLeaseTime()
    {
        if (IsStatic)
            return uint.MaxValue;

        var remaining = ExpiryTime - DateTime.UtcNow;
        return remaining.TotalSeconds > 0 ? (uint)remaining.TotalSeconds : 0;
    }

    /// <summary>
    /// Create a new lease with updated expiry time
    /// </summary>
    public DhcpLease Renew(uint leaseTimeSeconds)
    {
        return new DhcpLease
        {
            MacAddress = MacAddress,
            IpAddress = IpAddress,
            Hostname = Hostname,
            LeaseStartTime = DateTime.UtcNow,
            ExpiryTime = DateTime.UtcNow.AddSeconds(leaseTimeSeconds),
            IsStatic = IsStatic,
            LastTransactionId = LastTransactionId
        };
    }

    public override string ToString()
    {
        var status = IsStatic ? "Static" : IsExpired() ? "Expired" : "Active";
        return $"{IpAddress} -> {MacAddress} ({status})";
    }
}

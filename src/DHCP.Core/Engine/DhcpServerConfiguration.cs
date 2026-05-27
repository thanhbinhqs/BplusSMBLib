using System.Net;

namespace DHCP.Core.Engine;

/// <summary>
/// Configuration for DHCP server network settings
/// </summary>
public sealed class DhcpServerConfiguration
{
    /// <summary>
    /// Server IP address (DHCP server identifier)
    /// </summary>
    public IPAddress ServerIpAddress { get; init; } = IPAddress.Any;

    /// <summary>
    /// Subnet mask for the network
    /// </summary>
    public IPAddress SubnetMask { get; init; } = IPAddress.Parse("255.255.255.0");

    /// <summary>
    /// Default gateway (router)
    /// </summary>
    public IPAddress Gateway { get; init; } = IPAddress.Any;

    /// <summary>
    /// DNS servers
    /// </summary>
    public IPAddress[] DnsServers { get; init; } = Array.Empty<IPAddress>();

    /// <summary>
    /// Domain name
    /// </summary>
    public string? DomainName { get; init; }

    /// <summary>
    /// Default lease time in seconds (e.g., 86400 = 24 hours)
    /// </summary>
    public uint DefaultLeaseTime { get; init; } = 86400;

    /// <summary>
    /// Maximum lease time in seconds
    /// </summary>
    public uint MaxLeaseTime { get; init; } = 604800; // 7 days

    /// <summary>
    /// Renewal (T1) time as a percentage of lease time (typically 50%)
    /// </summary>
    public double RenewalTimePercentage { get; init; } = 0.5;

    /// <summary>
    /// Rebinding (T2) time as a percentage of lease time (typically 87.5%)
    /// </summary>
    public double RebindingTimePercentage { get; init; } = 0.875;

    /// <summary>
    /// Broadcast address for the subnet
    /// </summary>
    public IPAddress? BroadcastAddress { get; init; }

    /// <summary>
    /// Calculate renewal time in seconds
    /// </summary>
    public uint GetRenewalTime(uint leaseTime)
    {
        return (uint)(leaseTime * RenewalTimePercentage);
    }

    /// <summary>
    /// Calculate rebinding time in seconds
    /// </summary>
    public uint GetRebindingTime(uint leaseTime)
    {
        return (uint)(leaseTime * RebindingTimePercentage);
    }
}

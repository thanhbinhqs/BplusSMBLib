namespace DHCP.Core.Models;

/// <summary>
/// DHCP Option Codes as defined in RFC 2132
/// </summary>
public enum DhcpOptionCode : byte
{
    /// <summary>
    /// Pad option (0)
    /// </summary>
    Pad = 0,

    /// <summary>
    /// Subnet Mask (1)
    /// </summary>
    SubnetMask = 1,

    /// <summary>
    /// Router / Default Gateway (3)
    /// </summary>
    Router = 3,

    /// <summary>
    /// Domain Name Server (6)
    /// </summary>
    DnsServer = 6,

    /// <summary>
    /// Host Name (12)
    /// </summary>
    HostName = 12,

    /// <summary>
    /// Domain Name (15)
    /// </summary>
    DomainName = 15,

    /// <summary>
    /// Broadcast Address (28)
    /// </summary>
    BroadcastAddress = 28,

    /// <summary>
    /// Requested IP Address (50)
    /// </summary>
    RequestedIpAddress = 50,

    /// <summary>
    /// IP Address Lease Time in seconds (51)
    /// </summary>
    LeaseTime = 51,

    /// <summary>
    /// DHCP Message Type (53)
    /// </summary>
    MessageType = 53,

    /// <summary>
    /// Server Identifier (54)
    /// </summary>
    ServerIdentifier = 54,

    /// <summary>
    /// Parameter Request List (55)
    /// </summary>
    ParameterRequestList = 55,

    /// <summary>
    /// Renewal (T1) Time Value (58)
    /// </summary>
    RenewalTime = 58,

    /// <summary>
    /// Rebinding (T2) Time Value (59)
    /// </summary>
    RebindingTime = 59,

    /// <summary>
    /// Client Identifier (61)
    /// </summary>
    ClientIdentifier = 61,

    /// <summary>
    /// End option (255)
    /// </summary>
    End = 255
}

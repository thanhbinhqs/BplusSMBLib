using System.Net;

namespace DHCP.Core.Models;

/// <summary>
/// Represents a DHCPv4 packet structure as defined in RFC 2131
/// </summary>
public sealed class DhcpPacket
{
    /// <summary>
    /// Message op code / message type (1 = BOOTREQUEST, 2 = BOOTREPLY)
    /// </summary>
    public byte Op { get; init; }

    /// <summary>
    /// Hardware address type (1 = Ethernet)
    /// </summary>
    public byte Htype { get; init; } = 1;

    /// <summary>
    /// Hardware address length (6 for Ethernet MAC)
    /// </summary>
    public byte Hlen { get; init; } = 6;

    /// <summary>
    /// Client sets to zero, optionally used by relay agents
    /// </summary>
    public byte Hops { get; init; }

    /// <summary>
    /// Transaction ID, a random number chosen by the client
    /// </summary>
    public uint Xid { get; init; }

    /// <summary>
    /// Seconds elapsed since client began address acquisition or renewal process
    /// </summary>
    public ushort Secs { get; init; }

    /// <summary>
    /// Flags (bit 0 = broadcast flag)
    /// </summary>
    public ushort Flags { get; init; }

    /// <summary>
    /// Client IP address (only filled if client is in BOUND, RENEW or REBINDING state)
    /// </summary>
    public IPAddress Ciaddr { get; set; } = IPAddress.Any;

    /// <summary>
    /// 'Your' (client) IP address - filled by server
    /// </summary>
    public IPAddress Yiaddr { get; set; } = IPAddress.Any;

    /// <summary>
    /// IP address of next server to use in bootstrap (returned in DHCPOFFER, DHCPACK)
    /// </summary>
    public IPAddress Siaddr { get; set; } = IPAddress.Any;

    /// <summary>
    /// Relay agent IP address
    /// </summary>
    public IPAddress Giaddr { get; init; } = IPAddress.Any;

    /// <summary>
    /// Client hardware address (MAC address - 16 bytes, padded with zeros)
    /// </summary>
    public byte[] Chaddr { get; init; } = new byte[16];

    /// <summary>
    /// Optional server host name (64 bytes)
    /// </summary>
    public byte[] Sname { get; init; } = new byte[64];

    /// <summary>
    /// Boot file name (128 bytes)
    /// </summary>
    public byte[] File { get; init; } = new byte[128];

    /// <summary>
    /// DHCP options
    /// </summary>
    public List<DhcpOption> Options { get; init; } = new();

    /// <summary>
    /// Get the MAC address from Chaddr field
    /// </summary>
    public string GetMacAddress()
    {
        return BitConverter.ToString(Chaddr, 0, Hlen).Replace("-", ":");
    }

    /// <summary>
    /// Get a specific DHCP option by code
    /// </summary>
    public DhcpOption? GetOption(DhcpOptionCode code)
    {
        return Options.FirstOrDefault(o => o.Code == (byte)code);
    }

    /// <summary>
    /// Get the DHCP message type
    /// </summary>
    public DhcpMessageType? GetMessageType()
    {
        var option = GetOption(DhcpOptionCode.MessageType);
        var value = option?.AsByte();
        return value.HasValue ? (DhcpMessageType)value.Value : null;
    }

    /// <summary>
    /// Check if broadcast flag is set
    /// </summary>
    public bool IsBroadcast()
    {
        return (Flags & 0x8000) != 0;
    }

    /// <summary>
    /// Create a new packet with modified options
    /// </summary>
    public DhcpPacket WithOptions(List<DhcpOption> options)
    {
        var newOptions = new List<DhcpOption>(options);
        Options.Clear();
        Options.AddRange(newOptions);
        return this;
    }
}

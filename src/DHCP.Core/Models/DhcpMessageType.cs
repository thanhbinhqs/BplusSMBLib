namespace DHCP.Core.Models;

/// <summary>
/// DHCP Message Types as defined in RFC 2131
/// </summary>
public enum DhcpMessageType : byte
{
    /// <summary>
    /// DHCPDISCOVER - Client broadcast to locate available servers
    /// </summary>
    Discover = 1,

    /// <summary>
    /// DHCPOFFER - Server to client in response to DHCPDISCOVER
    /// </summary>
    Offer = 2,

    /// <summary>
    /// DHCPREQUEST - Client message to servers either requesting offered parameters or confirming correctness
    /// </summary>
    Request = 3,

    /// <summary>
    /// DHCPDECLINE - Client to server indicating network address is already in use
    /// </summary>
    Decline = 4,

    /// <summary>
    /// DHCPACK - Server to client with configuration parameters including committed network address
    /// </summary>
    Ack = 5,

    /// <summary>
    /// DHCPNAK - Server to client refusing request for configuration parameters
    /// </summary>
    Nak = 6,

    /// <summary>
    /// DHCPRELEASE - Client to server relinquishing network address and cancelling remaining lease
    /// </summary>
    Release = 7,

    /// <summary>
    /// DHCPINFORM - Client to server asking for local configuration parameters
    /// </summary>
    Inform = 8
}

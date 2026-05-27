using System.Net;
using System.Text;

namespace DHCP.Core.Models;

/// <summary>
/// Represents a DHCP option as defined in RFC 2132
/// </summary>
public sealed class DhcpOption
{
    /// <summary>
    /// Option code
    /// </summary>
    public byte Code { get; init; }

    /// <summary>
    /// Length of option data
    /// </summary>
    public byte Length { get; init; }

    /// <summary>
    /// Raw option data
    /// </summary>
    public byte[] Data { get; init; }

    public DhcpOption(byte code, byte[] data)
    {
        Code = code;
        Data = data ?? Array.Empty<byte>();
        Length = (byte)Data.Length;
    }

    /// <summary>
    /// Parse option data as a single IP address
    /// </summary>
    public IPAddress? AsIpAddress()
    {
        if (Data.Length != 4)
            return null;

        return new IPAddress(Data);
    }

    /// <summary>
    /// Parse option data as multiple IP addresses
    /// </summary>
    public IPAddress[] AsIpAddresses()
    {
        if (Data.Length % 4 != 0)
            return Array.Empty<IPAddress>();

        var addresses = new IPAddress[Data.Length / 4];
        for (int i = 0; i < addresses.Length; i++)
        {
            var ipBytes = new byte[4];
            Array.Copy(Data, i * 4, ipBytes, 0, 4);
            addresses[i] = new IPAddress(ipBytes);
        }

        return addresses;
    }

    /// <summary>
    /// Parse option data as a 32-bit unsigned integer (big-endian)
    /// </summary>
    public uint? AsUInt32()
    {
        if (Data.Length != 4)
            return null;

        return (uint)(Data[0] << 24 | Data[1] << 16 | Data[2] << 8 | Data[3]);
    }

    /// <summary>
    /// Parse option data as a 16-bit unsigned integer (big-endian)
    /// </summary>
    public ushort? AsUInt16()
    {
        if (Data.Length != 2)
            return null;

        return (ushort)(Data[0] << 8 | Data[1]);
    }

    /// <summary>
    /// Parse option data as a UTF-8 string
    /// </summary>
    public string AsString()
    {
        return Encoding.UTF8.GetString(Data);
    }

    /// <summary>
    /// Parse option data as a single byte
    /// </summary>
    public byte? AsByte()
    {
        if (Data.Length != 1)
            return null;

        return Data[0];
    }

    /// <summary>
    /// Create an option from an IP address
    /// </summary>
    public static DhcpOption FromIpAddress(byte code, IPAddress address)
    {
        return new DhcpOption(code, address.GetAddressBytes());
    }

    /// <summary>
    /// Create an option from multiple IP addresses
    /// </summary>
    public static DhcpOption FromIpAddresses(byte code, params IPAddress[] addresses)
    {
        var data = new byte[addresses.Length * 4];
        for (int i = 0; i < addresses.Length; i++)
        {
            var bytes = addresses[i].GetAddressBytes();
            Array.Copy(bytes, 0, data, i * 4, 4);
        }

        return new DhcpOption(code, data);
    }

    /// <summary>
    /// Create an option from a 32-bit unsigned integer (big-endian)
    /// </summary>
    public static DhcpOption FromUInt32(byte code, uint value)
    {
        var data = new byte[4];
        data[0] = (byte)(value >> 24);
        data[1] = (byte)(value >> 16);
        data[2] = (byte)(value >> 8);
        data[3] = (byte)value;

        return new DhcpOption(code, data);
    }

    /// <summary>
    /// Create an option from a single byte
    /// </summary>
    public static DhcpOption FromByte(byte code, byte value)
    {
        return new DhcpOption(code, new[] { value });
    }

    /// <summary>
    /// Create an option from a string
    /// </summary>
    public static DhcpOption FromString(byte code, string value)
    {
        return new DhcpOption(code, Encoding.UTF8.GetBytes(value));
    }
}

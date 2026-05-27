using System.Buffers.Binary;
using System.Net;
using DHCP.Core.Models;

namespace DHCP.Core.Network;

/// <summary>
/// High-performance DHCP packet parser and serializer
/// Implements RFC 2131 packet structure
/// </summary>
public static class DhcpParser
{
    // DHCP Magic Cookie (99.130.83.99)
    private static readonly byte[] MagicCookie = { 99, 130, 83, 99 };

    // Minimum DHCP packet size (without options)
    private const int MinPacketSize = 236;

    // Fixed header size (before options)
    private const int HeaderSize = 236;

    /// <summary>
    /// Deserialize raw bytes into a DhcpPacket object
    /// </summary>
    /// <param name="data">Raw packet data received from network</param>
    /// <returns>Parsed DHCP packet</returns>
    /// <exception cref="ArgumentException">Thrown when packet data is invalid</exception>
    public static DhcpPacket Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < MinPacketSize)
        {
            throw new ArgumentException($"DHCP packet too small: {data.Length} bytes (minimum {MinPacketSize})");
        }

        // Parse fixed header fields
        var packet = new DhcpPacket
        {
            Op = data[0],
            Htype = data[1],
            Hlen = data[2],
            Hops = data[3],
            Xid = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4)),
            Secs = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(8, 2)),
            Flags = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(10, 2)),
            Ciaddr = new IPAddress(data.Slice(12, 4)),
            Yiaddr = new IPAddress(data.Slice(16, 4)),
            Siaddr = new IPAddress(data.Slice(20, 4)),
            Giaddr = new IPAddress(data.Slice(24, 4)),
            Chaddr = data.Slice(28, 16).ToArray(),
            Sname = data.Slice(44, 64).ToArray(),
            File = data.Slice(108, 128).ToArray()
        };

        // Parse options (starts at byte 236)
        if (data.Length > HeaderSize)
        {
            var optionsData = data.Slice(HeaderSize);

            // Verify magic cookie
            if (optionsData.Length >= 4 && 
                optionsData[0] == MagicCookie[0] &&
                optionsData[1] == MagicCookie[1] &&
                optionsData[2] == MagicCookie[2] &&
                optionsData[3] == MagicCookie[3])
            {
                packet.Options.AddRange(ParseOptions(optionsData.Slice(4)));
            }
        }

        return packet;
    }

    /// <summary>
    /// Serialize a DhcpPacket into raw bytes for network transmission
    /// </summary>
    /// <param name="packet">DHCP packet to serialize</param>
    /// <returns>Raw packet bytes</returns>
    public static byte[] Serialize(DhcpPacket packet)
    {
        // Calculate options size
        var optionsSize = 4; // Magic cookie
        foreach (var option in packet.Options)
        {
            optionsSize += 2 + option.Length; // Code + Length + Data
        }
        optionsSize += 1; // End option (255)

        // Allocate buffer
        var buffer = new byte[HeaderSize + optionsSize];
        var span = buffer.AsSpan();

        // Write fixed header
        span[0] = packet.Op;
        span[1] = packet.Htype;
        span[2] = packet.Hlen;
        span[3] = packet.Hops;

        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4, 4), packet.Xid);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(8, 2), packet.Secs);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(10, 2), packet.Flags);

        packet.Ciaddr.TryWriteBytes(span.Slice(12, 4), out _);
        packet.Yiaddr.TryWriteBytes(span.Slice(16, 4), out _);
        packet.Siaddr.TryWriteBytes(span.Slice(20, 4), out _);
        packet.Giaddr.TryWriteBytes(span.Slice(24, 4), out _);

        packet.Chaddr.AsSpan().CopyTo(span.Slice(28, 16));
        packet.Sname.AsSpan().CopyTo(span.Slice(44, 64));
        packet.File.AsSpan().CopyTo(span.Slice(108, 128));

        // Write magic cookie
        var optionOffset = HeaderSize;
        MagicCookie.CopyTo(span.Slice(optionOffset, 4));
        optionOffset += 4;

        // Write options
        foreach (var option in packet.Options)
        {
            span[optionOffset++] = option.Code;
            span[optionOffset++] = option.Length;
            option.Data.CopyTo(span.Slice(optionOffset, option.Length));
            optionOffset += option.Length;
        }

        // Write end option
        span[optionOffset] = (byte)DhcpOptionCode.End;

        return buffer;
    }

    /// <summary>
    /// Parse DHCP options from raw data
    /// </summary>
    private static List<DhcpOption> ParseOptions(ReadOnlySpan<byte> data)
    {
        var options = new List<DhcpOption>();
        var offset = 0;

        while (offset < data.Length)
        {
            var code = data[offset++];

            // Pad option
            if (code == (byte)DhcpOptionCode.Pad)
                continue;

            // End option
            if (code == (byte)DhcpOptionCode.End)
                break;

            // Check if we have enough data for length byte
            if (offset >= data.Length)
                break;

            var length = data[offset++];

            // Check if we have enough data for option data
            if (offset + length > data.Length)
                break;

            var optionData = data.Slice(offset, length).ToArray();
            offset += length;

            options.Add(new DhcpOption(code, optionData));
        }

        return options;
    }

    /// <summary>
    /// Create a DHCP response packet based on a request
    /// </summary>
    public static DhcpPacket CreateResponse(DhcpPacket request, DhcpMessageType messageType)
    {
        return new DhcpPacket
        {
            Op = 2, // BOOTREPLY
            Htype = request.Htype,
            Hlen = request.Hlen,
            Hops = 0,
            Xid = request.Xid,
            Secs = 0,
            Flags = request.Flags,
            Ciaddr = IPAddress.Any,
            Yiaddr = IPAddress.Any,
            Siaddr = IPAddress.Any,
            Giaddr = request.Giaddr,
            Chaddr = request.Chaddr,
            Sname = new byte[64],
            File = new byte[128],
            Options = new List<DhcpOption>
            {
                DhcpOption.FromByte((byte)DhcpOptionCode.MessageType, (byte)messageType)
            }
        };
    }
}

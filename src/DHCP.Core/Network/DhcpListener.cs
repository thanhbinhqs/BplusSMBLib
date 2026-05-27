using System.Net;
using System.Net.Sockets;
using DHCP.Core.Models;
using Microsoft.Extensions.Logging;

namespace DHCP.Core.Network;

/// <summary>
/// High-performance asynchronous DHCP network listener
/// Manages UDP socket on port 67 with broadcast support
/// </summary>
public sealed class DhcpListener : IDisposable
{
    private const int DhcpServerPort = 67;
    private const int DhcpClientPort = 68;
    private const int MaxPacketSize = 1500;

    private readonly ILogger<DhcpListener> _logger;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    /// <summary>
    /// Event fired when a DHCP packet is received
    /// </summary>
    public event EventHandler<DhcpPacketReceivedEventArgs>? PacketReceived;

    /// <summary>
    /// Event fired when an error occurs during packet processing
    /// </summary>
    public event EventHandler<Exception>? ErrorOccurred;

    public DhcpListener(ILogger<DhcpListener> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Start listening for DHCP packets
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_udpClient is not null)
        {
            _logger.LogWarning("DHCP listener is already running");
            return;
        }

        try
        {
            // Create UDP client bound to DHCP server port
            _udpClient = new UdpClient(DhcpServerPort);
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
            _udpClient.EnableBroadcast = true;

            _logger.LogInformation("DHCP listener started on port {Port}", DhcpServerPort);

            // Start listening loop
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listenTask = ListenAsync(_cts.Token);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start DHCP listener");
            throw;
        }
    }

    /// <summary>
    /// Stop listening for DHCP packets
    /// </summary>
    public async Task StopAsync()
    {
        if (_udpClient is null)
        {
            _logger.LogWarning("DHCP listener is not running");
            return;
        }

        try
        {
            _logger.LogInformation("Stopping DHCP listener...");

            // Cancel listening loop
            _cts?.Cancel();

            // Wait for listen task to complete
            if (_listenTask is not null)
            {
                await _listenTask;
            }

            // Close UDP client
            _udpClient?.Close();
            _udpClient?.Dispose();
            _udpClient = null;

            _logger.LogInformation("DHCP listener stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping DHCP listener");
        }
    }

    /// <summary>
    /// Send a DHCP packet to a specific endpoint
    /// </summary>
    public async Task SendPacketAsync(DhcpPacket packet, IPEndPoint endpoint, CancellationToken cancellationToken = default)
    {
        if (_udpClient is null)
        {
            throw new InvalidOperationException("DHCP listener is not running");
        }

        try
        {
            var data = DhcpParser.Serialize(packet);
            await _udpClient.SendAsync(data, endpoint, cancellationToken);

            _logger.LogDebug("Sent {MessageType} to {Endpoint}", packet.GetMessageType(), endpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send DHCP packet to {Endpoint}", endpoint);
            throw;
        }
    }

    /// <summary>
    /// Send a DHCP response packet
    /// </summary>
    public async Task SendResponseAsync(DhcpPacket request, DhcpPacket response, CancellationToken cancellationToken = default)
    {
        // Determine destination endpoint
        IPEndPoint endpoint;

        if (request.Giaddr.Equals(IPAddress.Any))
        {
            // No relay agent - send directly to client
            if (request.IsBroadcast() || request.Ciaddr.Equals(IPAddress.Any))
            {
                // Broadcast response
                endpoint = new IPEndPoint(IPAddress.Broadcast, DhcpClientPort);
            }
            else
            {
                // Unicast to client's IP
                endpoint = new IPEndPoint(request.Ciaddr, DhcpClientPort);
            }
        }
        else
        {
            // Relay agent present - send to relay agent
            endpoint = new IPEndPoint(request.Giaddr, DhcpServerPort);
        }

        await SendPacketAsync(response, endpoint, cancellationToken);
    }

    /// <summary>
    /// Main listening loop - processes incoming DHCP packets asynchronously
    /// </summary>
    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DHCP listener loop started");

        while (!cancellationToken.IsCancellationRequested && _udpClient is not null)
        {
            try
            {
                // Receive packet asynchronously
                var result = await _udpClient.ReceiveAsync(cancellationToken);
                var remoteEndpoint = result.RemoteEndPoint;
                var data = result.Buffer;

                _logger.LogDebug("Received {Bytes} bytes from {Endpoint}", data.Length, remoteEndpoint);

                // Process packet on thread pool to avoid blocking receive loop
                _ = Task.Run(() => ProcessPacketAsync(data, remoteEndpoint), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving DHCP packet");
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        _logger.LogInformation("DHCP listener loop stopped");
    }

    /// <summary>
    /// Process a received DHCP packet
    /// </summary>
    private async Task ProcessPacketAsync(byte[] data, IPEndPoint remoteEndpoint)
    {
        try
        {
            // Parse packet
            var packet = DhcpParser.Deserialize(data);

            _logger.LogInformation(
                "Received {MessageType} from {MacAddress} (XID: {TransactionId})",
                packet.GetMessageType(),
                packet.GetMacAddress(),
                packet.Xid
            );

            // Fire event
            PacketReceived?.Invoke(this, new DhcpPacketReceivedEventArgs(packet, remoteEndpoint));

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing DHCP packet");
            ErrorOccurred?.Invoke(this, ex);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _udpClient?.Dispose();
    }
}

/// <summary>
/// Event args for DHCP packet received events
/// </summary>
public sealed class DhcpPacketReceivedEventArgs : EventArgs
{
    public DhcpPacket Packet { get; }
    public IPEndPoint RemoteEndpoint { get; }

    public DhcpPacketReceivedEventArgs(DhcpPacket packet, IPEndPoint remoteEndpoint)
    {
        Packet = packet;
        RemoteEndpoint = remoteEndpoint;
    }
}

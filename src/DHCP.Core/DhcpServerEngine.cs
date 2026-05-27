using System.Net;
using DHCP.Core.Engine;
using DHCP.Core.Models;
using DHCP.Core.Network;
using Microsoft.Extensions.Logging;

namespace DHCP.Core;

/// <summary>
/// Main DHCP Server engine implementing RFC 2131 state machine
/// Orchestrates packet processing, IP allocation, and lease management
/// </summary>
public sealed class DhcpServerEngine : IDisposable
{
    private readonly ILogger<DhcpServerEngine> _logger;
    private readonly DhcpListener _listener;
    private readonly IpAllocationEngine _allocationEngine;
    private readonly DhcpServerConfiguration _config;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Event fired when a packet is received
    /// </summary>
    public event EventHandler<DhcpPacket>? PacketReceived;

    /// <summary>
    /// Event fired when a lease is granted
    /// </summary>
    public event EventHandler<DhcpLease>? LeaseGranted;

    /// <summary>
    /// Event fired when a lease is released
    /// </summary>
    public event EventHandler<DhcpLease>? LeaseReleased;

    /// <summary>
    /// Event fired when a log message is emitted
    /// </summary>
    public event EventHandler<string>? LogEmitted;

    public DhcpServerEngine(
        ILogger<DhcpServerEngine> logger,
        DhcpListener listener,
        IpAllocationEngine allocationEngine,
        DhcpServerConfiguration config)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _listener = listener ?? throw new ArgumentNullException(nameof(listener));
        _allocationEngine = allocationEngine ?? throw new ArgumentNullException(nameof(allocationEngine));
        _config = config ?? throw new ArgumentNullException(nameof(config));

        // Subscribe to listener events
        _listener.PacketReceived += OnPacketReceived;
        _listener.ErrorOccurred += OnListenerError;

        // Subscribe to allocation engine events
        _allocationEngine.LeaseGranted += OnLeaseGranted;
        _allocationEngine.LeaseReleased += OnLeaseReleased;
    }

    /// <summary>
    /// Start the DHCP server
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting DHCP server...");
        EmitLog("Starting DHCP server...");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await _listener.StartAsync(_cts.Token);

        // Start background cleanup task
        _ = Task.Run(() => CleanupLoopAsync(_cts.Token), _cts.Token);

        _logger.LogInformation("DHCP server started successfully");
        EmitLog("DHCP server started successfully");
    }

    /// <summary>
    /// Stop the DHCP server
    /// </summary>
    public async Task StopAsync()
    {
        _logger.LogInformation("Stopping DHCP server...");
        EmitLog("Stopping DHCP server...");

        _cts?.Cancel();
        await _listener.StopAsync();

        _logger.LogInformation("DHCP server stopped");
        EmitLog("DHCP server stopped");
    }

    /// <summary>
    /// Get active leases
    /// </summary>
    public IReadOnlyCollection<DhcpLease> GetActiveLeases()
    {
        return _allocationEngine.GetActiveLeases();
    }

    /// <summary>
    /// Get all leases
    /// </summary>
    public IReadOnlyCollection<DhcpLease> GetAllLeases()
    {
        return _allocationEngine.GetAllLeases();
    }

    /// <summary>
    /// Add a static IP binding
    /// </summary>
    public bool AddStaticBinding(string macAddress, IPAddress ipAddress)
    {
        return _allocationEngine.AddStaticBinding(macAddress, ipAddress);
    }

    /// <summary>
    /// Remove a static IP binding
    /// </summary>
    public bool RemoveStaticBinding(string macAddress)
    {
        return _allocationEngine.RemoveStaticBinding(macAddress);
    }

    /// <summary>
    /// Handle received DHCP packet
    /// </summary>
    private async void OnPacketReceived(object? sender, DhcpPacketReceivedEventArgs e)
    {
        var packet = e.Packet;
        var messageType = packet.GetMessageType();

        _logger.LogInformation("Received {MessageType} from {MacAddress}", messageType, packet.GetMacAddress());
        PacketReceived?.Invoke(this, packet);

        try
        {
            switch (messageType)
            {
                case DhcpMessageType.Discover:
                    await HandleDiscoverAsync(packet);
                    break;

                case DhcpMessageType.Request:
                    await HandleRequestAsync(packet);
                    break;

                case DhcpMessageType.Release:
                    HandleRelease(packet);
                    break;

                case DhcpMessageType.Decline:
                    HandleDecline(packet);
                    break;

                case DhcpMessageType.Inform:
                    await HandleInformAsync(packet);
                    break;

                default:
                    _logger.LogWarning("Unsupported message type: {MessageType}", messageType);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing {MessageType} packet", messageType);
            EmitLog($"Error processing {messageType}: {ex.Message}");
        }
    }

    /// <summary>
    /// Handle DHCPDISCOVER - respond with DHCPOFFER
    /// </summary>
    private async Task HandleDiscoverAsync(DhcpPacket request)
    {
        var macAddress = request.GetMacAddress();
        var offeredIp = _allocationEngine.ProcessDiscover(request);

        if (offeredIp is null)
        {
            _logger.LogWarning("No available IP for {MacAddress}", macAddress);
            EmitLog($"No available IP for {macAddress}");
            return;
        }

        // Create DHCPOFFER response
        var response = DhcpParser.CreateResponse(request, DhcpMessageType.Offer);
        response.Yiaddr = offeredIp;
        response.Siaddr = _config.ServerIpAddress;

        // Build options
        var options = BuildStandardOptions(DhcpMessageType.Offer);
        response.WithOptions(options);

        // Send offer
        await _listener.SendResponseAsync(request, response);

        _logger.LogInformation("Sent OFFER: {IpAddress} to {MacAddress}", offeredIp, macAddress);
        EmitLog($"OFFER: {offeredIp} to {macAddress}");
    }

    /// <summary>
    /// Handle DHCPREQUEST - respond with DHCPACK or DHCPNAK
    /// </summary>
    private async Task HandleRequestAsync(DhcpPacket request)
    {
        var macAddress = request.GetMacAddress();

        // Determine requested IP
        IPAddress? requestedIp = null;

        // Check for requested IP option (SELECTING state)
        var requestedIpOption = request.GetOption(DhcpOptionCode.RequestedIpAddress);
        if (requestedIpOption is not null)
        {
            requestedIp = requestedIpOption.AsIpAddress();
        }
        // Check client IP (RENEWING/REBINDING state)
        else if (!request.Ciaddr.Equals(IPAddress.Any))
        {
            requestedIp = request.Ciaddr;
        }

        if (requestedIp is null)
        {
            _logger.LogWarning("REQUEST from {MacAddress} without requested IP", macAddress);
            await SendNakAsync(request, "No requested IP address");
            return;
        }

        // Verify server identifier if present (must match our server)
        var serverIdOption = request.GetOption(DhcpOptionCode.ServerIdentifier);
        if (serverIdOption is not null)
        {
            var serverId = serverIdOption.AsIpAddress();
            if (serverId is not null && !serverId.Equals(_config.ServerIpAddress))
            {
                _logger.LogInformation("REQUEST for different server: {ServerId}", serverId);
                return; // Ignore - request is for another server
            }
        }

        // Process request
        if (_allocationEngine.ProcessRequest(request, requestedIp, out var lease))
        {
            // Create DHCPACK response
            var response = DhcpParser.CreateResponse(request, DhcpMessageType.Ack);
            response.Yiaddr = requestedIp;
            response.Siaddr = _config.ServerIpAddress;

            // Build options
            var options = BuildStandardOptions(DhcpMessageType.Ack);
            response.WithOptions(options);

            // Send ACK
            await _listener.SendResponseAsync(request, response);

            _logger.LogInformation("Sent ACK: {IpAddress} to {MacAddress}", requestedIp, macAddress);
            EmitLog($"ACK: {requestedIp} to {macAddress}");
        }
        else
        {
            // Send NAK
            await SendNakAsync(request, "Requested IP not available");

            _logger.LogWarning("Sent NAK to {MacAddress} (IP {IpAddress} not available)", macAddress, requestedIp);
            EmitLog($"NAK to {macAddress} - IP {requestedIp} not available");
        }
    }

    /// <summary>
    /// Handle DHCPRELEASE
    /// </summary>
    private void HandleRelease(DhcpPacket request)
    {
        var macAddress = request.GetMacAddress();
        _allocationEngine.ProcessRelease(request);

        _logger.LogInformation("Processed RELEASE from {MacAddress}", macAddress);
        EmitLog($"RELEASE from {macAddress}");
    }

    /// <summary>
    /// Handle DHCPDECLINE
    /// </summary>
    private void HandleDecline(DhcpPacket request)
    {
        var macAddress = request.GetMacAddress();
        _allocationEngine.ProcessDecline(request);

        _logger.LogWarning("Processed DECLINE from {MacAddress}", macAddress);
        EmitLog($"DECLINE from {macAddress}");
    }

    /// <summary>
    /// Handle DHCPINFORM - respond with DHCPACK without allocation
    /// </summary>
    private async Task HandleInformAsync(DhcpPacket request)
    {
        var macAddress = request.GetMacAddress();

        // Create DHCPACK response (no IP allocation)
        var response = DhcpParser.CreateResponse(request, DhcpMessageType.Ack);
        response.Ciaddr = request.Ciaddr;
        response.Siaddr = _config.ServerIpAddress;

        // Build options (network configuration only)
        var options = BuildInformOptions();
        response.WithOptions(options);

        // Send ACK
        await _listener.SendResponseAsync(request, response);

        _logger.LogInformation("Sent ACK (INFORM) to {MacAddress}", macAddress);
        EmitLog($"ACK (INFORM) to {macAddress}");
    }

    /// <summary>
    /// Send DHCPNAK response
    /// </summary>
    private async Task SendNakAsync(DhcpPacket request, string reason)
    {
        var response = DhcpParser.CreateResponse(request, DhcpMessageType.Nak);
        response.Siaddr = _config.ServerIpAddress;

        var options = new List<DhcpOption>
        {
            DhcpOption.FromByte((byte)DhcpOptionCode.MessageType, (byte)DhcpMessageType.Nak),
            DhcpOption.FromIpAddress((byte)DhcpOptionCode.ServerIdentifier, _config.ServerIpAddress)
        };

        response.WithOptions(options);

        await _listener.SendResponseAsync(request, response);
    }

    /// <summary>
    /// Build standard DHCP options for OFFER/ACK
    /// </summary>
    private List<DhcpOption> BuildStandardOptions(DhcpMessageType messageType)
    {
        var options = new List<DhcpOption>
        {
            DhcpOption.FromByte((byte)DhcpOptionCode.MessageType, (byte)messageType),
            DhcpOption.FromIpAddress((byte)DhcpOptionCode.ServerIdentifier, _config.ServerIpAddress),
            DhcpOption.FromUInt32((byte)DhcpOptionCode.LeaseTime, _config.DefaultLeaseTime),
            DhcpOption.FromUInt32((byte)DhcpOptionCode.RenewalTime, _config.GetRenewalTime(_config.DefaultLeaseTime)),
            DhcpOption.FromUInt32((byte)DhcpOptionCode.RebindingTime, _config.GetRebindingTime(_config.DefaultLeaseTime)),
            DhcpOption.FromIpAddress((byte)DhcpOptionCode.SubnetMask, _config.SubnetMask)
        };

        if (!_config.Gateway.Equals(IPAddress.Any))
        {
            options.Add(DhcpOption.FromIpAddress((byte)DhcpOptionCode.Router, _config.Gateway));
        }

        if (_config.DnsServers.Length > 0)
        {
            options.Add(DhcpOption.FromIpAddresses((byte)DhcpOptionCode.DnsServer, _config.DnsServers));
        }

        if (_config.BroadcastAddress is not null)
        {
            options.Add(DhcpOption.FromIpAddress((byte)DhcpOptionCode.BroadcastAddress, _config.BroadcastAddress));
        }

        if (!string.IsNullOrEmpty(_config.DomainName))
        {
            options.Add(DhcpOption.FromString((byte)DhcpOptionCode.DomainName, _config.DomainName));
        }

        return options;
    }

    /// <summary>
    /// Build options for DHCPINFORM response
    /// </summary>
    private List<DhcpOption> BuildInformOptions()
    {
        var options = new List<DhcpOption>
        {
            DhcpOption.FromByte((byte)DhcpOptionCode.MessageType, (byte)DhcpMessageType.Ack),
            DhcpOption.FromIpAddress((byte)DhcpOptionCode.ServerIdentifier, _config.ServerIpAddress),
            DhcpOption.FromIpAddress((byte)DhcpOptionCode.SubnetMask, _config.SubnetMask)
        };

        if (!_config.Gateway.Equals(IPAddress.Any))
        {
            options.Add(DhcpOption.FromIpAddress((byte)DhcpOptionCode.Router, _config.Gateway));
        }

        if (_config.DnsServers.Length > 0)
        {
            options.Add(DhcpOption.FromIpAddresses((byte)DhcpOptionCode.DnsServer, _config.DnsServers));
        }

        return options;
    }

    /// <summary>
    /// Background cleanup loop for expired leases
    /// </summary>
    private async Task CleanupLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
                _allocationEngine.CleanupExpiredLeases();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in cleanup loop");
            }
        }
    }

    /// <summary>
    /// Emit log event
    /// </summary>
    private void EmitLog(string message)
    {
        LogEmitted?.Invoke(this, message);
    }

    private void OnLeaseGranted(object? sender, DhcpLease lease)
    {
        LeaseGranted?.Invoke(this, lease);
    }

    private void OnLeaseReleased(object? sender, DhcpLease lease)
    {
        LeaseReleased?.Invoke(this, lease);
    }

    private void OnListenerError(object? sender, Exception ex)
    {
        _logger.LogError(ex, "Listener error");
        EmitLog($"Listener error: {ex.Message}");
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _listener?.Dispose();
    }
}

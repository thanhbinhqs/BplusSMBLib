using System.Net;
using DHCP.Core;
using DHCP.Core.Engine;
using DHCP.Core.Network;
using DHCP.ManagementApp.Models;
using Microsoft.Extensions.Logging;

namespace DHCP.ManagementApp.Services;

public class DhcpServerService : IDhcpServerService
{
    private readonly ILogger<DhcpServerService> _logger;
    private DhcpServerEngine? _serverEngine;
    private DhcpListener? _listener;
    private IpAllocationEngine? _allocationEngine;
    private DhcpServerConfiguration? _config;

    public event EventHandler<string>? LogReceived;
    public event EventHandler<LeaseDisplayModel>? LeaseGranted;
    public event EventHandler? ServerStateChanged;

    public bool IsRunning => _serverEngine != null;

    public DhcpServerService(ILogger<DhcpServerService> logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(DhcpSettings settings)
    {
        if (IsRunning)
        {
            EmitLog("Server is already running");
            return;
        }

        try
        {
            settings.Validate();

            // Create configuration
            _config = new DhcpServerConfiguration
            {
                ServerIpAddress = IPAddress.Parse(settings.ServerIpAddress),
                SubnetMask = IPAddress.Parse(settings.SubnetMask),
                Gateway = IPAddress.Parse(settings.Gateway),
                DnsServers = new[]
                {
                    IPAddress.Parse(settings.PrimaryDns),
                    IPAddress.Parse(settings.SecondaryDns)
                },
                DefaultLeaseTime = (uint)settings.DefaultLeaseTime,
                MaxLeaseTime = (uint)settings.MaxLeaseTime,
                DomainName = settings.DomainName
            };

            // Create components
            var listenerLogger = LoggerFactory.Create(b => b.AddDebug()).CreateLogger<DhcpListener>();
            var engineLogger = LoggerFactory.Create(b => b.AddDebug()).CreateLogger<IpAllocationEngine>();
            var serverLogger = LoggerFactory.Create(b => b.AddDebug()).CreateLogger<DhcpServerEngine>();

            _listener = new DhcpListener(listenerLogger);
            _allocationEngine = new IpAllocationEngine(
                engineLogger,
                _config,
                IPAddress.Parse(settings.PoolStartIp),
                IPAddress.Parse(settings.PoolEndIp)
            );

            // Create server engine
            _serverEngine = new DhcpServerEngine(serverLogger, _listener, _allocationEngine, _config);

            // Subscribe to events
            _serverEngine.PacketReceived += (s, packet) =>
            {
                EmitLog($"Received {packet.GetMessageType()} from {packet.GetMacAddress()}");
            };

            _serverEngine.LeaseGranted += (s, lease) =>
            {
                EmitLog($"Lease granted: {lease.IpAddress} -> {lease.MacAddress}");
                LeaseGranted?.Invoke(this, ConvertToDisplayModel(lease));
            };

            _serverEngine.LogEmitted += (s, log) =>
            {
                EmitLog(log);
            };

            // Start server
            await _serverEngine.StartAsync();
            EmitLog("DHCP Server started successfully");
            ServerStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start DHCP server");
            EmitLog($"Error: {ex.Message}");
            await StopAsync();
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (_serverEngine != null)
        {
            try
            {
                await _serverEngine.StopAsync();
                _serverEngine.Dispose();
                _serverEngine = null;
                _listener = null;
                _allocationEngine = null;

                EmitLog("DHCP Server stopped");
                ServerStateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping DHCP server");
                EmitLog($"Error stopping server: {ex.Message}");
            }
        }
    }

    public IEnumerable<LeaseDisplayModel> GetActiveLeases()
    {
        if (_serverEngine == null)
            return Enumerable.Empty<LeaseDisplayModel>();

        return _serverEngine.GetActiveLeases().Select(ConvertToDisplayModel);
    }

    public bool AddStaticBinding(string macAddress, string ipAddress)
    {
        if (_serverEngine == null)
            return false;

        try
        {
            var result = _serverEngine.AddStaticBinding(macAddress, IPAddress.Parse(ipAddress));
            if (result)
            {
                EmitLog($"Added static binding: {macAddress} -> {ipAddress}");
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add static binding");
            EmitLog($"Error adding static binding: {ex.Message}");
            return false;
        }
    }

    public bool RemoveStaticBinding(string macAddress)
    {
        if (_serverEngine == null)
            return false;

        try
        {
            var result = _serverEngine.RemoveStaticBinding(macAddress);
            if (result)
            {
                EmitLog($"Removed static binding: {macAddress}");
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove static binding");
            EmitLog($"Error removing static binding: {ex.Message}");
            return false;
        }
    }

    private LeaseDisplayModel ConvertToDisplayModel(DhcpLease lease)
    {
        var remaining = lease.ExpiryTime - DateTime.UtcNow;
        var remainingStr = lease.IsStatic ? "Permanent" :
            remaining.TotalSeconds > 0 ? $"{remaining.Days}d {remaining.Hours}h {remaining.Minutes}m" : "Expired";

        return new LeaseDisplayModel
        {
            IpAddress = lease.IpAddress.ToString(),
            MacAddress = lease.MacAddress,
            Hostname = lease.Hostname ?? "Unknown",
            LeaseStart = lease.LeaseStartTime,
            LeaseExpiry = lease.ExpiryTime,
            RemainingTime = remainingStr,
            IsStatic = lease.IsStatic,
            Status = lease.IsExpired() ? "Expired" : lease.IsStatic ? "Static" : "Active"
        };
    }

    private void EmitLog(string message)
    {
        LogReceived?.Invoke(this, message);
    }
}

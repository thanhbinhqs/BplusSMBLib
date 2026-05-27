using System.Collections.Concurrent;
using System.Net;
using DHCP.Core.Models;
using Microsoft.Extensions.Logging;

namespace DHCP.Core.Engine;

/// <summary>
/// High-performance IP allocation engine with dynamic pool and static binding support
/// Implements DHCP state machine as per RFC 2131
/// </summary>
public sealed class IpAllocationEngine
{
    private readonly ILogger<IpAllocationEngine> _logger;
    private readonly DhcpServerConfiguration _config;

    // Dynamic pool management
    private readonly IPAddress _poolStartIp;
    private readonly IPAddress _poolEndIp;
    private readonly ConcurrentDictionary<string, DhcpLease> _leasesByMac = new();
    private readonly ConcurrentDictionary<string, DhcpLease> _leasesByIp = new();

    // Static bindings (MAC -> Reserved IP)
    private readonly ConcurrentDictionary<string, IPAddress> _staticBindings = new();

    // IP availability tracking
    private readonly ConcurrentDictionary<string, bool> _availableIps = new();

    /// <summary>
    /// Event fired when a lease is granted
    /// </summary>
    public event EventHandler<DhcpLease>? LeaseGranted;

    /// <summary>
    /// Event fired when a lease is released
    /// </summary>
    public event EventHandler<DhcpLease>? LeaseReleased;



    public IpAllocationEngine(
        ILogger<IpAllocationEngine> logger,
        DhcpServerConfiguration config,
        IPAddress poolStartIp,
        IPAddress poolEndIp)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _poolStartIp = poolStartIp ?? throw new ArgumentNullException(nameof(poolStartIp));
        _poolEndIp = poolEndIp ?? throw new ArgumentNullException(nameof(poolEndIp));

        InitializeIpPool();
    }

    /// <summary>
    /// Add a static binding (reserved IP for specific MAC address)
    /// Used for industrial machines like CNCs, PLCs, etc.
    /// </summary>
    public bool AddStaticBinding(string macAddress, IPAddress ipAddress)
    {
        macAddress = NormalizeMacAddress(macAddress);

        if (_staticBindings.TryAdd(macAddress, ipAddress))
        {
            _logger.LogInformation("Added static binding: {MacAddress} -> {IpAddress}", macAddress, ipAddress);

            // Mark IP as unavailable in dynamic pool
            _availableIps.TryRemove(ipAddress.ToString(), out _);

            return true;
        }

        _logger.LogWarning("Failed to add static binding for {MacAddress} (already exists)", macAddress);
        return false;
    }

    /// <summary>
    /// Remove a static binding
    /// </summary>
    public bool RemoveStaticBinding(string macAddress)
    {
        macAddress = NormalizeMacAddress(macAddress);

        if (_staticBindings.TryRemove(macAddress, out var ipAddress))
        {
            _logger.LogInformation("Removed static binding: {MacAddress} -> {IpAddress}", macAddress, ipAddress);

            // Return IP to dynamic pool if within range
            if (IsIpInPool(ipAddress))
            {
                _availableIps.TryAdd(ipAddress.ToString(), true);
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Process DHCP DISCOVER message - find available IP and prepare OFFER
    /// </summary>
    public IPAddress? ProcessDiscover(DhcpPacket packet)
    {
        var macAddress = packet.GetMacAddress();
        _logger.LogInformation("Processing DISCOVER from {MacAddress}", macAddress);

        // Check for static binding first
        if (_staticBindings.TryGetValue(macAddress, out var staticIp))
        {
            _logger.LogInformation("Found static binding for {MacAddress}: {IpAddress}", macAddress, staticIp);
            return staticIp;
        }

        // Check if client already has an active lease
        if (_leasesByMac.TryGetValue(macAddress, out var existingLease) && !existingLease.IsExpired())
        {
            _logger.LogInformation("Found existing lease for {MacAddress}: {IpAddress}", macAddress, existingLease.IpAddress);
            return existingLease.IpAddress;
        }

        // Check if client requested a specific IP
        var requestedIpOption = packet.GetOption(DhcpOptionCode.RequestedIpAddress);
        if (requestedIpOption is not null)
        {
            var requestedIp = requestedIpOption.AsIpAddress();
            if (requestedIp is not null && IsIpAvailable(requestedIp, macAddress))
            {
                _logger.LogInformation("Client requested IP {IpAddress} is available", requestedIp);
                return requestedIp;
            }
        }

        // Allocate new IP from pool
        var availableIp = FindAvailableIpFromPool();
        if (availableIp is not null)
        {
            _logger.LogInformation("Allocated new IP {IpAddress} for {MacAddress}", availableIp, macAddress);
            return availableIp;
        }

        _logger.LogWarning("No available IPs in pool for {MacAddress}", macAddress);
        return null;
    }

    /// <summary>
    /// Process DHCP REQUEST message - validate and commit lease
    /// </summary>
    public bool ProcessRequest(DhcpPacket packet, IPAddress requestedIp, out DhcpLease? lease)
    {
        var macAddress = packet.GetMacAddress();
        _logger.LogInformation("Processing REQUEST from {MacAddress} for {IpAddress}", macAddress, requestedIp);

        lease = null;

        // Verify IP is available or already assigned to this client
        if (!IsIpAvailable(requestedIp, macAddress))
        {
            _logger.LogWarning("Requested IP {IpAddress} is not available for {MacAddress}", requestedIp, macAddress);
            return false;
        }

        // Determine if this is a static binding
        var isStatic = _staticBindings.TryGetValue(macAddress, out var staticIp) && staticIp.Equals(requestedIp);

        // Create lease
        var leaseTime = _config.DefaultLeaseTime;
        var hostname = packet.GetOption(DhcpOptionCode.HostName)?.AsString();

        lease = new DhcpLease
        {
            MacAddress = macAddress,
            IpAddress = requestedIp,
            Hostname = hostname,
            LeaseStartTime = DateTime.UtcNow,
            ExpiryTime = isStatic ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(leaseTime),
            IsStatic = isStatic,
            LastTransactionId = packet.Xid
        };

        // Commit lease
        _leasesByMac[macAddress] = lease;
        _leasesByIp[requestedIp.ToString()] = lease;
        _availableIps.TryRemove(requestedIp.ToString(), out _);

        _logger.LogInformation(
            "Lease granted: {IpAddress} -> {MacAddress} ({LeaseType})",
            requestedIp,
            macAddress,
            isStatic ? "Static" : $"Dynamic, expires in {leaseTime}s"
        );

        LeaseGranted?.Invoke(this, lease);
        return true;
    }

    /// <summary>
    /// Process DHCP RELEASE message - free up the lease
    /// </summary>
    public bool ProcessRelease(DhcpPacket packet)
    {
        var macAddress = packet.GetMacAddress();
        var clientIp = packet.Ciaddr;

        _logger.LogInformation("Processing RELEASE from {MacAddress} for {IpAddress}", macAddress, clientIp);

        if (_leasesByMac.TryRemove(macAddress, out var lease))
        {
            _leasesByIp.TryRemove(clientIp.ToString(), out _);

            // Return IP to pool if not a static binding
            if (!lease.IsStatic && IsIpInPool(clientIp))
            {
                _availableIps.TryAdd(clientIp.ToString(), true);
            }

            _logger.LogInformation("Lease released: {IpAddress} -> {MacAddress}", clientIp, macAddress);
            LeaseReleased?.Invoke(this, lease);
            return true;
        }

        _logger.LogWarning("No lease found for {MacAddress}", macAddress);
        return false;
    }

    /// <summary>
    /// Process DHCP DECLINE message - mark IP as problematic
    /// </summary>
    public void ProcessDecline(DhcpPacket packet)
    {
        var macAddress = packet.GetMacAddress();
        var requestedIp = packet.GetOption(DhcpOptionCode.RequestedIpAddress)?.AsIpAddress();

        _logger.LogWarning("Client {MacAddress} declined IP {IpAddress}", macAddress, requestedIp);

        if (requestedIp is not null)
        {
            // Remove from available pool (mark as problematic)
            _availableIps.TryRemove(requestedIp.ToString(), out _);
            _leasesByIp.TryRemove(requestedIp.ToString(), out _);
        }

        if (_leasesByMac.TryGetValue(macAddress, out var lease))
        {
            _leasesByMac.TryRemove(macAddress, out _);
        }
    }

    /// <summary>
    /// Get active leases
    /// </summary>
    public IReadOnlyCollection<DhcpLease> GetActiveLeases()
    {
        return _leasesByMac.Values.Where(l => !l.IsExpired()).ToList();
    }

    /// <summary>
    /// Get all leases (including expired)
    /// </summary>
    public IReadOnlyCollection<DhcpLease> GetAllLeases()
    {
        return _leasesByMac.Values.ToList();
    }

    /// <summary>
    /// Clean up expired leases
    /// </summary>
    public void CleanupExpiredLeases()
    {
        var expiredLeases = _leasesByMac.Values.Where(l => l.IsExpired()).ToList();

        foreach (var lease in expiredLeases)
        {
            _leasesByMac.TryRemove(lease.MacAddress, out _);
            _leasesByIp.TryRemove(lease.IpAddress.ToString(), out _);

            // Return IP to pool
            if (IsIpInPool(lease.IpAddress))
            {
                _availableIps.TryAdd(lease.IpAddress.ToString(), true);
            }

            _logger.LogInformation("Cleaned up expired lease: {IpAddress} -> {MacAddress}", lease.IpAddress, lease.MacAddress);
        }
    }

    /// <summary>
    /// Initialize IP pool with all available addresses
    /// </summary>
    private void InitializeIpPool()
    {
        var startBytes = _poolStartIp.GetAddressBytes();
        var endBytes = _poolEndIp.GetAddressBytes();

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(startBytes);
            Array.Reverse(endBytes);
        }

        var startInt = BitConverter.ToUInt32(startBytes, 0);
        var endInt = BitConverter.ToUInt32(endBytes, 0);

        for (uint i = startInt; i <= endInt; i++)
        {
            var ipBytes = BitConverter.GetBytes(i);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(ipBytes);
            }
            var ipAddress = new IPAddress(ipBytes);
            _availableIps.TryAdd(ipAddress.ToString(), true);
        }

        _logger.LogInformation(
            "Initialized IP pool: {StartIp} - {EndIp} ({Count} addresses)",
            _poolStartIp,
            _poolEndIp,
            _availableIps.Count
        );
    }

    /// <summary>
    /// Find an available IP from the pool
    /// </summary>
    private IPAddress? FindAvailableIpFromPool()
    {
        foreach (var kvp in _availableIps)
        {
            if (IPAddress.TryParse(kvp.Key, out var ip))
            {
                return ip;
            }
        }

        return null;
    }

    /// <summary>
    /// Check if an IP is available for assignment
    /// </summary>
    private bool IsIpAvailable(IPAddress ip, string macAddress)
    {
        // Check if it's a static binding for this MAC
        if (_staticBindings.TryGetValue(macAddress, out var staticIp) && staticIp.Equals(ip))
        {
            return true;
        }

        // Check if already leased to this MAC
        if (_leasesByMac.TryGetValue(macAddress, out var lease) && lease.IpAddress.Equals(ip))
        {
            return true;
        }

        // Check if IP is in available pool
        return _availableIps.ContainsKey(ip.ToString());
    }

    /// <summary>
    /// Check if IP is within the configured pool range
    /// </summary>
    private bool IsIpInPool(IPAddress ip)
    {
        var ipBytes = ip.GetAddressBytes();
        var startBytes = _poolStartIp.GetAddressBytes();
        var endBytes = _poolEndIp.GetAddressBytes();

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(ipBytes);
            Array.Reverse(startBytes);
            Array.Reverse(endBytes);
        }

        var ipInt = BitConverter.ToUInt32(ipBytes, 0);
        var startInt = BitConverter.ToUInt32(startBytes, 0);
        var endInt = BitConverter.ToUInt32(endBytes, 0);

        return ipInt >= startInt && ipInt <= endInt;
    }

    /// <summary>
    /// Normalize MAC address format (to uppercase with colons)
    /// </summary>
    private static string NormalizeMacAddress(string macAddress)
    {
        return macAddress.Replace("-", ":").Replace(".", ":").ToUpperInvariant();
    }
}

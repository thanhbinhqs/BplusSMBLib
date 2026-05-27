using System.Net;

namespace DHCP.ManagementApp.Models;

public class DhcpSettings
{
    public string ServerIpAddress { get; set; } = "192.168.1.1";
    public string SubnetMask { get; set; } = "255.255.255.0";
    public string Gateway { get; set; } = "192.168.1.1";
    public string PrimaryDns { get; set; } = "8.8.8.8";
    public string SecondaryDns { get; set; } = "8.8.4.4";
    public string DomainName { get; set; } = "local";
    public string PoolStartIp { get; set; } = "192.168.1.100";
    public string PoolEndIp { get; set; } = "192.168.1.200";
    public int DefaultLeaseTime { get; set; } = 86400;
    public int MaxLeaseTime { get; set; } = 604800;
    public bool EnableActionBridge { get; set; } = true;
    public int ActionBridgePort { get; set; } = 8888;
    public bool AutoStart { get; set; } = false;

    public void Validate()
    {
        if (!IPAddress.TryParse(ServerIpAddress, out _))
            throw new ArgumentException("Invalid server IP address");

        if (!IPAddress.TryParse(SubnetMask, out _))
            throw new ArgumentException("Invalid subnet mask");

        if (!IPAddress.TryParse(Gateway, out _))
            throw new ArgumentException("Invalid gateway");

        if (!IPAddress.TryParse(PrimaryDns, out _))
            throw new ArgumentException("Invalid primary DNS");

        if (!IPAddress.TryParse(SecondaryDns, out _))
            throw new ArgumentException("Invalid secondary DNS");

        if (!IPAddress.TryParse(PoolStartIp, out _))
            throw new ArgumentException("Invalid pool start IP");

        if (!IPAddress.TryParse(PoolEndIp, out _))
            throw new ArgumentException("Invalid pool end IP");

        if (DefaultLeaseTime <= 0)
            throw new ArgumentException("Lease time must be positive");
    }
}

public class StaticBindingModel
{
    public string MacAddress { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class LeaseDisplayModel
{
    public string IpAddress { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public DateTime LeaseStart { get; set; }
    public DateTime LeaseExpiry { get; set; }
    public string RemainingTime { get; set; } = string.Empty;
    public bool IsStatic { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "INFO";
    public string Message { get; set; } = string.Empty;
}

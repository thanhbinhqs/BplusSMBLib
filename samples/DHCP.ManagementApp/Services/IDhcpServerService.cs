using DHCP.Core.Engine;
using DHCP.ManagementApp.Models;

namespace DHCP.ManagementApp.Services;

public interface IDhcpServerService
{
    event EventHandler<string>? LogReceived;
    event EventHandler<LeaseDisplayModel>? LeaseGranted;
    event EventHandler? ServerStateChanged;

    bool IsRunning { get; }
    Task StartAsync(DhcpSettings settings);
    Task StopAsync();
    IEnumerable<LeaseDisplayModel> GetActiveLeases();
    bool AddStaticBinding(string macAddress, string ipAddress);
    bool RemoveStaticBinding(string macAddress);
}

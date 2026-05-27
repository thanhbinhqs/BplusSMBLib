using DHCP.ManagementApp.Models;

namespace DHCP.ManagementApp.Services;

public interface ISettingsService
{
    DhcpSettings LoadSettings();
    void SaveSettings(DhcpSettings settings);
}

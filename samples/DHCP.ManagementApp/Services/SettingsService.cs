using System.IO;
using System.Text.Json;
using DHCP.ManagementApp.Models;

namespace DHCP.ManagementApp.Services;

public class SettingsService : ISettingsService
{
    private readonly string _settingsPath;

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appData, "DHCPManagement");
        Directory.CreateDirectory(appFolder);
        _settingsPath = Path.Combine(appFolder, "settings.json");
    }

    public DhcpSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<DhcpSettings>(json) ?? new DhcpSettings();
            }
        }
        catch
        {
            // Return default settings if load fails
        }

        return new DhcpSettings();
    }

    public void SaveSettings(DhcpSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(_settingsPath, json);
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using SmbEnterprise.Core.Models;

namespace SmbEnterprise.WinFormsApp;

public sealed class SmbSettings
{
    public string Server { get; set; } = "";
    public string Share { get; set; } = "";
    public string Username { get; set; } = "";
    public string Domain { get; set; } = "";
    public int Port { get; set; } = 445;

    [JsonIgnore]
    public string Password { get; set; } = "";
}

public sealed class SettingsManager
{
    private readonly string _settingsPath;
    private const string SettingsFile = "smb_settings.json";

    public SettingsManager()
    {
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SmbEnterprise",
            SettingsFile);
    }

    public SmbSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<SmbSettings>(json) ?? new();
            }
        }
        catch { /* ignore load errors */ }
        return new();
    }

    public void Save(SmbSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch { /* ignore save errors */ }
    }

    public RemoteCredential ToCredential(SmbSettings settings)
    {
        return new RemoteCredential
        {
            Server = settings.Server,
            Share = settings.Share,
            Username = settings.Username,
            Domain = settings.Domain,
            Port = settings.Port
        };
    }
}

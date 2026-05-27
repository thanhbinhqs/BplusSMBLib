using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DHCP.ManagementApp.Models;
using DHCP.ManagementApp.Services;

namespace DHCP.ManagementApp.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IDhcpServerService _dhcpService;

    [ObservableProperty]
    private string _serverIpAddress = "192.168.1.1";

    [ObservableProperty]
    private string _subnetMask = "255.255.255.0";

    [ObservableProperty]
    private string _gateway = "192.168.1.1";

    [ObservableProperty]
    private string _primaryDns = "8.8.8.8";

    [ObservableProperty]
    private string _secondaryDns = "8.8.4.4";

    [ObservableProperty]
    private string _domainName = "local";

    [ObservableProperty]
    private string _poolStartIp = "192.168.1.100";

    [ObservableProperty]
    private string _poolEndIp = "192.168.1.200";

    [ObservableProperty]
    private int _defaultLeaseTime = 86400;

    [ObservableProperty]
    private int _maxLeaseTime = 604800;

    [ObservableProperty]
    private bool _enableActionBridge = true;

    [ObservableProperty]
    private int _actionBridgePort = 8888;

    [ObservableProperty]
    private bool _autoStart;

    public SettingsViewModel(ISettingsService settingsService, IDhcpServerService dhcpService)
    {
        _settingsService = settingsService;
        _dhcpService = dhcpService;
        LoadSettings();
    }

    [RelayCommand]
    private void LoadSettings()
    {
        var settings = _settingsService.LoadSettings();
        ServerIpAddress = settings.ServerIpAddress;
        SubnetMask = settings.SubnetMask;
        Gateway = settings.Gateway;
        PrimaryDns = settings.PrimaryDns;
        SecondaryDns = settings.SecondaryDns;
        DomainName = settings.DomainName;
        PoolStartIp = settings.PoolStartIp;
        PoolEndIp = settings.PoolEndIp;
        DefaultLeaseTime = settings.DefaultLeaseTime;
        MaxLeaseTime = settings.MaxLeaseTime;
        EnableActionBridge = settings.EnableActionBridge;
        ActionBridgePort = settings.ActionBridgePort;
        AutoStart = settings.AutoStart;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            var settings = new DhcpSettings
            {
                ServerIpAddress = ServerIpAddress,
                SubnetMask = SubnetMask,
                Gateway = Gateway,
                PrimaryDns = PrimaryDns,
                SecondaryDns = SecondaryDns,
                DomainName = DomainName,
                PoolStartIp = PoolStartIp,
                PoolEndIp = PoolEndIp,
                DefaultLeaseTime = DefaultLeaseTime,
                MaxLeaseTime = MaxLeaseTime,
                EnableActionBridge = EnableActionBridge,
                ActionBridgePort = ActionBridgePort,
                AutoStart = AutoStart
            };

            settings.Validate();
            _settingsService.SaveSettings(settings);

            MessageBox.Show("Settings saved successfully. Restart the server for changes to take effect.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        var result = MessageBox.Show("Are you sure you want to reset all settings to defaults?", "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            var defaults = new DhcpSettings();
            ServerIpAddress = defaults.ServerIpAddress;
            SubnetMask = defaults.SubnetMask;
            Gateway = defaults.Gateway;
            PrimaryDns = defaults.PrimaryDns;
            SecondaryDns = defaults.SecondaryDns;
            DomainName = defaults.DomainName;
            PoolStartIp = defaults.PoolStartIp;
            PoolEndIp = defaults.PoolEndIp;
            DefaultLeaseTime = defaults.DefaultLeaseTime;
            MaxLeaseTime = defaults.MaxLeaseTime;
            EnableActionBridge = defaults.EnableActionBridge;
            ActionBridgePort = defaults.ActionBridgePort;
            AutoStart = defaults.AutoStart;
        }
    }
}

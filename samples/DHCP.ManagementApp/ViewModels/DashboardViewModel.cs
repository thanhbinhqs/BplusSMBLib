using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DHCP.ManagementApp.Models;
using DHCP.ManagementApp.Services;

namespace DHCP.ManagementApp.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly IDhcpServerService _dhcpService;
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private bool _isServerRunning;

    [ObservableProperty]
    private int _activeLeases;

    [ObservableProperty]
    private string _serverIpAddress = "N/A";

    [ObservableProperty]
    private string _ipPoolRange = "N/A";

    [ObservableProperty]
    private ObservableCollection<LogEntry> _recentLogs = new();

    public DashboardViewModel(IDhcpServerService dhcpService, ISettingsService settingsService)
    {
        _dhcpService = dhcpService;
        _settingsService = settingsService;

        _dhcpService.ServerStateChanged += (s, e) => UpdateStatus();
        _dhcpService.LogReceived += (s, log) => AddLog("INFO", log);
        _dhcpService.LeaseGranted += (s, lease) => UpdateStatus();

        UpdateStatus();
        LoadSettings();
    }

    [RelayCommand]
    private async Task StartServerAsync()
    {
        try
        {
            var settings = _settingsService.LoadSettings();
            await _dhcpService.StartAsync(settings);
            AddLog("SUCCESS", "DHCP Server started successfully");
        }
        catch (Exception ex)
        {
            AddLog("ERROR", $"Failed to start server: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task StopServerAsync()
    {
        try
        {
            await _dhcpService.StopAsync();
            AddLog("SUCCESS", "DHCP Server stopped");
        }
        catch (Exception ex)
        {
            AddLog("ERROR", $"Failed to stop server: {ex.Message}");
        }
    }

    private void UpdateStatus()
    {
        IsServerRunning = _dhcpService.IsRunning;
        ActiveLeases = _dhcpService.GetActiveLeases().Count();
    }

    private void LoadSettings()
    {
        var settings = _settingsService.LoadSettings();
        ServerIpAddress = settings.ServerIpAddress;
        IpPoolRange = $"{settings.PoolStartIp} - {settings.PoolEndIp}";
    }

    private void AddLog(string level, string message)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            RecentLogs.Insert(0, new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message
            });

            if (RecentLogs.Count > 50)
            {
                RecentLogs.RemoveAt(RecentLogs.Count - 1);
            }
        });
    }
}

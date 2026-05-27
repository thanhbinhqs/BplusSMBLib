using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DHCP.ManagementApp.Services;

namespace DHCP.ManagementApp.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IDhcpServerService _dhcpService;

    [ObservableProperty]
    private ViewModelBase _currentView;

    [ObservableProperty]
    private string _serverStatus = "Stopped";

    [ObservableProperty]
    private bool _isServerRunning;

    public DashboardViewModel DashboardViewModel { get; }
    public LeasesViewModel LeasesViewModel { get; }
    public StaticBindingsViewModel StaticBindingsViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }
    public LogsViewModel LogsViewModel { get; }

    public MainViewModel(
        IDhcpServerService dhcpService,
        DashboardViewModel dashboardViewModel,
        LeasesViewModel leasesViewModel,
        StaticBindingsViewModel staticBindingsViewModel,
        SettingsViewModel settingsViewModel,
        LogsViewModel logsViewModel)
    {
        _dhcpService = dhcpService;
        DashboardViewModel = dashboardViewModel;
        LeasesViewModel = leasesViewModel;
        StaticBindingsViewModel = staticBindingsViewModel;
        SettingsViewModel = settingsViewModel;
        LogsViewModel = logsViewModel;

        _currentView = DashboardViewModel;

        _dhcpService.ServerStateChanged += (s, e) => UpdateServerStatus();
        UpdateServerStatus();
    }

    [RelayCommand]
    private void NavigateToDashboard()
    {
        CurrentView = DashboardViewModel;
    }

    [RelayCommand]
    private void NavigateToLeases()
    {
        CurrentView = LeasesViewModel;
        LeasesViewModel.RefreshLeases();
    }

    [RelayCommand]
    private void NavigateToStaticBindings()
    {
        CurrentView = StaticBindingsViewModel;
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        CurrentView = SettingsViewModel;
    }

    [RelayCommand]
    private void NavigateToLogs()
    {
        CurrentView = LogsViewModel;
    }

    private void UpdateServerStatus()
    {
        IsServerRunning = _dhcpService.IsRunning;
        ServerStatus = IsServerRunning ? "Running" : "Stopped";
    }
}

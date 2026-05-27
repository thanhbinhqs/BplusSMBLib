using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DHCP.ManagementApp.Models;
using DHCP.ManagementApp.Services;

namespace DHCP.ManagementApp.ViewModels;

public partial class LeasesViewModel : ViewModelBase
{
    private readonly IDhcpServerService _dhcpService;

    [ObservableProperty]
    private ObservableCollection<LeaseDisplayModel> _leases = new();

    [ObservableProperty]
    private LeaseDisplayModel? _selectedLease;

    public LeasesViewModel(IDhcpServerService dhcpService)
    {
        _dhcpService = dhcpService;
        _dhcpService.LeaseGranted += (s, lease) => RefreshLeases();
        RefreshLeases();
    }

    [RelayCommand]
    public void RefreshLeases()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Leases.Clear();
            foreach (var lease in _dhcpService.GetActiveLeases())
            {
                Leases.Add(lease);
            }
        });
    }
}

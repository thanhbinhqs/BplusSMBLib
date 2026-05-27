using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DHCP.ManagementApp.Models;
using DHCP.ManagementApp.Services;

namespace DHCP.ManagementApp.ViewModels;

public partial class StaticBindingsViewModel : ViewModelBase
{
    private readonly IDhcpServerService _dhcpService;

    [ObservableProperty]
    private ObservableCollection<StaticBindingModel> _bindings = new();

    [ObservableProperty]
    private StaticBindingModel? _selectedBinding;

    [ObservableProperty]
    private string _newMacAddress = string.Empty;

    [ObservableProperty]
    private string _newIpAddress = string.Empty;

    [ObservableProperty]
    private string _newDescription = string.Empty;

    public StaticBindingsViewModel(IDhcpServerService dhcpService)
    {
        _dhcpService = dhcpService;
    }

    [RelayCommand]
    private void AddBinding()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(NewMacAddress) || string.IsNullOrWhiteSpace(NewIpAddress))
            {
                MessageBox.Show("MAC address and IP address are required", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_dhcpService.AddStaticBinding(NewMacAddress, NewIpAddress))
            {
                Bindings.Add(new StaticBindingModel
                {
                    MacAddress = NewMacAddress,
                    IpAddress = NewIpAddress,
                    Description = NewDescription,
                    CreatedAt = DateTime.Now
                });

                NewMacAddress = string.Empty;
                NewIpAddress = string.Empty;
                NewDescription = string.Empty;

                MessageBox.Show("Static binding added successfully", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Failed to add static binding", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void RemoveBinding()
    {
        if (SelectedBinding == null)
        {
            MessageBox.Show("Please select a binding to remove", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"Are you sure you want to remove the binding for {SelectedBinding.MacAddress}?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            if (_dhcpService.RemoveStaticBinding(SelectedBinding.MacAddress))
            {
                Bindings.Remove(SelectedBinding);
                MessageBox.Show("Static binding removed successfully", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Failed to remove static binding", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

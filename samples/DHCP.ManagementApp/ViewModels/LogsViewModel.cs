using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DHCP.ManagementApp.Models;
using DHCP.ManagementApp.Services;

namespace DHCP.ManagementApp.ViewModels;

public partial class LogsViewModel : ViewModelBase
{
    private readonly IDhcpServerService _dhcpService;

    [ObservableProperty]
    private ObservableCollection<LogEntry> _logs = new();

    public LogsViewModel(IDhcpServerService dhcpService)
    {
        _dhcpService = dhcpService;
        _dhcpService.LogReceived += (s, log) => AddLog(log);
    }

    [RelayCommand]
    private void ClearLogs()
    {
        Logs.Clear();
    }

    private void AddLog(string message)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Logs.Insert(0, new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = "INFO",
                Message = message
            });

            if (Logs.Count > 500)
            {
                Logs.RemoveAt(Logs.Count - 1);
            }
        });
    }
}

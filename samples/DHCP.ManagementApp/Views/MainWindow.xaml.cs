using System.Windows;
using DHCP.ManagementApp.ViewModels;

namespace DHCP.ManagementApp.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

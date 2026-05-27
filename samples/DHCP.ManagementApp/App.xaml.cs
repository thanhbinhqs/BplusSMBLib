using System.Windows;
using DHCP.Core;
using DHCP.Core.Engine;
using DHCP.Core.Extensions;
using DHCP.ManagementApp.Services;
using DHCP.ManagementApp.ViewModels;
using DHCP.ManagementApp.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace DHCP.ManagementApp;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File("logs/dhcp-management-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            var builder = Host.CreateApplicationBuilder();

            // Add Serilog
            builder.Services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.AddSerilog(Log.Logger);
            });

            // Add DHCP Server services
            builder.Services.AddDhcpServer(options =>
            {
                // Default configuration - will be overridden from settings
                options.Configuration = new DhcpServerConfiguration
                {
                    ServerIpAddress = System.Net.IPAddress.Parse("192.168.1.1"),
                    SubnetMask = System.Net.IPAddress.Parse("255.255.255.0"),
                    Gateway = System.Net.IPAddress.Parse("192.168.1.1"),
                    DnsServers = new[]
                    {
                        System.Net.IPAddress.Parse("8.8.8.8"),
                        System.Net.IPAddress.Parse("8.8.4.4")
                    },
                    DefaultLeaseTime = 86400,
                    DomainName = "local"
                };

                options.PoolStartIp = System.Net.IPAddress.Parse("192.168.1.100");
                options.PoolEndIp = System.Net.IPAddress.Parse("192.168.1.200");
                options.EnableActionBridge = true;
                options.ActionBridgePort = 8888;
            });

            // Add application services
            builder.Services.AddSingleton<ISettingsService, SettingsService>();
            builder.Services.AddSingleton<IDhcpServerService, DhcpServerService>();

            // Add ViewModels
            builder.Services.AddSingleton<MainViewModel>();
            builder.Services.AddSingleton<DashboardViewModel>();
            builder.Services.AddSingleton<LeasesViewModel>();
            builder.Services.AddSingleton<StaticBindingsViewModel>();
            builder.Services.AddSingleton<SettingsViewModel>();
            builder.Services.AddSingleton<LogsViewModel>();

            // Add Views
            builder.Services.AddSingleton<MainWindow>();

            _host = builder.Build();

            // Show main window
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application startup failed");
            MessageBox.Show($"Failed to start application: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }
}

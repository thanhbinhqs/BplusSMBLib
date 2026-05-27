using System.Net;
using DHCP.Core;
using DHCP.Core.Engine;
using DHCP.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DHCP.SampleServer;

/// <summary>
/// Sample DHCP Server Application
/// Demonstrates how to implement a complete DHCP server using DHCP.Core library
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("==============================================");
        Console.WriteLine("   DHCP Server - Sample Application");
        Console.WriteLine("   Built with DHCP.Core Library");
        Console.WriteLine("==============================================");
        Console.WriteLine();

        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File("logs/dhcp-server-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            // Add Serilog
            builder.Services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.ClearProviders();
                loggingBuilder.AddSerilog(Log.Logger);
            });

            // Configure DHCP Server
            builder.Services.AddDhcpServer(options =>
            {
                options.Configuration = new DhcpServerConfiguration
                {
                    ServerIpAddress = IPAddress.Parse("192.168.1.1"),
                    SubnetMask = IPAddress.Parse("255.255.255.0"),
                    Gateway = IPAddress.Parse("192.168.1.1"),
                    DnsServers = new[]
                    {
                        IPAddress.Parse("8.8.8.8"),
                        IPAddress.Parse("8.8.4.4")
                    },
                    DefaultLeaseTime = 86400, // 24 hours
                    MaxLeaseTime = 604800, // 7 days
                    DomainName = "example.local",
                    BroadcastAddress = IPAddress.Parse("192.168.1.255")
                };

                options.PoolStartIp = IPAddress.Parse("192.168.1.100");
                options.PoolEndIp = IPAddress.Parse("192.168.1.200");
                options.EnableActionBridge = true;
                options.ActionBridgePort = 8888;
            });

            // Add hosted service to manage server lifetime
            builder.Services.AddHostedService<DhcpServerHostedService>();

            var app = builder.Build();

            Console.WriteLine("Starting DHCP Server...");
            Console.WriteLine("Configuration:");
            Console.WriteLine("  Server IP:    192.168.1.1");
            Console.WriteLine("  Subnet Mask:  255.255.255.0");
            Console.WriteLine("  Gateway:      192.168.1.1");
            Console.WriteLine("  DNS Servers:  8.8.8.8, 8.8.4.4");
            Console.WriteLine("  IP Pool:      192.168.1.100 - 192.168.1.200");
            Console.WriteLine("  Lease Time:   24 hours");
            Console.WriteLine();
            Console.WriteLine("Press Ctrl+C to stop the server...");
            Console.WriteLine();

            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}

/// <summary>
/// Hosted service to manage DHCP server lifecycle
/// </summary>
class DhcpServerHostedService : IHostedService
{
    private readonly DhcpServerEngine _server;
    private readonly ILogger<DhcpServerHostedService> _logger;

    public DhcpServerHostedService(
        DhcpServerEngine server,
        ILogger<DhcpServerHostedService> logger)
    {
        _server = server;
        _logger = logger;

        // Subscribe to server events
        _server.PacketReceived += OnPacketReceived;
        _server.LeaseGranted += OnLeaseGranted;
        _server.LeaseReleased += OnLeaseReleased;
        _server.LogEmitted += OnLogEmitted;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting DHCP Server Hosted Service...");

        // Add some static bindings for demonstration
        _server.AddStaticBinding("00:11:22:33:44:55", IPAddress.Parse("192.168.1.50"));
        _server.AddStaticBinding("AA:BB:CC:DD:EE:FF", IPAddress.Parse("192.168.1.51"));

        _logger.LogInformation("Added static bindings:");
        _logger.LogInformation("  - 00:11:22:33:44:55 -> 192.168.1.50");
        _logger.LogInformation("  - AA:BB:CC:DD:EE:FF -> 192.168.1.51");

        await _server.StartAsync(cancellationToken);

        _logger.LogInformation("DHCP Server is running");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping DHCP Server Hosted Service...");

        await _server.StopAsync();

        _logger.LogInformation("DHCP Server stopped");

        // Display final statistics
        var leases = _server.GetAllLeases();
        _logger.LogInformation("Total leases managed: {Count}", leases.Count);
    }

    private void OnPacketReceived(object? sender, DHCP.Core.Models.DhcpPacket packet)
    {
        var messageType = packet.GetMessageType();
        var macAddress = packet.GetMacAddress();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] << {messageType,-10} from {macAddress}");
        Console.ResetColor();
    }

    private void OnLeaseGranted(object? sender, DhcpLease lease)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ LEASE GRANTED: {lease.IpAddress} -> {lease.MacAddress}");
        if (lease.IsStatic)
        {
            Console.WriteLine($"                    (Static Binding)");
        }
        else
        {
            Console.WriteLine($"                    (Expires: {lease.ExpiryTime:yyyy-MM-dd HH:mm:ss})");
        }
        Console.ResetColor();

        // Display current leases
        var activeLeases = _server.GetActiveLeases();
        Console.WriteLine($"                    Active leases: {activeLeases.Count}");
        Console.WriteLine();
    }

    private void OnLeaseReleased(object? sender, DhcpLease lease)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ LEASE RELEASED: {lease.IpAddress} -> {lease.MacAddress}");
        Console.ResetColor();
        Console.WriteLine();
    }

    private void OnLogEmitted(object? sender, string message)
    {
        // Additional custom logging if needed
        _logger.LogInformation("Server: {Message}", message);
    }
}

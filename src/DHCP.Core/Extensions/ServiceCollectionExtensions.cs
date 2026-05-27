using System.Net;
using DHCP.Core.Actions;
using DHCP.Core.Engine;
using DHCP.Core.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DHCP.Core.Extensions;

/// <summary>
/// Extension methods for configuring DHCP services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add DHCP server services to the dependency injection container
    /// </summary>
    public static IServiceCollection AddDhcpServer(
        this IServiceCollection services,
        Action<DhcpServerOptions> configureOptions)
    {
        var options = new DhcpServerOptions();
        configureOptions(options);

        // Validate options
        ValidateOptions(options);

        // Register configuration
        services.AddSingleton(options.Configuration);

        // Register network components
        services.AddSingleton<DhcpListener>();

        // Register allocation engine
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<IpAllocationEngine>>();
            return new IpAllocationEngine(
                logger,
                options.Configuration,
                options.PoolStartIp,
                options.PoolEndIp
            );
        });

        // Register main server engine
        services.AddSingleton<DhcpServerEngine>();

        // Register action bridge if enabled
        if (options.EnableActionBridge)
        {
            services.AddSingleton<IActionBridge, TcpActionBridge>();
        }

        return services;
    }

    private static void ValidateOptions(DhcpServerOptions options)
    {
        if (options.Configuration.ServerIpAddress.Equals(IPAddress.Any))
        {
            throw new ArgumentException("Server IP address must be specified");
        }

        if (options.PoolStartIp.Equals(IPAddress.Any) || options.PoolEndIp.Equals(IPAddress.Any))
        {
            throw new ArgumentException("IP pool range must be specified");
        }

        // Validate pool range
        var startBytes = options.PoolStartIp.GetAddressBytes();
        var endBytes = options.PoolEndIp.GetAddressBytes();

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(startBytes);
            Array.Reverse(endBytes);
        }

        var startInt = BitConverter.ToUInt32(startBytes, 0);
        var endInt = BitConverter.ToUInt32(endBytes, 0);

        if (endInt < startInt)
        {
            throw new ArgumentException("Pool end IP must be greater than or equal to start IP");
        }
    }
}

/// <summary>
/// Configuration options for DHCP server
/// </summary>
public sealed class DhcpServerOptions
{
    /// <summary>
    /// DHCP server configuration
    /// </summary>
    public DhcpServerConfiguration Configuration { get; set; } = new();

    /// <summary>
    /// Start of dynamic IP pool
    /// </summary>
    public IPAddress PoolStartIp { get; set; } = IPAddress.Any;

    /// <summary>
    /// End of dynamic IP pool
    /// </summary>
    public IPAddress PoolEndIp { get; set; } = IPAddress.Any;

    /// <summary>
    /// Enable TCP action bridge for remote client management
    /// </summary>
    public bool EnableActionBridge { get; set; } = false;

    /// <summary>
    /// Action bridge listening port (default: 8888)
    /// </summary>
    public int ActionBridgePort { get; set; } = 8888;
}

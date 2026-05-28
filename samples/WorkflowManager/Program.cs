using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using WorkflowManager.Services;
using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Protocol.SMB;
using SmbEnterprise.Protocol.SMB.Connection;

namespace WorkflowManager;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File("logs/workflow-manager-.txt", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            // Setup DI container
            var services = new ServiceCollection();
            ConfigureServices(services);
            var serviceProvider = services.BuildServiceProvider();

            // Configure application
            ApplicationConfiguration.Initialize();

            // Run main form
            var mainForm = serviceProvider.GetRequiredService<MainForm>();
            Application.Run(mainForm);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            MessageBox.Show($"Fatal error: {ex.Message}", "Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });

        // SMB Infrastructure
        services.AddSingleton<SmbSessionPool>();
        services.AddTransient<IRemoteFileSystem, SmbFileSystem>();

        // Services
        services.AddSingleton<HashFileReader>();

        services.AddSingleton<PackageScannerService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<PackageScannerService>>();
            var factory = new Func<IRemoteFileSystem>(() => sp.GetRequiredService<IRemoteFileSystem>());
            return new PackageScannerService(logger, factory);
        });

        services.AddSingleton<PackageCopyService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<PackageCopyService>>();
            var factory = new Func<IRemoteFileSystem>(() => sp.GetRequiredService<IRemoteFileSystem>());
            var hashReader = sp.GetRequiredService<HashFileReader>();
            return new PackageCopyService(logger, factory, hashReader);
        });

        services.AddSingleton<MultiDestinationCopyService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<MultiDestinationCopyService>>();
            var hashReader = sp.GetRequiredService<HashFileReader>();
            var factory = new Func<IRemoteFileSystem>(() => sp.GetRequiredService<IRemoteFileSystem>());
            return new MultiDestinationCopyService(logger, hashReader, factory);
        });

        // Forms
        services.AddTransient<MainForm>();
    }

}
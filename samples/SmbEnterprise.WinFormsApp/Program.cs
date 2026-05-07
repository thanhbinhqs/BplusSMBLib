using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using SmbEnterprise.Persistence;
using SmbEnterprise.Protocol.SMB;
using SmbEnterprise.Transfer;
using SmbEnterprise.Checksum;
using SmbEnterprise.Diagnostics;
using SmbEnterprise.Cache;
using SmbEnterprise.WinFormsApp.Services;
using SmbEnterprise.WinFormsApp.Themes;
using SmbEnterprise.WinFormsApp.Transfer;

namespace SmbEnterprise.WinFormsApp;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        var services = new ServiceCollection();
        var uiLogStore = new UiLogStore();
        services.AddSingleton(uiLogStore);

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmbEnterprise",
            "logs",
            "winforms-.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .WriteTo.Sink(new UiLogSink(uiLogStore))
            .CreateLogger();

        services.AddLogging(b =>
        {
            b.AddSerilog(dispose: true);
        });

        services.AddSmbProvider();
        services.AddChecksumEngine(Core.Models.ChecksumAlgorithm.XxHash64);
        services.AddSingleton<TransferTelemetry>();
        services.AddSingleton<TransferDashboard>();
        services.AddSingleton<MetadataCache>(sp => new MetadataCache(
            new MetadataCacheOptions(),
            sp.GetRequiredService<ILogger<MetadataCache>>()));
        services.AddSqlitePersistence();

        services.AddSingleton<AppThemeManager>();
        services.AddSingleton<TransferViewModel>();
        services.AddSingleton<TransferQueueController>();
        services.AddTransient<MainForm>();
        services.AddTransient<FullTestForm>();

        Application.ThreadException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            MessageBox.Show(
                "Đã xảy ra lỗi không mong muốn trong giao diện. Vui lòng xem tab Logs để biết chi tiết.",
                "Lỗi ứng dụng",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                Log.Fatal(ex, "Unhandled non-UI exception");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        var sp = services.BuildServiceProvider();

        var mainForm = sp.GetRequiredService<MainForm>();
        Application.Run(mainForm);

        Log.CloseAndFlush();
    }
}

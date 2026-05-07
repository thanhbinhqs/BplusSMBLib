using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace SmbEnterprise.Core.Logging;

/// <summary>
/// Factory for creating structured loggers with correlation ID and session ID enrichment.
/// </summary>
public static class SmbLoggerFactory
{
    public static ILoggerFactory Create(LogLevel minimumLevel = LogLevel.Information, string? logFilePath = null)
    {
        var logConfig = new LoggerConfiguration()
            .MinimumLevel.Is(ToSerilogLevel(minimumLevel))
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {SessionId} {Message:lj}{NewLine}{Exception}");

        if (!string.IsNullOrEmpty(logFilePath))
        {
            logConfig = logConfig.WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate:
                    "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {CorrelationId} {SessionId} {Message:lj}{NewLine}{Exception}");
        }

        Log.Logger = logConfig.CreateLogger();
        return new SerilogLoggerFactory(Log.Logger, dispose: true);
    }

    private static Serilog.Events.LogEventLevel ToSerilogLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => Serilog.Events.LogEventLevel.Verbose,
        LogLevel.Debug => Serilog.Events.LogEventLevel.Debug,
        LogLevel.Information => Serilog.Events.LogEventLevel.Information,
        LogLevel.Warning => Serilog.Events.LogEventLevel.Warning,
        LogLevel.Error => Serilog.Events.LogEventLevel.Error,
        LogLevel.Critical => Serilog.Events.LogEventLevel.Fatal,
        _ => Serilog.Events.LogEventLevel.Information
    };
}

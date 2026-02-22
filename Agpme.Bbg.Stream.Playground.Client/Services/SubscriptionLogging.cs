using Serilog;
using Serilog.Events;

namespace Agpme.Bbg.Stream.Playground.Client.Services;

public static class SubscriptionLogging
{
    /// <summary>
    /// Creates a per-subscription logger that writes to:
    /// Logs/subscriptions/{entityType}_{entityName}.log
    /// The caller is responsible for disposing the returned logger.
    /// </summary>
    public static Serilog.ILogger CreateSubscriptionLogger(string entityType, string entityName)
    {
        var safeId = $"{entityType}_{entityName}"
            .Replace('/', '_')
            .Replace('\\', '_')
            .Replace(':', '_')
            .Replace('*', '_')
            .Replace('?', '_')
            .Replace('"', '_')
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace('|', '_');

        var folder = Path.Combine("Logs", "subscriptions");
        Directory.CreateDirectory(folder);
        var filePath = Path.Combine(folder, $"{safeId}.log");

        var logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("Subscription", safeId)
            .WriteTo.File(
                filePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true,
                restrictedToMinimumLevel: LogEventLevel.Information,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties}{NewLine}{Exception}")
            .CreateLogger();

        return logger;
    }
}
using Microsoft.Extensions.Configuration;


var cfg = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

var cmd = args.FirstOrDefault()?.ToLowerInvariant() ?? "bootstrap";
// bootstrap = up + apply-sql + sync-metadata

try
{
    switch (cmd)
    {
        case "bootstrap":
            await Bootstrap.UpAsync(cfg);
            await Bootstrap.ApplySqlAsync(cfg);
            await Bootstrap.SyncMetadataAsync(cfg);
            break;
        case "up":
            await Bootstrap.UpAsync(cfg);
            break;
        case "apply-sql":
            await Bootstrap.ApplySqlAsync(cfg);
            break;
        case "sync-metadata":
            await Bootstrap.SyncMetadataAsync(cfg);
            break;
        case "reset":
            await Bootstrap.DownAsync(cfg, removeVolumes: true);
            await Bootstrap.UpAsync(cfg);
            await Bootstrap.ApplySqlAsync(cfg);
            break;
        case "down":
            await Bootstrap.DownAsync(cfg, removeVolumes: true);
            break;
        default:
            Console.WriteLine("Usage: dotnet run -- [bootstrap|up|apply-sql|sync-metadata|reset|down]");
            break;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
    Environment.ExitCode = 1;
}
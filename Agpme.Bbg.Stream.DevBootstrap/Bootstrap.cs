using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Agpme.Bbg.Stream.Playground.Shared.Postgres;
using Agpme.Bbg.Stream.Playground.Shared.Aws;

public static class Bootstrap
{
    public static async Task UpAsync(IConfiguration cfg)
    {
        var compose = cfg["Docker:ComposeFile"]!;
        RunDockerCompose(["compose", "-f", compose, "up", "-d"]);

        // Sanity probe (match your fixture’s intent: open connection + quick query)
        var cs = cfg["LocalDb:ConnectionString"]!;
        Console.WriteLine($"[INFO] LocalDb.ConnectionString = {cs}");
        await WaitForDbAsync(cs, TimeSpan.FromMinutes(2));
        await PrintServerIdentityAsync(cs);
        Console.WriteLine("[OK] Postgres is ready.");
    }

    public static Task ApplySqlAsync(IConfiguration cfg)
    {
        var cs = cfg["LocalDb:ConnectionString"]!;
        var folder = cfg["DbScripts:Folder"]!;
        return SqlScriptsApplier.ApplyAsync(cs, folder, Console.WriteLine);
    }


    public static async Task SyncMetadataAsync(IConfiguration cfg)
    {
        // Enabled?
        if (!(bool.TryParse(cfg["MetadataSync:Enabled"], out var enabled) && enabled))
        {
            Console.WriteLine("[Skip] MetadataSync.Enabled=false");
            return;
        }

        // Destination CS (local)
        var destCs = cfg["LocalDb:ConnectionString"]
                     ?? throw new InvalidOperationException("LocalDb:ConnectionString is not set.");

        // Tables (NO Binder): GetChildren → Select → ToArray
        var tables = cfg.GetSection("MetadataSync:Tables")
                        .GetChildren()
                        .Select(s => s.Value)
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .ToArray();

        if (tables.Length == 0)
            throw new InvalidOperationException("MetadataSync:Tables is empty.");

        var truncate = bool.TryParse(cfg["MetadataSync:TruncateBeforeCopy"], out var t) && t;

        // Source connection string: either use AWS Secret or a direct value
        string sourceCs;

        // Preferred: UseAwsSecret block moved to ROOT under "MetadataAwsSecrets"
        var useSecret = bool.TryParse(cfg["MetadataAwsSecrets:UseAwsSecret"], out var us) && us;

        if (useSecret)
        {
            var arn = cfg["MetadataAwsSecrets:Arn"];
            var keyName = cfg["MetadataAwsSecrets:KeyName"];
            var region = cfg["MetadataAwsSecrets:Region"];
            // Optional; defaults via ResolveProfileForEnv if you want
            var profile = cfg["MetadataAwsSecrets:Profile"];

            if (string.IsNullOrWhiteSpace(arn) || string.IsNullOrWhiteSpace(keyName))
                throw new InvalidOperationException("MetadataAwsSecrets.Arn/KeyName must be configured.");

            // Use the Shared helper you already have
            sourceCs = await AwsSecretHelper.GetSecretValueByKeyAsync(
                profile, arn!, keyName!, region);
        }
        else
        {
            sourceCs = cfg["MetadataSourceConnectionString"]
                       ?? throw new InvalidOperationException(
                            "MetadataSourceConnectionString is not set and UseAwsSecret=false.");
        }

        // Copy small metadata tables from source (DEV) → local
        await PostgresSyncHelper.CopyTablesAsync(sourceCs, destCs, tables, truncate);
        Console.WriteLine("[OK] Metadata synchronized.");
    }

    public static async Task DownAsync(IConfiguration cfg, bool removeVolumes)
    {
        var compose = cfg["Docker:ComposeFile"]!;
        var args = removeVolumes
            ? new[] { "compose", "-f", compose, "down", "-v" }
            : new[] { "compose", "-f", compose, "down" };

        RunDockerCompose(args);
        await Task.Delay(500);
        Console.WriteLine("[OK] Docker compose down.");
    }

    // ---- helpers (close to your fixture’s sanity probe) ----
    private static async Task PrintServerIdentityAsync(string cs, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(ct);

        const string sql = """
        SELECT current_database(),
               current_user,
               inet_server_addr()::text,
               inet_server_port(),
               current_setting('server_version'),
               current_setting('search_path')
    """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);

        if (await rdr.ReadAsync(ct))
        {
            var db = rdr.GetString(0);
            var user = rdr.GetString(1);
            var ip = rdr.IsDBNull(2) ? "(null)" : rdr.GetString(2);
            var port = rdr.IsDBNull(3) ? "0" : rdr.GetInt32(3).ToString();
            var version = rdr.IsDBNull(4) ? "(unknown)" : rdr.GetString(4);
            var searchPath = rdr.IsDBNull(5) ? "(unknown)" : rdr.GetString(5);

            Console.WriteLine($"DB          : {db}");
            Console.WriteLine($"User        : {user}");
            Console.WriteLine($"Server (ip) : {ip}");
            Console.WriteLine($"Server port : {port}");
            Console.WriteLine($"Version     : {version}");
            Console.WriteLine($"Search path : {searchPath}");
        }
    }

    private static void RunDockerCompose(string[] args)
    {
        // Prefer `docker compose`, fallback to legacy `docker-compose`
        var ok = TryRun("docker", args) || TryRun("docker-compose", args.Skip(1).ToArray());
        if (!ok) throw new InvalidOperationException("Failed to execute docker compose.");
    }

    private static bool TryRun(string file, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file, string.Join(' ', args))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(stdout)) Console.WriteLine(stdout);
            if (!string.IsNullOrWhiteSpace(stderr)) Console.WriteLine(stderr);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static async Task WaitForDbAsync(string cs, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        Exception? last = null;
        int attempt = 0;
        while (sw.Elapsed < timeout)
        {
            try
            {
                await using var conn = new NpgsqlConnection(cs);
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand("select 1", conn);
                await cmd.ExecuteScalarAsync();
                Console.WriteLine("[INFO] DB connection established.");
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                attempt++;
                Console.WriteLine($"[WAIT] DB not ready (attempt {attempt}) : {ex.Message}");
                await Task.Delay(1500);
            }
        }
        throw new TimeoutException($"DB not ready within {timeout}. Last error: {last?.Message}");
    }
}

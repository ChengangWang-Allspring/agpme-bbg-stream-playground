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

        var cs = cfg["LocalPlaygroundDb:ConnectionString"]!;
        Console.WriteLine($"[INFO] Waiting for Postgres at {cs}");

        await WaitForDbAsync(cs, TimeSpan.FromMinutes(2));
        await PrintServerIdentityAsync(cs);

        Console.WriteLine("[OK] Postgres is ready.");
    }

    public static Task ApplySqlAsync(IConfiguration cfg)
    {
        var cs = cfg["LocalPlaygroundDb:ConnectionString"]!;
        var folder = cfg["DbScripts:Folder"]!;
        return SqlScriptsApplier.ApplyAsync(cs, folder, Console.WriteLine);
    }

    public static async Task SyncMetadataAsync(IConfiguration cfg)
    {
        // Strictly use MetadataSync keys
        var enabled = bool.Parse(cfg["MetadataSync:Enabled"]!);
        if (!enabled)
        {
            Console.WriteLine("[Skip] MetadataSync.Enabled=false");
            return;
        }

        var destCs = cfg["LocalPlaygroundDb:ConnectionString"]!;
        var truncate = bool.Parse(cfg["MetadataSync:TruncateBeforeCopy"]!);

        var tables = cfg.GetSection("MetadataSync:Tables")
                        .GetChildren()
                        .Select(s => s.Value!)
                        .ToArray();

        Console.WriteLine("[INFO] Syncing metadata tables:");
        foreach (var t in tables) Console.WriteLine($"  - {t}");

        // Always use AWS secrets from MetadataAwsSecrets
        var arn = cfg["MetadataAwsSecrets:Arn"]!;
        var keyName = cfg["MetadataAwsSecrets:KeyName"]!;
        var region = cfg["MetadataAwsSecrets:Region"]!;
        var profile = cfg["MetadataAwsSecrets:Profile"]!;

        Console.WriteLine($"[INFO] Retrieving source connection from AWS Secret: {arn}");
        var sourceCs = await AwsSecretHelper.GetSecretValueByKeyAsync(profile, arn, keyName, region);

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
        await Task.Delay(300);
        Console.WriteLine("[OK] Docker compose down.");
    }

    // --------------------------------------------------------------------
    // Helpers (no optional config usage here either)
    // --------------------------------------------------------------------

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
            Console.WriteLine($"Port        : {port}");
            Console.WriteLine($"Version     : {version}");
            Console.WriteLine($"Search path : {searchPath}");
        }
    }

    private static void RunDockerCompose(string[] args)
    {
        var ok = TryRun("docker", args)
              || TryRun("docker-compose", args.Skip(1).ToArray());

        if (!ok)
            throw new InvalidOperationException("Failed to execute docker compose.");
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

            Console.Write(p.StandardOutput.ReadToEnd());
            Console.Write(p.StandardError.ReadToEnd());

            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task WaitForDbAsync(string cs, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        Exception? last = null;
        var attempt = 0;

        while (sw.Elapsed < timeout)
        {
            try
            {
                attempt++;

                await using var conn = new NpgsqlConnection(cs);
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand("select 1", conn);
                await cmd.ExecuteScalarAsync();

                Console.WriteLine("[INFO] Database connection succeeded.");
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                Console.WriteLine($"[WAIT] Database not ready (attempt {attempt}): {ex.Message}");
                await Task.Delay(1500);
            }
        }

        throw new TimeoutException(
            $"Database was not ready within {timeout}. Last error: {last?.Message}");
    }
}
using System.Diagnostics;
using Npgsql;

namespace Agpme.Bbg.Playground.Shared.Postgres;

public static class SqlScriptsApplier
{
    public static async Task ApplyAsync(string connectionString, string scriptsDir, Action<string>? log = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(scriptsDir))
            throw new DirectoryNotFoundException($"Scripts folder not found: {scriptsDir}");

        var files = Directory.GetFiles(scriptsDir, "*.sql", SearchOption.TopDirectoryOnly)
                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                             .ToArray();
        if (files.Length == 0)
            throw new FileNotFoundException($"No .sql files found under: {scriptsDir}");

        log?.Invoke($"Found {files.Length} script(s) in {scriptsDir}:");
        foreach (var f in files) log?.Invoke($" - {Path.GetFileName(f)}");

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        int applied = 0;
        var total = Stopwatch.StartNew();

        foreach (var file in files)
        {
            var sw = Stopwatch.StartNew();
            var name = Path.GetFileName(file);
            try
            {
                var sql = await File.ReadAllTextAsync(file, ct);
                if (string.IsNullOrWhiteSpace(sql))
                {
                    log?.Invoke($"SKIP (empty): {name}");
                    continue;
                }

                await using var tx = await conn.BeginTransactionAsync(ct);
                await using (var cmd = new NpgsqlCommand(sql, conn, tx))
                    await cmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);

                sw.Stop(); applied++;
                log?.Invoke($"OK ({sw.ElapsedMilliseconds} ms): {name}");
            }
            catch (Exception ex)
            {
                sw.Stop();
                log?.Invoke($"FAIL ({sw.ElapsedMilliseconds} ms): {name}");
                log?.Invoke($"{ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        total.Stop();
        log?.Invoke($"Applied {applied}/{files.Length} script(s) in {total.ElapsedMilliseconds} ms.");
    }
}
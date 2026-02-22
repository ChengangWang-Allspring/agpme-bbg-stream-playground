// Shared/Postgres/PostgresSyncHelper.cs
using System.Buffers;
using System.Text.RegularExpressions;
using Npgsql;

namespace Agpme.Bbg.Stream.Playground.Shared.Postgres;

public static class PostgresSyncHelper
{
    private static readonly Regex SafeIdent =
        new(@"^[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public static async Task CopyTablesAsync(
        string sourceConnectionString,
        string destinationConnectionString,
        IEnumerable<string> schemaQualifiedTables,
        bool truncateBeforeCopy = true,
        CancellationToken ct = default)
    {
        // Guard: disallow same endpoint (accidental destructive copy)
        var src = new NpgsqlConnectionStringBuilder(sourceConnectionString);
        var dst = new NpgsqlConnectionStringBuilder(destinationConnectionString);
        bool sameEndpoint = src.Host.Equals(dst.Host, StringComparison.OrdinalIgnoreCase)
                            && src.Port == dst.Port
                            && src.Database.Equals(dst.Database, StringComparison.OrdinalIgnoreCase);
        if (sameEndpoint)
            throw new InvalidOperationException(
                "Source and destination endpoints are identical (host/port/database). Aborting metadata sync.");

        foreach (var table in schemaQualifiedTables)
        {
            await CopyTableAsync(sourceConnectionString, destinationConnectionString, table, truncateBeforeCopy, ct);
        }
    }

    private static async Task CopyTableAsync(
        string sourceCs, string destCs, string table, bool truncate, CancellationToken ct)
    {
        if (!SafeIdent.IsMatch(table))
            throw new ArgumentException($"Invalid schema-qualified table name: '{table}'");

        await using var src = new NpgsqlConnection(sourceCs);
        await using var dst = new NpgsqlConnection(destCs);
        await src.OpenAsync(ct);
        await dst.OpenAsync(ct);

        if (truncate)
        {
            await using var tr = new NpgsqlCommand($"TRUNCATE TABLE {table} RESTART IDENTITY CASCADE;", dst);
            await tr.ExecuteNonQueryAsync(ct);
        }

        await using var outStream =
            await src.BeginRawBinaryCopyAsync($"COPY {table} TO STDOUT (FORMAT BINARY)", ct);
        await using var inStream =
            await dst.BeginRawBinaryCopyAsync($"COPY {table} FROM STDIN (FORMAT BINARY)", ct);

        var buf = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            int read;
            while ((read = await outStream.ReadAsync(buf.AsMemory(0, buf.Length), ct)) > 0)
                await inStream.WriteAsync(buf.AsMemory(0, read), ct);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }
}
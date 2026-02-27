using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace Agpme.Bbg.Playground.Subscriptions.Api.Services;

public interface IResetService
{
    Task ResetPositionsAsync(CancellationToken ct);
}

public sealed class ResetService : IResetService
{
    private readonly string _cs;

    public ResetService(IConfiguration cfg)
    {
        _cs = cfg["ConnectionString_Local"]
            ?? throw new InvalidOperationException("ConnectionString_Local not configured.");
    }

    public async Task ResetPositionsAsync(CancellationToken ct)
    {
        // Using TRUNCATE with RESTART IDENTITY + CASCADE to keep it fast and consistent.
        const string sql = """
            TRUNCATE TABLE app_data.bbg_positions_inbound RESTART IDENTITY CASCADE;
            TRUNCATE TABLE app_data.bbg_positions         RESTART IDENTITY CASCADE;
        """;

        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);

        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var cmd = new NpgsqlCommand(sql, conn, tx))
        {
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }
}
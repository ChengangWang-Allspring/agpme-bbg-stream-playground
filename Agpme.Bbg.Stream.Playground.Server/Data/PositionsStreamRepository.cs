using Microsoft.Extensions.Logging;
using Serilog;
using Npgsql;
using Agpme.Bbg.Stream.Playground.Shared.Streaming;

namespace Agpme.Bbg.Stream.Playground.Server.Data;

public sealed class PositionsStreamRepository : IPositionsStreamRepository
{
    private readonly Task<NpgsqlDataSource> _dataSourceTask;

    public PositionsStreamRepository(Task<NpgsqlDataSource> dataSourceTask, ILogger<PositionsStreamRepository> logger)
    {
        _dataSourceTask = dataSourceTask;
    }

    public async Task<(int lastStreamOrder, string? msgRequestId, List<string> jsonRows)>
        GetInitialAsync(string entityType, string entityName, DateOnly asOfDate, CancellationToken ct)
    {
        Log.Information("GetInitialAsync(entityType={EntityType}, entityName={EntityName}, asOf={AsOf})",
            entityType, entityName, asOfDate);

        var ds = await _dataSourceTask;
        await using var conn = await ds.OpenConnectionAsync(ct);

        var rows = new List<(int streamOrder, string msgId, string json)>();
        await using (var cmd = new NpgsqlCommand(StreamSql.InitialQuery, conn))
        {
            cmd.Parameters.AddWithValue("asOfDate", asOfDate);
            cmd.Parameters.AddWithValue("entityName", entityName);
            cmd.Parameters.AddWithValue("entityType", entityType);
            cmd.Parameters.AddWithValue("action", "initial");

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rows.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }

        if (rows.Count == 0)
        {
            Log.Information("Initial paint returned 0 rows.");
            return (0, null, new List<string>());
        }

        var last = rows[^1].streamOrder;
        var msgId = rows[^1].msgId;
        Log.Information("Initial paint returned {Count} rows; lastStreamOrder={Last}, msg_request_id={MsgId}.",
            rows.Count, last, msgId);

        var jsons = rows.Select(r => r.json).ToList();
        return (last, msgId, jsons);
    }

    public async Task<List<(int streamOrder, string json)>> GetUpdatesAsync(
        string entityType, string entityName, DateOnly asOfDate, string msgRequestId, int lastStreamOrder, CancellationToken ct)
    {
        Log.Information("GetUpdatesAsync(entityType={EntityType}, entityName={EntityName}, asOf={AsOf}, msgId={MsgId}, last={Last})",
            entityType, entityName, asOfDate, msgRequestId, lastStreamOrder);

        var ds = await _dataSourceTask;
        await using var conn = await ds.OpenConnectionAsync(ct);

        var updates = new List<(int streamOrder, string json)>();
        await using (var cmd = new NpgsqlCommand(StreamSql.UpdateQuery, conn))
        {
            cmd.Parameters.AddWithValue("asOfDate", asOfDate);
            cmd.Parameters.AddWithValue("entityName", entityName);
            cmd.Parameters.AddWithValue("entityType", entityType);
            cmd.Parameters.AddWithValue("stream_order", lastStreamOrder);
            cmd.Parameters.AddWithValue("msg_request_id", msgRequestId);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                updates.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        Log.Information("Updates returned {Count} row(s).", updates.Count);
        return updates;
    }
}

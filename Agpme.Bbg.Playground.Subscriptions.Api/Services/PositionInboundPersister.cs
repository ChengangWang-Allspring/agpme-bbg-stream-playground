using System.Data;
using Newtonsoft.Json.Linq;
using Npgsql;
using NpgsqlTypes;
using Serilog;
using Agpme.Bbg.Playground.Subscriptions.Api.Models;
using Microsoft.Extensions.Configuration;

namespace Agpme.Bbg.Playground.Subscriptions.Api.Services;

public sealed class PositionInboundPersister : IPositionInboundPersister
{
    private readonly string _cs;
    private readonly Serilog.ILogger _log;

    // Cache for inbound column map (thread-safe lazy init)
    private readonly object _mapLock = new();
    private volatile Task<List<ColMap>>? _mapTask = null;


    public PositionInboundPersister(IConfiguration cfg)
    {
        _cs = cfg.GetSection("ClientDb:ConnectionString").Value
              ?? throw new InvalidOperationException("ClientDb:ConnectionString is not configured.");
        _log = Log.ForContext<PositionInboundPersister>();
    }

    private sealed record ColMap(string SourceColumn, string SourceKind);

    private Task<List<ColMap>> GetInboundColumnMapCachedAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        // Fast path: already initiated
        var task = _mapTask;
        if (task is not null) return task;

        lock (_mapLock)
        {
            if (_mapTask is not null) return _mapTask;

            // Create the load Task ONCE; all threads will await the same Task
            _mapTask = GetInboundColumnMapAsync(conn, ct);
            return _mapTask;
        }
    }

    private static async Task<List<ColMap>> GetInboundColumnMapAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        const string sql = @"
            select distinct source_column, source_kind
            from app_config.bbg_positions_inbound_cols_map
            where source_kind in ('json','loader');";
        var cols = new List<ColMap>(capacity: 64);

        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 120 };
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        var iCol = rdr.GetOrdinal("source_column");
        var iKind = rdr.GetOrdinal("source_kind");
        while (await rdr.ReadAsync(ct))
        {
            var col = rdr.IsDBNull(iCol) ? null : rdr.GetString(iCol);
            var kind = rdr.IsDBNull(iKind) ? null : rdr.GetString(iKind);
            if (!string.IsNullOrWhiteSpace(col) && !string.IsNullOrWhiteSpace(kind))
                cols.Add(new ColMap(col!, kind!));
        }
        return cols;
    }


    public Task RefreshColumnMapAsync(CancellationToken ct = default)
    {
        lock (_mapLock)
        {
            _mapTask = null; // next call will re-load
        }
        _log.Information("Inbound column map cache cleared by request.");
        return Task.CompletedTask;
    }


    private static object? GetLoaderValueText(SubscriptionKey key, DateOnly asOf, string colName, bool isIntraday, string? accountFromJson)
        => colName.ToLowerInvariant() switch
        {
            "as_of_date" => asOf, // DATE type will be used when writing
            "load_bb_entity_type" => key.entityType,
            "load_bb_entity_name" => key.entityName,
            "load_bb_action" => isIntraday ? null : "initial",
            "load_bb_uuid" => null,
            "load_process" => "playground-client",
            "msg_request_id" => null,
            "is_intraday" => isIntraday ? "true" : "false",
            "account" or "account_id" => accountFromJson,
            _ => null
        };

    private static string? GetJsonTextOrNull(JObject j, string sourceColumn)
    {
        var key = sourceColumn?.ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (j.TryGetValue(key, out var token) && token.Type != JTokenType.Null)
            return token.Type == JTokenType.String ? token.Value<string>()! : token.ToString();
        return null;
    }

    // -------------------- Initial paint (batch) --------------------

    public async Task PersistInitialBatchToInboundAsync(List<string> jsons, SubscriptionKey key, DateOnly asOf, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);

        var map = await GetInboundColumnMapCachedAsync(conn, ct);
        var colList = string.Join(", ", map.Select(m => $"\"{m.SourceColumn}\""));
        var copySql = $"COPY app_data.bbg_positions_inbound ({colList}) FROM STDIN (FORMAT BINARY)";

        _log.Information("[INBOUND BEFORE] Initial batch → rows={Count}, entity={EntityType}/{EntityName}, as_of={AsOf}",
                         jsons.Count, key.entityType, key.entityName, asOf);

        await using (var writer = await conn.BeginBinaryImportAsync(copySql, ct))
        {
            foreach (var json in jsons)
            {
                await writer.StartRowAsync(ct);

                var j = string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);
                var accountFromJson = GetJsonTextOrNull(j, "ACCOUNT");

                foreach (var m in map)
                {
                    if (m.SourceKind.Equals("loader", StringComparison.OrdinalIgnoreCase))
                    {
                        if (m.SourceColumn.Equals("as_of_date", StringComparison.OrdinalIgnoreCase))
                            await writer.WriteAsync(asOf, NpgsqlDbType.Date, ct);
                        else
                            await writer.WriteAsync(GetLoaderValueText(key, asOf, m.SourceColumn, isIntraday: false, accountFromJson), NpgsqlDbType.Text, ct);
                    }
                    else // json
                    {
                        var val = GetJsonTextOrNull(j, m.SourceColumn);
                        await writer.WriteAsync(val, NpgsqlDbType.Text, ct);
                    }
                }
            }
            await writer.CompleteAsync(ct);
        }

        _log.Information("[INBOUND AFTER] Initial batch persisted → rows={Count}, entity={EntityType}/{EntityName}, as_of={AsOf}",
                         jsons.Count, key.entityType, key.entityName, asOf);
    }

    public async Task CallUpsertInitialAsync(SubscriptionKey key, DateOnly asOf, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);

        const string procName = "app_data.bbg_upsert_positions_from_inbound";

        _log.Information("[UPSERT BEFORE] Initial → proc={Proc}, p_is_intraday={Intraday}, p_load_process={ProcName}, p_as_of_date={AsOf}, p_account_id_intraday=null, p_load_bb_entity_name={EntityName}",
                         procName, false, "playground-client", asOf, key.entityName);

        await using var call = new NpgsqlCommand(
            $"CALL {procName} (@p_is_intraday, @p_load_process, @p_as_of_date, @p_account_id_intraday, @p_load_bb_entity_name)", conn);
        call.Parameters.AddWithValue("p_is_intraday", false);
        call.Parameters.AddWithValue("p_load_process", "playground-client");
        call.Parameters.Add("p_as_of_date", NpgsqlDbType.Date).Value = asOf;
        call.Parameters.AddWithValue("p_account_id_intraday", DBNull.Value);
        call.Parameters.AddWithValue("p_load_bb_entity_name", key.entityName);
        await call.ExecuteNonQueryAsync(ct);

        _log.Information("[UPSERT AFTER] Initial complete → entity={EntityType}/{EntityName}, as_of={AsOf}",
                         key.entityType, key.entityName, asOf);
    }

    // -------------------- Intraday (one-by-one) --------------------

    public async Task PersistIntradayToInboundAsync(string json, SubscriptionKey key, DateOnly asOf, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);

        var map = await GetInboundColumnMapCachedAsync(conn, ct);
        var colList = string.Join(", ", map.Select(m => $"\"{m.SourceColumn}\""));
        var copySql = $"COPY app_data.bbg_positions_inbound ({colList}) FROM STDIN (FORMAT BINARY)";

        var j = string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);
        var accountFromJson = GetJsonTextOrNull(j, "ACCOUNT");

        _log.Information("[INBOUND BEFORE] Intraday → entity={EntityType}/{EntityName}, account={Account}, as_of={AsOf}",
                         key.entityType, key.entityName, accountFromJson, asOf);

        await using (var writer = await conn.BeginBinaryImportAsync(copySql, ct))
        {
            await writer.StartRowAsync(ct);
            foreach (var m in map)
            {
                if (m.SourceKind.Equals("loader", StringComparison.OrdinalIgnoreCase))
                {
                    if (m.SourceColumn.Equals("as_of_date", StringComparison.OrdinalIgnoreCase))
                        await writer.WriteAsync(asOf, NpgsqlDbType.Date, ct);
                    else
                        await writer.WriteAsync(GetLoaderValueText(key, asOf, m.SourceColumn, isIntraday: true, accountFromJson), NpgsqlDbType.Text, ct);
                }
                else // json
                {
                    var val = GetJsonTextOrNull(j, m.SourceColumn);
                    await writer.WriteAsync(val, NpgsqlDbType.Text, ct);
                }
            }
            await writer.CompleteAsync(ct);
        }

        _log.Information("[INBOUND AFTER] Intraday persisted → entity={EntityType}/{EntityName}, account={Account}, as_of={AsOf}",
                         key.entityType, key.entityName, accountFromJson, asOf);
    }

    public async Task CallUpsertIntradayAsync(string json, SubscriptionKey key, DateOnly asOf, CancellationToken ct)
    {
        var j = string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);
        var accountFromJson = GetJsonTextOrNull(j, "ACCOUNT");

        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);

        const string procName = "app_data.bbg_upsert_positions_from_inbound";

        _log.Information("[UPSERT BEFORE] Intraday → proc={Proc}, p_is_intraday={Intraday}, p_load_process={ProcName}, p_as_of_date={AsOf}, p_account_id_intraday={Account}, p_load_bb_entity_name={EntityName}",
                         procName, true, "playground-client", asOf, accountFromJson ?? "(null)", key.entityName);

        await using var call = new NpgsqlCommand(
            $"CALL {procName} (@p_is_intraday, @p_load_process, @p_as_of_date, @p_account_id_intraday, @p_load_bb_entity_name)", conn);
        call.Parameters.AddWithValue("p_is_intraday", true);
        call.Parameters.AddWithValue("p_load_process", "playground-client");
        call.Parameters.Add("p_as_of_date", NpgsqlDbType.Date).Value = asOf;
        call.Parameters.AddWithValue("p_account_id_intraday", (object?)accountFromJson ?? DBNull.Value);
        call.Parameters.AddWithValue("p_load_bb_entity_name", key.entityName);
        await call.ExecuteNonQueryAsync(ct);

        _log.Information("[UPSERT AFTER] Intraday complete → entity={EntityType}/{EntityName}, account={Account}, as_of={AsOf}",
                         key.entityType, key.entityName, accountFromJson ?? "(null)", asOf);
    }
}
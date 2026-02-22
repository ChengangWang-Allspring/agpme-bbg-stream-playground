// File: Agpme.Bbg.Playground.Simulator.Api/Streaming/PositionsStreamService.cs

using System.IO;
using Serilog;
using Agpme.Bbg.Playground.Simulator.Api.Data;

namespace Agpme.Bbg.Playground.Simulator.Api.Streaming;

public sealed class PositionsStreamService : IPositionsStreamService
{
    private readonly IPositionsStreamRepository _repo;

    private const int InitialPaintEndEmptyCount = 5;
    // Slower, developer-friendly pacing 
    private static readonly TimeSpan InitialPaintEndEmptyDelay = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan HeartbeatDelayNoUpdates = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan UpdatePollDelay = TimeSpan.FromSeconds(5);

    public PositionsStreamService(IPositionsStreamRepository repo, ILogger<PositionsStreamService> logger)
    {
        _repo = repo;
    }

    public async Task StreamAsync(
        HttpContext http,
        string entityType,
        string entityName,
        DateOnly asOfDate,
        bool chunk,
        CancellationToken ct)
    {
        try
        {
            // Try to pick up a pre-generated request id (from the endpoint) so we can include it in logs
            var reqIdFromHeader = http.Response.Headers.TryGetValue("X-Request-ID", out var hdr) ? (string?)hdr.ToString() : null;
            var reqIdFromItems = http.Items.TryGetValue("msg_request_id", out var obj) ? obj as string : null;
            string? logMsgId = reqIdFromItems ?? reqIdFromHeader;

            Log.Information("Stream start: {EntityName}-{EntityType} ({MsgId}) asOf={AsOf} chunk={Chunk}",
                entityName, entityType, logMsgId ?? "(none)", asOfDate, chunk);

            var (last, msgId, initialRows) = await _repo.GetInitialAsync(entityType, entityName, asOfDate, ct);
            // Prefer DB msgId, else fallback to the header/items one for logging
            if (!string.IsNullOrWhiteSpace(msgId))
                logMsgId = msgId;

            if (initialRows.Count == 0)
            {
                Log.Information("Initial paint: 0 rows. Sending keep-alive {{}} → {EntityName}-{EntityType} ({MsgId})",
                    entityName, entityType, logMsgId ?? "(none)");
                await JsonWriterUtil.WriteJsonAsync(http, "{}", chunk, ct);
                await http.Response.Body.FlushAsync(ct);
            }
            else
            {
                Log.Information("Initial paint: sending {Count} rows → {EntityName}-{EntityType} ({MsgId})",
                    initialRows.Count, entityName, entityType, logMsgId ?? "(none)");

                foreach (var json in initialRows)
                    await JsonWriterUtil.WriteJsonAsync(http, json, chunk, ct);

                await http.Response.Body.FlushAsync(ct);

                Log.Information("Initial paint complete. Emitting {Count} empty {{}} markers → {EntityName}-{EntityType} ({MsgId})",
                    InitialPaintEndEmptyCount, entityName, entityType, logMsgId ?? "(none)");

                for (int i = 0; i < InitialPaintEndEmptyCount && !ct.IsCancellationRequested; i++)
                {
                    await JsonWriterUtil.WriteJsonAsync(http, "{}", chunk, ct);
                    await http.Response.Body.FlushAsync(ct);
                    await Task.Delay(InitialPaintEndEmptyDelay, ct);
                }
            }

            // If we don't have a msg_request_id from initial paint:
            // just keep heartbeating until the client closes.
            if (msgId is null)
            {
                Log.Information("No msg_request_id found for initial paint → {EntityName}-{EntityType} ({MsgId}); will only send heartbeats until client disconnects.",
                    entityName, entityType, logMsgId ?? "(none)");

                while (!ct.IsCancellationRequested)
                {
                    await JsonWriterUtil.WriteJsonAsync(http, "{}", chunk, ct);
                    await http.Response.Body.FlushAsync(ct);
                    await Task.Delay(HeartbeatDelayNoUpdates, ct);
                    Log.Information("Heartbeat {{}} → {EntityName}-{EntityType} ({MsgId})", entityName, entityType, logMsgId ?? "(none)");
                        await Task.Delay(HeartbeatDelayNoUpdates, ct);
                }
                return;
            }

            Log.Information("Steady state: {EntityName}-{EntityType} (msg_request_id={MsgId}, lastStreamOrder={Last})",
                entityName, entityType, msgId, last);

            // Steady-state: poll for updates and heartbeat when idle.
            while (!ct.IsCancellationRequested)
            {
                var updates = await _repo.GetUpdatesAsync(entityType, entityName, asOfDate, msgId!, last, ct);

                if (updates.Count == 0)
                {
                    Log.Information("No updates. Sending heartbeat {{}} → {EntityName}-{EntityType} ({MsgId})",
                        entityName, entityType, msgId ?? logMsgId ?? "(none)");
                    await JsonWriterUtil.WriteJsonAsync(http, "{}", chunk, ct);
                    await http.Response.Body.FlushAsync(ct);
                    await Task.Delay(HeartbeatDelayNoUpdates, ct);
                    continue;
                }

                Log.Information("Sending {Count} update(s) → {EntityName}-{EntityType} ({MsgId})",
                    updates.Count, entityName, entityType, msgId ?? logMsgId ?? "(none)");

                foreach (var (streamOrder, json) in updates)
                {
                    last = streamOrder;
                    await JsonWriterUtil.WriteJsonAsync(http, json, chunk, ct);
                }

                await http.Response.Body.FlushAsync(ct);
                await Task.Delay(UpdatePollDelay, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the client disconnects or the request is aborted.
            Log.Information("Stream canceled (client disconnect).");
        }
        catch (IOException ioEx)
        {
            // Also expected if the socket is closed during a write/flush.
            Log.Information(ioEx, "Stream I/O closed (client disconnect).");
        }
        finally
        {
            Log.Information("Stream end (client disconnected or request aborted).");
        }
    }
}
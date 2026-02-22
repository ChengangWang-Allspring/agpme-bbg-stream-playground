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
        // Try to pick up a pre-generated request id (from the endpoint) so we can include it in logs
        var reqIdFromHeader = http.Response.Headers.TryGetValue("X-Request-ID", out var hdr)
            ? (string?)hdr.ToString()
            : null;
        var reqIdFromItems = http.Items.TryGetValue("msg_request_id", out var obj)
            ? obj as string
            : null;

        // This is the msg_request_id we’ll show in logs until/if the DB returns a definitive one
        string? logMsgId = reqIdFromItems ?? reqIdFromHeader;

        // Local function to build the display string consistently in all code paths
        string MsgIdDisplay(string? currentDbMsgId, string? fallback) =>
            !string.IsNullOrWhiteSpace(currentDbMsgId) ? currentDbMsgId
            : !string.IsNullOrWhiteSpace(fallback) ? fallback
            : "(none)";

        string msgIdFromDb = ""; // will be set after initial paint (if available)
        int last = 0;            // last streamOrder

        try
        {
            Log.Information(
                "Stream start: {EntityName}-{EntityType} ({MsgId}) asOf={AsOf} chunk={Chunk}",
                entityName, entityType, MsgIdDisplay(msgIdFromDb, logMsgId), asOfDate, chunk);

            // Initial paint
            var initial = await _repo.GetInitialAsync(entityType, entityName, asOfDate, ct);
            last = initial.lastStreamOrder;
            msgIdFromDb = initial.msgRequestId ?? "";

            // Once we have a DB-provided msg_request_id, prefer it for the rest of the logs
            var logMsgIdNow = MsgIdDisplay(msgIdFromDb, logMsgId);

            if (initial.jsonRows.Count == 0)
            {
                Log.Information(
                    "Initial paint: 0 rows. Sending keep-alive {{}} → {EntityName}-{EntityType} ({MsgId})",
                    entityName, entityType, logMsgIdNow);

                await JsonWriterUtil.WriteJsonAsync(http, "{}", chunk, ct);
                await http.Response.Body.FlushAsync(ct);
            }
            else
            {
                Log.Information(
                    "Initial paint: sending {Count} rows → {EntityName}-{EntityType} ({MsgId})",
                    initial.jsonRows.Count, entityName, entityType, logMsgIdNow);

                foreach (var json in initial.jsonRows)
                    await JsonWriterUtil.WriteJsonAsync(http, json, chunk, ct);

                await http.Response.Body.FlushAsync(ct);

                Log.Information(
                    "Initial paint complete. Emitting {Count} empty {{}} markers → {EntityName}-{EntityType} ({MsgId})",
                    InitialPaintEndEmptyCount, entityName, entityType, logMsgIdNow);

                for (int i = 0; i < InitialPaintEndEmptyCount && !ct.IsCancellationRequested; i++)
                {
                    await JsonWriterUtil.WriteJsonAsync(http, "{}", chunk, ct);
                    await http.Response.Body.FlushAsync(ct);
                    await Task.Delay(InitialPaintEndEmptyDelay, ct);
                }
            }

            // If repo didn’t return msg_request_id, we stay in heartbeat mode
            if (string.IsNullOrWhiteSpace(msgIdFromDb))
            {
                Log.Information(
                    "No msg_request_id from initial paint → {EntityName}-{EntityType} ({MsgId}); only heartbeats until client disconnects.",
                    entityName, entityType, MsgIdDisplay(msgIdFromDb, logMsgId));

                while (!ct.IsCancellationRequested)
                {
                    await JsonWriterUtil.WriteJsonAsync(http, "{}", chunk, ct);
                    await http.Response.Body.FlushAsync(ct);

                    Log.Information(
                        "Heartbeat {{}} → {EntityName}-{EntityType} ({MsgId})",
                        entityName, entityType, MsgIdDisplay(msgIdFromDb, logMsgId));

                    await Task.Delay(HeartbeatDelayNoUpdates, ct);
                }

                Log.Information("Stream end (no msg_request_id).");
                return;
            }

            Log.Information(
                "Steady state: {EntityName}-{EntityType} (msg_request_id={MsgId}, lastStreamOrder={Last})",
                entityName, entityType, msgIdFromDb, last);

            // Poll for updates forever, heartbeat when no updates
            while (!ct.IsCancellationRequested)
            {
                var updates = await _repo.GetUpdatesAsync(entityType, entityName, asOfDate, msgIdFromDb, last, ct);
                if (updates.Count == 0)
                {
                    Log.Information(
                        "No updates. Sending heartbeat {{}} → {EntityName}-{EntityType} ({MsgId})",
                        entityName, entityType, MsgIdDisplay(msgIdFromDb, logMsgId));

                    await JsonWriterUtil.WriteJsonAsync(http, "{}", chunk, ct);
                    await http.Response.Body.FlushAsync(ct);
                    await Task.Delay(HeartbeatDelayNoUpdates, ct);
                    continue;
                }

                Log.Information(
                    "Sending {Count} update(s) → {EntityName}-{EntityType} ({MsgId})",
                    updates.Count, entityName, entityType, MsgIdDisplay(msgIdFromDb, logMsgId));

                foreach (var (streamOrder, json) in updates)
                {
                    last = streamOrder;
                    await JsonWriterUtil.WriteJsonAsync(http, json, chunk, ct);
                }

                await http.Response.Body.FlushAsync(ct);
                await Task.Delay(UpdatePollDelay, ct);
            }

            Log.Information("Stream end (client disconnected or request aborted).");
        }
        catch (OperationCanceledException)
        {
            // Expected when the client disconnects or the request is aborted.
            Log.Information(
                "Stream canceled (client disconnect) → {EntityName}-{EntityType} ({MsgId})",
                entityName, entityType, MsgIdDisplay(msgIdFromDb, logMsgId));
        }
        catch (IOException ioEx)
        {
            // Also expected if the socket is closed during a write/flush.
            Log.Information(
                ioEx,
                "Stream I/O closed (client disconnect) → {EntityName}-{EntityType} ({MsgId})",
                entityName, entityType, MsgIdDisplay(msgIdFromDb, logMsgId));
        }
        finally
        {
            Log.Information(
                "Stream end (client disconnected or request aborted) → {EntityName}-{EntityType} ({MsgId})",
                entityName, entityType, MsgIdDisplay(msgIdFromDb, logMsgId));
        }
    }

}
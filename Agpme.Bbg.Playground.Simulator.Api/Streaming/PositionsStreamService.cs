using Serilog;
using Agpme.Bbg.Playground.Simulator.Api.Data;

namespace Agpme.Bbg.Playground.Simulator.Api.Streaming;

public sealed class PositionsStreamService : IPositionsStreamService
{
    private readonly IPositionsStreamRepository _repo;

    private const int InitialPaintEndEmptyCount = 5;
    private static readonly TimeSpan InitialPaintEndEmptyDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan HeartbeatDelayNoUpdates = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan UpdatePollDelay = TimeSpan.FromSeconds(1);

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
        Log.Information("Stream start: entityType={EntityType}, entityName={EntityName}, asOfDate={AsOf}, chunk={Chunk}",
            entityType, entityName, asOfDate, chunk);

        var (last, msgId, initialRows) = await _repo.GetInitialAsync(entityType, entityName, asOfDate, ct);

        if (initialRows.Count == 0)
        {
            Log.Information("Initial paint: 0 rows. Sending keep-alive {{}} and transitioning to steady state.");
            await JsonWriterUtil.WriteJsonAsync(http, "{}", chunk, ct);
            await http.Response.Body.FlushAsync(ct);
        }
        else
        {
            Log.Information("Initial paint: sending {Count} rows.", initialRows.Count);
            foreach (var json in initialRows)
                await JsonWriterUtil.WriteJsonAsync(http, json, chunk, ct);

            await http.Response.Body.FlushAsync(ct);

            Log.Information("Initial paint complete. Emitting {Count} empty {{}} markers.", InitialPaintEndEmptyCount);
            for (int i = 0; i < InitialPaintEndEmptyCount && !ct.IsCancellationRequested; i++)
            {
                await JsonWriterUtil.WriteJsonAsync(http, "{}", chunk, ct);
                await http.Response.Body.FlushAsync(ct);
                await Task.Delay(InitialPaintEndEmptyDelay, ct);
            }
        }

        if (msgId is null)
        {
            Log.Information("No msg_request_id found for initial paint. Will only send heartbeats until client disconnects.");
            while (!ct.IsCancellationRequested)
            {
                await JsonWriterUtil.WriteJsonAsync(http, "{}", chunk, ct);
                await http.Response.Body.FlushAsync(ct);
                await Task.Delay(HeartbeatDelayNoUpdates, ct);
            }
            Log.Information("Stream end (no msg_request_id).");
            return;
        }

        Log.Information("Steady state: msg_request_id={MsgId}, lastStreamOrder={Last}.", msgId, last);

        while (!ct.IsCancellationRequested)
        {
            var updates = await _repo.GetUpdatesAsync(entityType, entityName, asOfDate, msgId!, last, ct);

            if (updates.Count == 0)
            {
                Log.Information("No updates. Sending heartbeat {{}}.");
                await JsonWriterUtil.WriteJsonAsync(http, "{}", chunk, ct);
                await http.Response.Body.FlushAsync(ct);
                await Task.Delay(HeartbeatDelayNoUpdates, ct);
                continue;
            }

            Log.Information("Sending {Count} update(s).", updates.Count);
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
}

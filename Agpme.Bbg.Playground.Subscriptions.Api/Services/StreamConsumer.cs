using System.Net.Http.Headers;
using System.Text;
using Agpme.Bbg.Playground.Subscriptions.Api.Configuration;
using Agpme.Bbg.Playground.Subscriptions.Api.Models;

namespace Agpme.Bbg.Playground.Subscriptions.Api.Services;

public static class StreamConsumer
{
    public static async Task RunAsync(
        HttpClient http,
        PlaygroundClientOptions opts,
        SubscriptionKey key,
        SubscriptionMetrics metrics,
        Serilog.ILogger log,
        IPositionInboundPersister persister,
        CancellationToken ct)
    {
        var asOf = string.IsNullOrWhiteSpace(opts.AsOfDate)
            ? DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd")
            : opts.AsOfDate!;
        var uri = $"/trading-solutions/positions/{key.entityType}/{key.entityName}/subscriptions" +
                  $"?as_of_date={asOf}&chunk={(opts.Chunk ? "true" : "false")}";

        log.Information("Subscription start → {EntityType}/{EntityName} as_of_date={AsOf} chunk={Chunk}",
                        key.entityType, key.entityName, asOf, opts.Chunk);

        // --- connect (isolated) ---
        using var res = await ConnectForStreamingAsync(http, uri, key, metrics, log, ct);
        if (res is null) return; // error path already handled/logged

        using var stream = await res.Content.ReadAsStreamAsync(ct);

        // Mirrors the realtime poller to capture X-Request-ID (if server provided it) 
        var msgRequestId =
            res.Headers.TryGetValues("X-Request-ID", out var vals) ? vals.FirstOrDefault() : null;
        if (string.IsNullOrWhiteSpace(msgRequestId))
        {
            // Fallback to a deterministic GUID for this run if header missing
            msgRequestId = Guid.NewGuid().ToString("N");
            log.Warning("No X-Request-ID header; generated local msgRequestId={MsgId}", msgRequestId);
        }
        else
        {
            log.Information("Using server-provided X-Request-ID: {MsgId}", msgRequestId);
        }


        // --- state ---
        metrics.State = SubscriptionState.InitialPaint;
        var asOfDate = DateOnly.Parse(asOf);
        var initialBatch = new List<string>();
        var initialPersisted = false;

        // --- read/process loop (flat) ---
        await foreach (var json in ReadJsonObjectsAsync(stream, log, ct))
        {
            metrics.LastMessageAt = DateTimeOffset.UtcNow;

            if (IsHeartbeat(json))
            {
                // Friendly heartbeat message for Playground testing
                log.Information("Heartbeat {{}} received ← {EntityType}/{EntityName}", key.entityType, key.entityName);

                // FIRST {} ends initial paint (client rule)
                if (metrics.State == SubscriptionState.InitialPaint && !initialPersisted)
                {
                    var ok = await HandleFirstHeartbeatEndsInitialAsync(
                        initialBatch, key, asOfDate, msgRequestId!, persister, metrics, log, ct);
                    if (!ok) return; // error already logged/set
                    initialBatch.Clear();
                    initialPersisted = true;
                    log.Information("InitialPaint complete → entering Intraday → {EntityType}/{EntityName}", key.entityType, key.entityName);
                    metrics.State = SubscriptionState.Intraday;
                    continue; // don’t count this {} as a steady heartbeat
                }

                metrics.Heartbeats++;
                continue;
            }

            // Non-empty JSON → route by state
            switch (metrics.State)
            {
                case SubscriptionState.InitialPaint:
                    BufferInitialPayload(initialBatch, json, metrics, log);
                    break;

                case SubscriptionState.Intraday:
                    if (!await HandleIntradayPayloadAsync(json, key, asOfDate, msgRequestId!, persister, metrics, log, ct))
                        return; // error already logged/set
                    break;

                default:
                    // If we ever get here, treat as no-op but log once
                    log.Debug("Ignoring payload in state {State} → {EntityType}/{EntityName}",
                              metrics.State, key.entityType, key.entityName);
                    break;
            }
        }

        // cancelled or stream closed
        metrics.State = SubscriptionState.Stopped;
        metrics.StoppedAt = DateTimeOffset.UtcNow;
        log.Information("Subscription stopped → {EntityType}/{EntityName}", key.entityType, key.entityName);
    }

    // ---------- helpers ----------

    private static bool IsHeartbeat(string json)
        => json.Length <= 2 && json.Trim() == "{}";

    private static async Task<HttpResponseMessage?> ConnectForStreamingAsync(
        HttpClient http, string uri, SubscriptionKey key, SubscriptionMetrics metrics, Serilog.ILogger log, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, uri);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode)
            {
                log.Error("HTTP {Status} for {EntityType}/{EntityName} (expected 200). Aborting.",
                          (int)res.StatusCode, key.entityType, key.entityName);
                metrics.LastError = $"HTTP {(int)res.StatusCode}";
                metrics.State = SubscriptionState.Error;
                metrics.StoppedAt = DateTimeOffset.UtcNow;
                return null;
            }
            log.Information("Connected (headers received) → {EntityType}/{EntityName}", key.entityType, key.entityName);
            return res;
        }
        catch (OperationCanceledException)
        {
            log.Warning("Subscription canceled before connection → {EntityType}/{EntityName}", key.entityType, key.entityName);
            metrics.State = SubscriptionState.Stopped;
            metrics.StoppedAt = DateTimeOffset.UtcNow;
            return null;
        }
        catch (Exception ex)
        {
            log.Error(ex, "HTTP connect error → {EntityType}/{EntityName}", key.entityType, key.entityName);
            metrics.LastError = ex.Message;
            metrics.State = SubscriptionState.Error;
            metrics.StoppedAt = DateTimeOffset.UtcNow;
            return null;
        }
    }

    private static void BufferInitialPayload(List<string> initialBatch, string json, SubscriptionMetrics metrics, Serilog.ILogger log)
    {
        initialBatch.Add(json);         
        metrics.InitialPaintObjects++;
        if (metrics.InitialPaintObjects % 50 == 0)
            log.Information("InitialPaint received {Count} so far", metrics.InitialPaintObjects);
    }

    private static async Task<bool> HandleFirstHeartbeatEndsInitialAsync(
        List<string> initialBatch,
        SubscriptionKey key,
        DateOnly asOfDate,
        string msgRequestId,
        IPositionInboundPersister persister,
        SubscriptionMetrics metrics,
        Serilog.ILogger log,
        CancellationToken ct)
    {
        try
        {
            log.Information("InitialPaint end marker (first {{}}) → persisting initial batch ({Count})",
                            initialBatch.Count);

            await persister.PersistInitialBatchToInboundAsync(initialBatch, key, asOfDate, msgRequestId, log, ct);
            await persister.CallUpsertInitialAsync(key, asOfDate, msgRequestId, log, ct);

            log.Information("InitialPaint batch persisted & upserted → {EntityType}/{EntityName}",
                            key.entityType, key.entityName);
            return true;
        }
        catch (Exception ex)
        {
            metrics.LastError = ex.Message;
            metrics.State = SubscriptionState.Error;
            log.Error(ex, "InitialPaint persist/upsert failed → {EntityType}/{EntityName}", key.entityType, key.entityName);
            return false;
        }
    }

    private static async Task<bool> HandleIntradayPayloadAsync(
        string json,
        SubscriptionKey key,
        DateOnly asOfDate,
        string msgRequestId,
        IPositionInboundPersister persister,
        SubscriptionMetrics metrics,
        Serilog.ILogger log,
        CancellationToken ct)
    {
        try
        {

            await persister.PersistIntradayToInboundAsync(json, key, asOfDate, msgRequestId, log, ct);
            await persister.CallUpsertIntradayAsync(json, key, asOfDate, msgRequestId, log, ct);

            metrics.IntradayObjects++;
            if (metrics.IntradayObjects % 100 == 0)
                log.Information("Intraday received {Count} so far → {EntityType}/{EntityName}",
                                metrics.IntradayObjects, key.entityType, key.entityName);
            return true;
        }
        catch (Exception ex)
        {
            metrics.LastError = ex.Message;
            metrics.State = SubscriptionState.Error;
            log.Error(ex, "Intraday persist/upsert failed → {EntityType}/{EntityName}", key.entityType, key.entityName);
            return false;
        }
    }

    private static async IAsyncEnumerable<string> ReadJsonObjectsAsync(global::System.IO.Stream stream, Serilog.ILogger log, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken ct)
    {
        var buffer = new byte[8192];
        var sb = new StringBuilder(capacity: 16 * 1024);

        while (!ct.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
            if (read == 0)
            {
                await Task.Delay(50, ct);
                continue;
            }

            var chunk = Encoding.UTF8.GetString(buffer, 0, read);
            sb.Append(chunk);

            var extracted = ExtractCompleteJsonObjects(sb);
            foreach (var json in extracted)
                yield return json;
        }
    }

    // Reuse your existing extractor unchanged (brace-depth with string/escape handling)
    private static IEnumerable<string> ExtractCompleteJsonObjects(StringBuilder sb)
    {
        var s = sb.ToString();
        var results = new List<string>();
        int depth = 0;
        bool inString = false, escape = false;
        int start = -1;

        for (int i = 0; i < s.Length; i++)
        {
            var ch = s[i];

            if (inString)
            {
                if (escape) { escape = false; continue; }
                if (ch == '\\') { escape = true; continue; }
                if (ch == '"') inString = false;
                continue;
            }

            if (ch == '"') { inString = true; continue; }
            if (ch == '{')
            {
                if (depth == 0) start = i;
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    int len = i - start + 1;
                    results.Add(s.Substring(start, len));
                    start = -1;
                }
            }
        }

        if (results.Count > 0)
        {
            var last = results[^1];
            var endIdx = s.LastIndexOf(last, StringComparison.Ordinal) + last.Length;
            if (endIdx > 0 && endIdx <= sb.Length)
                sb.Remove(0, endIdx);
        }
        return results;
    }
}
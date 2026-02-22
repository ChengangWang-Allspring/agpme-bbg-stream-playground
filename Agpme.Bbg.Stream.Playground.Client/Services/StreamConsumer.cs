using System.Net.Http.Headers;
using System.Text;
using Agpme.Bbg.Stream.Playground.Client.Configuration;
using Agpme.Bbg.Stream.Playground.Client.Models;

namespace Agpme.Bbg.Stream.Playground.Client.Services;

public static class StreamConsumer
{
    /// <summary>
    /// Consumes the streaming endpoint and reports lifecycle events via the provided logger.
    /// NOTE: If you also persist (initial batch & intraday), inject/pass an IPositionStreamPersister
    /// and call into it at the two indicated hooks below.
    /// </summary>
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
            ? DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd")
            : opts.AsOfDate!;
        var uri = $"/trading-solutions/positions/{key.entityType}/{key.entityName}/subscriptions" +
                  $"?as_of_date={asOf}&chunk={(opts.Chunk ? "true" : "false")}";

        log.Information("Subscription start → {EntityType}/{EntityName} as_of_date={AsOf} chunk={Chunk}",
                        key.entityType, key.entityName, asOf, opts.Chunk);

        using var req = new HttpRequestMessage(HttpMethod.Post, uri);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException)
        {
            log.Warning("Subscription canceled before connection → {EntityType}/{EntityName}", key.entityType, key.entityName);
            metrics.State = SubscriptionState.Stopped;
            metrics.StoppedAt = DateTimeOffset.UtcNow;
            return;
        }
        catch (Exception ex)
        {
            log.Error(ex, "HTTP connect error → {EntityType}/{EntityName}", key.entityType, key.entityName);
            metrics.LastError = ex.Message;
            metrics.State = SubscriptionState.Error;
            metrics.StoppedAt = DateTimeOffset.UtcNow;
            return;
        }

        if (!res.IsSuccessStatusCode)
        {
            log.Error("HTTP {Status} for {EntityType}/{EntityName} (expected 200). Aborting.",
                      (int)res.StatusCode, key.entityType, key.entityName);
            metrics.LastError = $"HTTP {(int)res.StatusCode}";
            metrics.State = SubscriptionState.Error;
            metrics.StoppedAt = DateTimeOffset.UtcNow;
            return;
        }

        log.Information("Connected (headers received) → {EntityType}/{EntityName}", key.entityType, key.entityName);

        using var stream = await res.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[8192];
        var sb = new StringBuilder(capacity: 16 * 1024);

        metrics.State = SubscriptionState.InitialPaint;
        var initialBatch = new List<string>();
        var asOfDateOnly = DateOnly.Parse(asOf);
        bool initialPersisted = false; // ensure we only do it once

        log.Debug("Entering read loop → {EntityType}/{EntityName}", key.entityType, key.entityName);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (read == 0)
                {
                    log.Debug("Zero-byte read; backing off 50ms → {EntityType}/{EntityName}", key.entityType, key.entityName);
                    await Task.Delay(50, ct);
                    continue;
                }

                var chunk = Encoding.UTF8.GetString(buffer, 0, read);
                sb.Append(chunk);

                var extracted = ExtractCompleteJsonObjects(sb).ToList();
                if (extracted.Count > 0)
                {
                    log.Debug("Extracted {Count} JSON object(s) → {EntityType}/{EntityName}",
                              extracted.Count, key.entityType, key.entityName);
                }

                foreach (var json in extracted)
                {
                    metrics.LastMessageAt = DateTimeOffset.UtcNow;

                    var isHeartbeat = json.Length <= 2 && json.Trim() == "{}";

                    // ---------- Client rule: FIRST {} ends InitialPaint ----------
                    if (metrics.State == SubscriptionState.InitialPaint && isHeartbeat && !initialPersisted)
                    {
                        // Persist & upsert the initial batch immediately (even if batch is empty)
                        try
                        {
                            log.Information("InitialPaint end marker received (first {{}}) → persisting initial batch ({Count})",
                                            initialBatch.Count);

                            await persister.PersistInitialBatchToInboundAsync(initialBatch, key, asOfDateOnly, ct);
                            await persister.CallUpsertInitialAsync(key, asOfDateOnly, ct);

                            log.Information("InitialPaint batch persisted & upserted → {EntityType}/{EntityName}",
                                            key.entityType, key.entityName);
                            initialPersisted = true;
                        }
                        catch (Exception ex)
                        {
                            metrics.LastError = ex.Message;
                            metrics.State = SubscriptionState.Error;
                            log.Error(ex, "InitialPaint persist/upsert failed → {EntityType}/{EntityName}", key.entityType, key.entityName);
                            return;
                        }
                        finally
                        {
                            initialBatch.Clear();
                        }

                        metrics.State = SubscriptionState.Intraday;
                        continue; // do not treat this {} as a heartbeat for steady state
                    }

                    if (isHeartbeat)
                    {
                        metrics.Heartbeats++;
                        // (optional log) log.Debug("Heartbeat in {State} → {EntityType}/{EntityName}", metrics.State, key.entityType, key.entityName);
                        continue;
                    }

                    // Non-empty JSON
                    if (metrics.State == SubscriptionState.InitialPaint)
                    {
                        metrics.InitialPaintObjects++;
                        initialBatch.Add(json);

                        if (metrics.InitialPaintObjects % 50 == 0)
                        {
                            log.Information("InitialPaint received {Count} so far → {EntityType}/{EntityName}",
                                            metrics.InitialPaintObjects, key.entityType, key.entityName);
                        }
                    }
                    else // Steady (intraday)
                    {
                        metrics.IntradayObjects++;

                        try
                        {
                            // Split: persist first, then call upsert
                            await persister.PersistIntradayToInboundAsync(json, key, asOfDateOnly, ct);
                            await persister.CallUpsertIntradayAsync(json, key, asOfDateOnly, ct);
                        }
                        catch (Exception ex)
                        {
                            metrics.LastError = ex.Message;
                            metrics.State = SubscriptionState.Error;
                            log.Error(ex, "Intraday persist/upsert failed → {EntityType}/{EntityName}", key.entityType, key.entityName);
                            return;
                        }

                        if (metrics.IntradayObjects % 100 == 0)
                        {
                            log.Information("Intraday received {Count} so far → {EntityType}/{EntityName}",
                                            metrics.IntradayObjects, key.entityType, key.entityName);
                        }
                    }
                }
            }

            log.Warning("Cancellation requested → exiting read loop → {EntityType}/{EntityName}", key.entityType, key.entityName);
        }
        catch (OperationCanceledException)
        {
            log.Warning("Subscription canceled during read → {EntityType}/{EntityName}", key.entityType, key.entityName);
            metrics.State = SubscriptionState.Stopped;
            metrics.StoppedAt = DateTimeOffset.UtcNow;
            return;
        }
        catch (Exception ex)
        {
            log.Error(ex, "Read loop error → {EntityType}/{EntityName}", key.entityType, key.entityName);
            metrics.LastError = ex.Message;
            metrics.State = SubscriptionState.Error;
            metrics.StoppedAt = DateTimeOffset.UtcNow;
            return;
        }

        metrics.State = SubscriptionState.Stopped;
        metrics.StoppedAt = DateTimeOffset.UtcNow;
        log.Information("Subscription stopped → {EntityType}/{EntityName}", key.entityType, key.entityName);
    }



    /// <summary>
    /// Parses the StringBuilder buffer and yields complete JSON objects as strings,
    /// even when incoming bytes split objects across chunks. Maintains state in 'sb'.
    /// </summary>
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

        // remove consumed prefix (up to the last complete JSON)
        if (results.Count > 0)
        {
            var last = results[^1];
            var endIdx = s.IndexOf(last, StringComparison.Ordinal) + last.Length;
            sb.Remove(0, endIdx);
        }
        return results;
    }
}
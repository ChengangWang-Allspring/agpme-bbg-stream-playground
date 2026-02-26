// Agpme.Bbg.Playground.Admin/Services/SubscriptionsClient.cs
using System.Net.Http.Json;
using Agpme.Bbg.Playground.Admin.Models;

namespace Agpme.Bbg.Playground.Admin.Services;

public sealed class SubscriptionsClient
{
    private readonly HttpClient _http;
    public SubscriptionsClient(IHttpClientFactory factory)
        => _http = factory.CreateClient("subsapi");

    // ---------------- Subscriptions ----------------
    public async Task<List<SubscriptionStatus>> ListAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<SubscriptionStatus>>("/client/subscriptions", ct) ?? new();

    public async Task<SubscriptionStatus?> StartAsync(SubscriptionKey key, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync("/client/subscriptions/start", key, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<SubscriptionStatus>(cancellationToken: ct);
    }

    public async Task<bool> StopAsync(SubscriptionKey key, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync("/client/subscriptions/stop", key, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<bool>(cancellationToken: ct);
    }

    public async Task StartAllAsync(CancellationToken ct = default)
    {
        var res = await _http.PostAsync("/client/subscriptions/start-all", null, ct);
        res.EnsureSuccessStatusCode();
    }

    // ---------------- Settings / Config ----------------
    public async Task<string?> GetAsOfDateAsync(CancellationToken ct = default)
        => (await _http.GetFromJsonAsync<Dictionary<string, string?>>("/client/settings/as-of-date", ct))?["as_of_date"];

    public async Task<string?> SetAsOfDateAsync(string? asOf, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync("/client/settings/as-of-date", new { as_of_date = asOf }, ct);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<Dictionary<string, string?>>(cancellationToken: ct))?["as_of_date"];
    }

    public async Task<List<Target>?> GetTargetsAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<Target>>("/client/config/targets", ct);

    // ---------------- Admin ----------------
    public async Task<bool> ResetPositionsAsync(CancellationToken ct = default)
    {
        var res = await _http.PostAsync("/client/admin/reset-positions", content: null, ct);
        res.EnsureSuccessStatusCode();
        return true;
    }

    // ---------------- Health ----------------
    public async Task<DbHealth> GetDbHealthAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<DbHealth>("/client/health/db", ct)
           ?? new DbHealth("offline", "no response");

    // ---------------- Metadata ----------------
    public Task<List<InboundColsMapRow>?> GetInboundColsMapAsync(CancellationToken ct = default)
        => _http.GetFromJsonAsync<List<InboundColsMapRow>>("/client/metadata/inbound-cols-map", ct);

    public async Task<bool> UpdateInboundColAsync(long mapId, MetadataUpdateDto dto, CancellationToken ct = default)
    {
        var res = await _http.PutAsJsonAsync($"/client/metadata/inbound-cols-map/{mapId}", dto, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<bool>(cancellationToken: ct);
    }

    public async Task<int> ResyncInboundColsMapAsync(CancellationToken ct = default)
    {
        var res = await _http.PostAsync("/client/metadata/inbound-cols-map/resync", content: null, ct);
        res.EnsureSuccessStatusCode();
        var doc = await res.Content.ReadFromJsonAsync<Dictionary<string, int>>(cancellationToken: ct);
        return doc?["rows"] ?? 0;
    }

    public async Task<ValidateResult> ValidateExprAsync(
        string? targetColumn, string? sourceColumn, string? transformExpr, string? updateSetExpr, CancellationToken ct = default)
    {
        var body = new
        {
            target_column = targetColumn,
            source_column = sourceColumn,
            transform_expr = transformExpr,
            update_set_expr = updateSetExpr
        };
        var res = await _http.PostAsJsonAsync("/client/metadata/inbound-cols-map/validate", body, ct);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<ValidateResult>(cancellationToken: ct))
               ?? new ValidateResult(false, "unknown", "no response");
    }
}
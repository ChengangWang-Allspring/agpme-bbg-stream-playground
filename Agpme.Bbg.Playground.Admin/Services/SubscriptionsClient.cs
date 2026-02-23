using System.Net.Http.Json;

namespace Agpme.Bbg.Playground.Admin.Services;

public sealed class SubscriptionsClient
{
    private readonly HttpClient _http;
    public SubscriptionsClient(IHttpClientFactory factory)
        => _http = factory.CreateClient("subsapi");

    // Shapes mirrored from your Subscriptions API
    public record SubscriptionKey(string entityType, string entityName);
    public record SubscriptionMetrics(
        int InitialPaintObjects, int IntradayObjects, int Heartbeats,
        DateTimeOffset? LastMessageAt, DateTimeOffset? StartedAt,
        DateTimeOffset? StoppedAt, string? LastError, int State);
    public record SubscriptionStatus(SubscriptionKey Key, SubscriptionMetrics Metrics);
    public record Target(string entityType, string entityName);

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

    public async Task<bool> ResetPositionsAsync(CancellationToken ct = default)
    {
        var res = await _http.PostAsync("/client/admin/reset-positions", content: null, ct);
        res.EnsureSuccessStatusCode();
        return true;
    }
}
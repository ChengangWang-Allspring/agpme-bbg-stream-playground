using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Agpme.Bbg.Playground.Subscriptions.Api.Configuration;
using Agpme.Bbg.Playground.Subscriptions.Api.Models;

namespace Agpme.Bbg.Playground.Subscriptions.Api.Services;

public sealed class ClientApi
{
    private readonly HttpClient _http;

    public ClientApi(IHttpClientFactory factory, IOptions<PlaygroundClientOptions> opts)
    {
        // call the *same* process (loopback)
        // Using the anonymous HttpClient is fine for same-site calls
        _http = factory.CreateClient();
        _http.BaseAddress ??= new Uri("http://localhost"); // overridden by NavigationManager in Dashboard
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public record StartStopDto(string entityType, string entityName);
    public record AsOfDto(string? as_of_date);

    public Task<List<SubscriptionStatus>> GetSubscriptionsAsync(CancellationToken ct)
        => _http.GetFromJsonAsync<List<SubscriptionStatus>>("/client/subscriptions", ct)!;

    public async Task<SubscriptionStatus?> StartAsync(string entityType, string entityName, CancellationToken ct)
    {
        var res = await _http.PostAsJsonAsync("/client/subscriptions/start", new StartStopDto(entityType, entityName), ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<SubscriptionStatus>(cancellationToken: ct);
    }

    public async Task<bool> StopAsync(string entityType, string entityName, CancellationToken ct)
    {
        var res = await _http.PostAsJsonAsync("/client/subscriptions/stop", new StartStopDto(entityType, entityName), ct);
        res.EnsureSuccessStatusCode();
        var ok = await res.Content.ReadFromJsonAsync<bool>(cancellationToken: ct);
        return ok;
    }

    public async Task StartAllAsync(CancellationToken ct)
    {
        var res = await _http.PostAsync("/client/subscriptions/start-all", content: null, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task<string?> SetAsOfDateAsync(string? asOf, CancellationToken ct)
    {
        var res = await _http.PostAsJsonAsync("/client/settings/as-of-date", new AsOfDto(asOf), ct);
        res.EnsureSuccessStatusCode();
        var doc = await res.Content.ReadFromJsonAsync<Dictionary<string, string?>>(cancellationToken: ct);
        return doc?["as_of_date"];
    }

    public Task<List<PlaygroundClientOptions.SubscriptionTarget>?> GetTargetsAsync(CancellationToken ct)
    => _http.GetFromJsonAsync<List<PlaygroundClientOptions.SubscriptionTarget>>("/client/config/targets", ct);
}
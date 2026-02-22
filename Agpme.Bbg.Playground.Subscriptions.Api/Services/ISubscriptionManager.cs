using Agpme.Bbg.Playground.Subscriptions.Api.Models;

namespace Agpme.Bbg.Playground.Subscriptions.Api.Services;

public interface ISubscriptionManager
{
    Task StartAllConfiguredAsync(CancellationToken ct);
    Task<SubscriptionStatus> StartAsync(SubscriptionKey key, CancellationToken ct);
    Task<bool> StopAsync(SubscriptionKey key);
    IReadOnlyCollection<SubscriptionStatus> List();
    SubscriptionStatus? Get(SubscriptionKey key);
}

using Agpme.Bbg.Stream.Playground.Client.Models;

namespace Agpme.Bbg.Stream.Playground.Client.Services;

public interface ISubscriptionManager
{
    Task StartAllConfiguredAsync(CancellationToken ct);
    Task<SubscriptionStatus> StartAsync(SubscriptionKey key, CancellationToken ct);
    Task<bool> StopAsync(SubscriptionKey key);
    IReadOnlyCollection<SubscriptionStatus> List();
    SubscriptionStatus? Get(SubscriptionKey key);
}

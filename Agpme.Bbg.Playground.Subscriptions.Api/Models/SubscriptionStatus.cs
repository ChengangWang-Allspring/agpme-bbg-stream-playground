namespace Agpme.Bbg.Playground.Subscriptions.Api.Models;

public sealed class SubscriptionStatus
{
    public SubscriptionKey Key { get; init; }
    public SubscriptionMetrics Metrics { get; init; } = new();
}
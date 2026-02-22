namespace Agpme.Bbg.Stream.Playground.Client.Models;

public sealed class SubscriptionStatus
{
    public SubscriptionKey Key { get; init; }
    public SubscriptionMetrics Metrics { get; init; } = new();
}
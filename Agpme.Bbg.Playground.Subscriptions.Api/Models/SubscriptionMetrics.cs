namespace Agpme.Bbg.Playground.Subscriptions.Api.Models;

public sealed class SubscriptionMetrics
{
    public int InitialPaintObjects { get; set; }
    public int IntradayObjects { get; set; }
    public int Heartbeats { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public string? LastError { get; set; }
    public SubscriptionState State { get; set; } = SubscriptionState.Stopped;
}
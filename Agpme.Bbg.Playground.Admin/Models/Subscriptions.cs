namespace Agpme.Bbg.Playground.Admin.Models;

public record SubscriptionKey(string entityType, string entityName);

public record SubscriptionMetrics(
    int InitialPaintObjects, int IntradayObjects, int Heartbeats,
    DateTimeOffset? LastMessageAt, DateTimeOffset? StartedAt,
    DateTimeOffset? StoppedAt, string? LastError, int State);

public record SubscriptionStatus(SubscriptionKey Key, SubscriptionMetrics Metrics);

public record Target(string entityType, string entityName);
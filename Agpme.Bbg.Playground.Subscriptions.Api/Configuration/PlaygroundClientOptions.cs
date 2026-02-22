namespace Agpme.Bbg.Playground.Subscriptions.Api.Configuration;

public sealed class PlaygroundClientOptions
{
    public string ServerBaseUrl { get; set; } = default!;
    public string? AsOfDate { get; set; } = null;   // yyyy-MM-dd or null = today
    public bool Chunk { get; set; } = true;

    // One flat list: each item declares exactly how to call the server:
    // /trading-solutions/positions/{entityType}/{entityName}/subscriptions
    public List<SubscriptionTarget> Targets { get; set; } = new();

    public sealed class SubscriptionTarget
    {
        // MUST be "accounts" or "groups"
        public string entityType { get; set; } = default!;
        // accountId when entityType=accounts, or groupName when entityType=groups
        public string entityName { get; set; } = default!;
    }
}
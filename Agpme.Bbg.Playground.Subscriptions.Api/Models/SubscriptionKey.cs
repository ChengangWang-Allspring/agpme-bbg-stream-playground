namespace Agpme.Bbg.Playground.Subscriptions.Api.Models;

public readonly record struct SubscriptionKey(string entityType, string entityName)
{
    public override string ToString() => $"{entityType}:{entityName}";
}
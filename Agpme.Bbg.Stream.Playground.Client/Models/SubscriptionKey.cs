namespace Agpme.Bbg.Stream.Playground.Client.Models;

public readonly record struct SubscriptionKey(string entityType, string entityName)
{
    public override string ToString() => $"{entityType}:{entityName}";
}
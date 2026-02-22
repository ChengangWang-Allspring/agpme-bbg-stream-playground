using Agpme.Bbg.Playground.Subscriptions.Api.Models;

namespace Agpme.Bbg.Playground.Subscriptions.Api.Services;

public interface IPositionInboundPersister
{
    // Initial paint (batch)
    Task PersistInitialBatchToInboundAsync(List<string> jsons, SubscriptionKey key, DateOnly asOf, string msgRequestId, CancellationToken ct);
    Task CallUpsertInitialAsync(SubscriptionKey key, DateOnly asOf, string msgRequestId, CancellationToken ct);

    // Intraday (one-by-one)
    Task PersistIntradayToInboundAsync(string json, SubscriptionKey key, DateOnly asOf, string msgRequestId, CancellationToken ct);
    Task CallUpsertIntradayAsync(string json, SubscriptionKey key, DateOnly asOf, string msgRequestId, CancellationToken ct);


}
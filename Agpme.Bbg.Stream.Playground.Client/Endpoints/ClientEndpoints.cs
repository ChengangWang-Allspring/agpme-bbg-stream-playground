using Agpme.Bbg.Stream.Playground.Client.Models;
using Agpme.Bbg.Stream.Playground.Client.Services;

namespace Agpme.Bbg.Stream.Playground.Client.Endpoints;

public static class ClientEndpoints
{
    public static IEndpointRouteBuilder MapClientEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/client/subscriptions", (ISubscriptionManager mgr)
            => Results.Ok(mgr.List()));

        app.MapGet("/client/subscriptions/status/{entityType}/{entityName}",
            (ISubscriptionManager mgr, string entityType, string entityName)
                => mgr.Get(new SubscriptionKey(entityType, entityName)) is { } s ? Results.Ok(s) : Results.NotFound());

        app.MapPost("/client/subscriptions/start",
            async (ISubscriptionManager mgr, SubscriptionKey key, CancellationToken ct)
                => Results.Ok(await mgr.StartAsync(key, ct)));

        app.MapPost("/client/subscriptions/stop",
            (ISubscriptionManager mgr, SubscriptionKey key)
                => Results.Ok(mgr.StopAsync(key)));

        app.MapPost("/client/subscriptions/start-all",
            async (ISubscriptionManager mgr, CancellationToken ct) =>
            {
                await mgr.StartAllConfiguredAsync(ct);
                return Results.Accepted();
            });

        app.MapGet("/client/health", () => Results.Ok(new { status = "ok" }));

        return app;
    }
}
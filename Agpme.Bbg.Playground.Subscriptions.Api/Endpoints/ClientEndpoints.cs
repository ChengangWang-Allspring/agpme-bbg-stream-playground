using Agpme.Bbg.Playground.Subscriptions.Api.Configuration;
using Agpme.Bbg.Playground.Subscriptions.Api.Models;
using Agpme.Bbg.Playground.Subscriptions.Api.Services;

namespace Agpme.Bbg.Playground.Subscriptions.Api.Endpoints;

public static class ClientEndpoints
{
    public static IEndpointRouteBuilder MapClientEndpoints(this IEndpointRouteBuilder app)
    {
        // Subscriptions
        var subs = app.MapGroup("/client/subscriptions").WithTags("Subscriptions");

        subs.MapGet("", (ISubscriptionManager mgr)
            => Results.Ok(mgr.List()))
            .WithSummary("List running subscriptions")
            .WithDescription("Returns running subscriptions and their metrics.")
            .WithOpenApi(); // <-- works now (Microsoft.AspNetCore.OpenApi installed)

        subs.MapGet("status/{entityType}/{entityName}",
            (ISubscriptionManager mgr, string entityType, string entityName)
                => mgr.Get(new SubscriptionKey(entityType, entityName)) is { } s ? Results.Ok(s) : Results.NotFound())
            .WithSummary("Get a subscription status")
            .WithOpenApi();

        subs.MapPost("start",
            async (ISubscriptionManager mgr, SubscriptionKey key, CancellationToken ct)
                => Results.Ok(await mgr.StartAsync(key, ct)))
            .WithSummary("Start a subscription")
            .WithOpenApi();

        subs.MapPost("stop",
            async (ISubscriptionManager mgr, SubscriptionKey key)
                => Results.Ok(await mgr.StopAsync(key)))
            .WithSummary("Stop a subscription")
            .WithOpenApi();

        subs.MapPost("start-all",
            async (ISubscriptionManager mgr, CancellationToken ct) =>
            {
                await mgr.StartAllConfiguredAsync(ct);
                return Results.Accepted();
            })
            .WithSummary("Start all configured")
            .WithOpenApi();

        // Settings
        var settings = app.MapGroup("/client/settings").WithTags("Settings");

        settings.MapPost("as-of-date",
            (Microsoft.Extensions.Options.IOptions<PlaygroundClientOptions> opts, AsOfDto dto) =>
            {
            	var o = opts.Value;
                o.AsOfDate = string.IsNullOrWhiteSpace(dto.as_of_date) ? null : dto.as_of_date.Trim();
                return Results.Ok(new { as_of_date = o.AsOfDate });
            })
            .WithSummary("Set as_of_date for new subscriptions")
            .WithDescription("Use yyyy-MM-dd or empty/null for today.")
            .WithOpenApi();

        settings.MapGet("as-of-date",
            (Microsoft.Extensions.Options.IOptions<PlaygroundClientOptions> opts) =>
            {
                var asOf = opts.Value.AsOfDate;
                return Results.Ok(new { as_of_date = asOf });
            })
            .WithSummary("Get current as_of_date")
            .WithDescription("Returns the configured as_of_date (null means today).")
            .WithOpenApi();

        // Config
        app.MapGet("/client/config/targets",
            (Microsoft.Extensions.Options.IOptions<PlaygroundClientOptions> opts)
                => Results.Ok(opts.Value.Targets))
            .WithTags("Config")
            .WithSummary("List configured targets")
            .WithOpenApi();

        // Health
        app.MapGet("/client/health",
            () => Results.Ok(new { status = "ok" }))
            .WithTags("Health")
            .WithSummary("Healthcheck")
            .WithOpenApi();

        return app;
    }

    public record AsOfDto(string? as_of_date);
}
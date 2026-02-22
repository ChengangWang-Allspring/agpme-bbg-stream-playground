using Agpme.Bbg.Playground.Simulator.Api.Streaming;

namespace Agpme.Bbg.Playground.Simulator.Api.Endpoints;

public static class PositionsStreamEndpoints
{
    public static IEndpointRouteBuilder MapPositionsStreamEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /trading-solutions/positions/{entityType}/{entityName}/subscriptions?as_of_date=YYYY-MM-DD&chunk=true
        app.MapPost("/trading-solutions/positions/{entityType}/{entityName}/subscriptions",
            async (HttpContext http,
                   string entityType,
                   string entityName,
                   DateOnly? as_of_date,
                   bool chunk,
                   IPositionsStreamService streamService,
                   CancellationToken ct) =>
            {
                // Basic validation
                if (string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(entityName))
                    return Results.BadRequest(new { error = "Both entityType and entityName are required." });

                var asOf = as_of_date ?? DateOnly.FromDateTime(DateTime.UtcNow);

                // Generate a stable request id for this subscription (client will persist it)
                var msgRequestId = Guid.NewGuid().ToString("N");
                http.Response.Headers["X-Request-ID"] = msgRequestId;

                http.Response.StatusCode = 200;
                http.Response.Headers.CacheControl = "no-store";
                http.Response.ContentType = "application/json; charset=utf-8";

                await http.Response.StartAsync(ct);

                await streamService.StreamAsync(
                    http, entityType, entityName, asOf, chunk, ct);

                return Results.Empty;
            });

        // Optional root health
        app.MapGet("/", () => Results.Ok(new { name = "agpme-bbg-stream-playground", status = "ok" }));

        return app;
    }
}
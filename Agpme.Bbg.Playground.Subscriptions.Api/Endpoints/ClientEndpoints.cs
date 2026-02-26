using Agpme.Bbg.Playground.Subscriptions.Api.Configuration;
using Agpme.Bbg.Playground.Subscriptions.Api.Models;
using Agpme.Bbg.Playground.Subscriptions.Api.Services;
using Allspring.Agpme.Bbg.TestsShared.Helpers.Postgres;
using Npgsql;

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

        // Database health (local playground Postgres)
        app.MapGet("/client/health/db", async (Microsoft.Extensions.Configuration.IConfiguration cfg, CancellationToken ct) =>
        {
            var cs = cfg.GetSection("ClientDb:ConnectionString").Value;
            if (string.IsNullOrWhiteSpace(cs))
                return Results.Problem("ClientDb:ConnectionString is not configured.", statusCode: 500);

            try
            {
                // Clone CS and force a short connection timeout (seconds)
                var csb = new Npgsql.NpgsqlConnectionStringBuilder(cs) { Timeout = 3 };
                await using var conn = new Npgsql.NpgsqlConnection(csb.ToString());
                await conn.OpenAsync(ct);
                await using var cmd = new Npgsql.NpgsqlCommand("select 1", conn) { CommandTimeout = 3 };
                await cmd.ExecuteScalarAsync(ct);
                return Results.Ok(new { status = "online" });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { status = "offline", error = ex.Message });
            }
        })
        .WithTags("Health")
        .WithSummary("Local database health")
        .WithDescription("Checks if the local playground Postgres is reachable (online/offline).")
        .WithOpenApi();


        // Admin
        var admin = app.MapGroup("/client/admin").WithTags("Admin");
        admin.MapPost("reset-positions",
            async (IResetService reset, CancellationToken ct) =>
            {
                await reset.ResetPositionsAsync(ct);
                return Results.Ok(new { reset = "ok" });
            })
            .WithSummary("Reset inbound and positions tables")
            .WithDescription("Truncates app_data.bbg_positions_inbound and app_data.bbg_positions; use with caution.")
            .WithOpenApi();


        // Metadata
        var metadata = app.MapGroup("/client/metadata").WithTags("Metadata");

        // GET all rows
        metadata.MapGet("inbound-cols-map",
            async (IConfiguration cfg, CancellationToken ct) =>
            {
                var cs = cfg.GetSection("ClientDb:ConnectionString").Value!;
                var rows = await MetadataHelper.GetInboundColsMapAsync(cs, ct);
                return Results.Ok(rows);
            });

        // PUT update one row
        metadata.MapPut("inbound-cols-map/{mapId:long}",
            async (IConfiguration cfg, long mapId, MetadataHelper.InboundColsMapUpdate dto, CancellationToken ct) =>
            {
                var cs = cfg.GetSection("ClientDb:ConnectionString").Value!;
                var ok = await MetadataHelper.UpdateInboundColsMapAsync(cs, mapId, dto, ct);
                return Results.Ok(ok);
            });

        // POST resync (DEV → local)
        metadata.MapPost("inbound-cols-map/resync",
            async (IConfiguration cfg, CancellationToken ct) =>
            {
                var destCs = cfg.GetSection("ClientDb:ConnectionString").Value!;
                // Source from your existing MetadataAwsSecrets block (same as Bootstrap)
                var arn = cfg["MetadataAwsSecrets:Arn"]!;
                var key = cfg["MetadataAwsSecrets:KeyName"]!;
                var region = cfg["MetadataAwsSecrets:Region"]!;
                var profile = cfg["MetadataAwsSecrets:Profile"]; // optional
                var sourceCs = await Allspring.Agpme.Bbg.TestsShared.Helpers.Aws.AwsSecretHelper
                    .GetSecretValueAsync(profile, region, arn, key, ct);

                var n = await MetadataHelper.ResyncInboundColsMapAsync(sourceCs, destCs, ct);
                return Results.Ok(new { rows = n });
            });

        // POST validate (syntax only)
        metadata.MapPost("inbound-cols-map/validate",
            async (IConfiguration cfg, [AsParameters] ValidateDto dto, CancellationToken ct) =>
            {
                var cs = cfg.GetSection("ClientDb:ConnectionString").Value!;
                if (!string.IsNullOrWhiteSpace(dto.transform_expr))
                {
                    var r = await MetadataHelper.ValidateTransformExprAsync(cs, dto.target_column, dto.source_column, dto.transform_expr, ct);
                    if (!r.ok) return Results.Ok(new { ok = false, kind = r.kind, error = r.error });
                }
                if (!string.IsNullOrWhiteSpace(dto.update_set_expr))
                {
                    var r = await MetadataHelper.ValidateUpdateSetExprAsync(cs, dto.target_column ?? "", dto.update_set_expr!, ct);
                    if (!r.ok) return Results.Ok(new { ok = false, kind = r.kind, error = r.error });
                }
                return Results.Ok(new { ok = true });
            });

        return app;
    }

    public record AsOfDto(string? as_of_date);

    public sealed record ValidateDto(string? target_column, string? source_column, string? transform_expr, string? update_set_expr);
}
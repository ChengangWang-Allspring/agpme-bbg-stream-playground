using Agpme.Bbg.Playground.Shared.Config;
using Agpme.Bbg.Playground.Shared.Aws;
using Microsoft.Extensions.Options;
using Npgsql;
using Agpme.Bbg.Playground.Simulator.Api.Data;
using Agpme.Bbg.Playground.Simulator.Api.Streaming;

namespace Agpme.Bbg.Playground.Simulator.Api.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStreamingServerServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind the single-ARN settings (section name "StreamAwsSecrets")
        services.Configure<AwsSecretsOptions>(configuration.GetSection("StreamAwsSecrets"));

        // Resolve the connection string from AWS once per app lifetime (lazy)
        services.AddSingleton(async sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AwsSecretsOptions>>().Value;

            if (string.IsNullOrWhiteSpace(opts.Arn))
                throw new InvalidOperationException("StreamAwsSecrets:Arn is required.");
            if (string.IsNullOrWhiteSpace(opts.SecretKeyName))
                throw new InvalidOperationException("StreamAwsSecrets:SecretKeyName is required.");

            var profile = string.IsNullOrWhiteSpace(opts.Profile)
                ? AwsSecretHelper.ResolveProfileForEnv("DEV")
                : opts.Profile!;

            var connString = await AwsSecretHelper.GetSecretValueByKeyAsync(
                profile,
                opts.Arn!,
                opts.SecretKeyName!,
                opts.Region);

            return connString;
        });

        // Create and share a single NpgsqlDataSource
        services.AddSingleton(async sp =>
        {
            var cs = await sp.GetRequiredService<Task<string>>();
            return NpgsqlDataSource.Create(cs);
        });

        // Repository and service
        services.AddScoped<IPositionsStreamRepository, PositionsStreamRepository>();
        services.AddScoped<IPositionsStreamService, PositionsStreamService>();

        return services;
    }
}

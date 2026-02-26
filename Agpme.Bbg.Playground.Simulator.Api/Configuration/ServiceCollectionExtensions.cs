using Agpme.Bbg.Playground.Simulator.Api.Configuration;   
using Allspring.Agpme.Bbg.TestsShared.Helpers.Aws;        
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
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AwsSecretsOptions>>().Value;
            if (string.IsNullOrWhiteSpace(opts.Arn))
                throw new InvalidOperationException("StreamAwsSecrets:Arn is required.");
            if (string.IsNullOrWhiteSpace(opts.KeyName))
                throw new InvalidOperationException("StreamAwsSecrets:KeyName is required.");
            if (string.IsNullOrWhiteSpace(opts.Region))
                throw new InvalidOperationException("StreamAwsSecrets:Region is required.");

            // Use the NuGet helper. If Profile is null/empty, it will fall back to default chain.
            var connString = await AwsSecretHelper.GetSecretValueAsync(
                opts.Profile,              // profile or null
                opts.Region!,              // required
                opts.Arn!,                 // secretId
                opts.KeyName!        // valueKey in SecretString JSON
            );

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

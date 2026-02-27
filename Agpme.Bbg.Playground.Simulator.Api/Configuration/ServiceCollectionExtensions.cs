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

        // Resolve the connection string from AWS once per app lifetime (lazy)
        services.AddSingleton(async sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var env = cfg["TargetEnvironment"] ?? "uat";
            var sec = cfg.GetSection($"TargetAwsSecrets_{env}");
            var arn = sec["Arn"] ?? throw new InvalidOperationException("TargetAwsSecrets: Arn missing");
            var key = sec["KeyName"] ?? throw new InvalidOperationException("TargetAwsSecrets: KeyName missing");
            var region = sec["Region"] ?? throw new InvalidOperationException("TargetAwsSecrets: Region missing");
            var profile = sec["Profile"]; // optional

            return await AwsSecretHelper.GetSecretValueAsync(profile, region, arn, key);
        });

        // keep DataSource singleton the same; it uses the Task<string> you just registered

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

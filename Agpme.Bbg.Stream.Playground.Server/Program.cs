using Serilog;
using Agpme.Bbg.Stream.Playground.Server.Configuration;
using Agpme.Bbg.Stream.Playground.Server.Endpoints;
using Microsoft.Extensions.Logging;


var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

Log.Logger = new LoggerConfiguration()
    .Enrich
    .FromLogContext()
    .ReadFrom.Configuration(configuration)
    .CreateLogger();

try
{
    Log.Information("Starting Stream Playground Server");

    var builder = WebApplication.CreateBuilder(args);
    builder.Services.AddStreamingServerServices(builder.Configuration);
    builder.Host.UseSerilog(Log.Logger, dispose: true);

    var app = builder.Build();
    app.UseSerilogRequestLogging();
    app.MapPositionsStreamEndpoints();
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Stream Playground Server terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

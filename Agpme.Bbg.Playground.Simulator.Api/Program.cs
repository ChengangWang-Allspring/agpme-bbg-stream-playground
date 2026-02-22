using Serilog;
using Agpme.Bbg.Playground.Simulator.Api.Configuration;
using Agpme.Bbg.Playground.Simulator.Api.Endpoints;
using Microsoft.Extensions.Logging;


Directory.SetCurrentDirectory(AppContext.BaseDirectory);

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseContentRoot(AppContext.BaseDirectory);

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

Log.Logger = new LoggerConfiguration()
    .Enrich
    .FromLogContext()
    .ReadFrom.Configuration(configuration)
    .CreateLogger();

Log.Information("Starting Stream Playground Server");

builder.Host.UseSerilog(Log.Logger, dispose: true);

builder.Services.AddStreamingServerServices(builder.Configuration);


var app = builder.Build();
app.UseSerilogRequestLogging();

app.MapPositionsStreamEndpoints();

await app.RunAsync();


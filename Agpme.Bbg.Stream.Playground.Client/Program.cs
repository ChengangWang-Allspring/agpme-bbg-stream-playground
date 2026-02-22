using Agpme.Bbg.Stream.Playground.Client.Configuration;
using Agpme.Bbg.Stream.Playground.Client.Endpoints;
using Agpme.Bbg.Stream.Playground.Client.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog (optional)
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());

// Bind options
builder.Services.Configure<PlaygroundClientOptions>(
    builder.Configuration.GetSection("PlaygroundClient"));

// HttpClient -> base = Playground server URL
builder.Services.AddHttpClient("playground", (sp, http) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PlaygroundClientOptions>>().Value;
    http.BaseAddress = new Uri(opts.ServerBaseUrl);
    http.Timeout = Timeout.InfiniteTimeSpan; // streaming
});

// Manager as Singleton
builder.Services.AddSingleton<ISubscriptionManager, SubscriptionManager>();
builder.Services.AddSingleton<IPositionInboundPersister, PositionInboundPersister>();

var app = builder.Build();

// Minimal API endpoints for manager control
app.MapClientEndpoints();

// DO NOT Auto Start for this Playground Client
//// Auto-start all configured on run (optional)
//_ = Task.Run(async () =>
//{
//    try
//    {
//        var mgr = app.Services.GetRequiredService<ISubscriptionManager>();
//        await mgr.StartAllConfiguredAsync(CancellationToken.None);
//    }
//    catch (Exception ex)
//    {
//        Console.Error.WriteLine($"[Client AutoStart ERROR] {ex.Message}");
//    }
//});

await app.RunAsync();
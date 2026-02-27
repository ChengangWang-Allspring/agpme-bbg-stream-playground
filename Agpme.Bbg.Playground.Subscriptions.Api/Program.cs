using Agpme.Bbg.Playground.Subscriptions.Api.Configuration;
using Agpme.Bbg.Playground.Subscriptions.Api.Endpoints;
using Agpme.Bbg.Playground.Subscriptions.Api.Services;
using Agpme.Bbg.Playground.Subscriptions.Api.Comparison;
using Serilog;


Directory.SetCurrentDirectory(AppContext.BaseDirectory);
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseContentRoot(AppContext.BaseDirectory);

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .ReadFrom.Configuration(configuration.GetSection("Serilog_SubscriptionApi"))
    .CreateLogger();


builder.Host.UseSerilog(Log.Logger, dispose: true);


// ---- Swagger for Minimal APIs ----
builder.Services.AddEndpointsApiExplorer();   // discover minimal APIs
builder.Services.AddSwaggerGen();             // generate OpenAPI + UI
// (Optional) include XML docs later if you enable <GenerateDocumentationFile>true</GenerateDocumentationFile>

// ---- Bind options ----
// Agpme.Bbg.Playground.Subscriptions.Api/Program.cs
builder.Services.Configure<PlaygroundClientOptions>(opts =>
{
    opts.ServerBaseUrl = builder.Configuration["SimulatorApiServer"]
        ?? "http://localhost:6066";
    opts.AsOfDate = null;  
    opts.Chunk = true;    

    // Build from SubscriptionTargets array (root)
    var targets = builder.Configuration
        .GetSection("SubscriptionTargets")
        .Get<List<PlaygroundClientOptions.SubscriptionTarget>>() ?? new();
    opts.Targets = targets;
});

// ---- HTTP client for streaming server ----
builder.Services.AddHttpClient("playground", (sp, http) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PlaygroundClientOptions>>().Value;
    http.BaseAddress = new Uri(opts.ServerBaseUrl);
    http.Timeout = Timeout.InfiniteTimeSpan;
});

// ---- Your services ----
builder.Services.AddSingleton<ISubscriptionManager, SubscriptionManager>();
builder.Services.AddSingleton<IPositionInboundPersister, PositionInboundPersister>();
builder.Services.AddSingleton<ClientApi>();
builder.Services.AddSingleton<IResetService, ResetService>();
builder.Services.AddSingleton<IPositionsCompareService, PositionsCompareService>();


var app = builder.Build();

app.UseSerilogRequestLogging();

// ---- Swagger middleware ----
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();    // serves /swagger/v1/swagger.json
    app.UseSwaggerUI();  // serves interactive UI at /swagger
}

// ---- Minimal API endpoints ----
app.MapClientEndpoints();


// Do not autostart subscriptions (keep it disabled)
//// _ = Task.Run(async () => { ... });

await app.RunAsync();
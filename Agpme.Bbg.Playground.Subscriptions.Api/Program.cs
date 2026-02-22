using Agpme.Bbg.Playground.Subscriptions.Api.Configuration;
using Agpme.Bbg.Playground.Subscriptions.Api.Endpoints;
using Agpme.Bbg.Playground.Subscriptions.Api.Services;
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
    .ReadFrom.Configuration(configuration)
    .CreateLogger();

builder.Host.UseSerilog(Log.Logger, dispose: true);


// ---- Swagger for Minimal APIs ----
builder.Services.AddEndpointsApiExplorer();   // discover minimal APIs
builder.Services.AddSwaggerGen();             // generate OpenAPI + UI
// (Optional) include XML docs later if you enable <GenerateDocumentationFile>true</GenerateDocumentationFile>

// ---- Bind options ----
builder.Services.Configure<PlaygroundClientOptions>(
    builder.Configuration.GetSection("PlaygroundClient"));

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
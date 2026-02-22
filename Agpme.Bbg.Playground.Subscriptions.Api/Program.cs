using Agpme.Bbg.Playground.Subscriptions.Api.Configuration;
using Agpme.Bbg.Playground.Subscriptions.Api.Endpoints;
using Agpme.Bbg.Playground.Subscriptions.Api.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog (existing)
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());

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

// ---- Swagger middleware ----
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();    // serves /swagger/v1/swagger.json
    app.UseSwaggerUI();  // serves interactive UI at /swagger
}

// ---- Minimal API endpoints ----
app.MapClientEndpoints();

// (No Blazor, no static files needed)

// Do not autostart subscriptions (keep it disabled)
//// _ = Task.Run(async () => { ... });

await app.RunAsync();
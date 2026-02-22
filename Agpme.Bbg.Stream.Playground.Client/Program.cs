using Agpme.Bbg.Stream.Playground.Client.Configuration;
using Agpme.Bbg.Stream.Playground.Client.Endpoints;
using Agpme.Bbg.Stream.Playground.Client.Services;
using Agpme.Bbg.Stream.Playground.Client.Components;
using Microsoft.AspNetCore.Builder;
using MudBlazor.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog (optional)
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());


// Add Razor Components (Blazor) - interactive (server)
builder.Services.AddRazorComponents().AddInteractiveServerComponents(); // server interactivity

// MudBlazor services
builder.Services.AddMudServices(); // theme, dialogs, snackbars, etc. 


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
builder.Services.AddSingleton<ClientApi>();

var app = builder.Build();

// Minimal API endpoints for manager control
app.MapClientEndpoints();


// Map Razor Components (Blazor)
app.MapRazorComponents<Agpme.Bbg.Stream.Playground.Client.Components.App>()
   .AddInteractiveServerRenderMode();

// (optional) static files if you add logos/images in wwwroot
app.UseStaticFiles();


/*
// Auto-start all configured on run (optional, Do Not Autostart for this Playground)
_ = Task.Run(async () =>
{
    try
    {
        var mgr = app.Services.GetRequiredService<ISubscriptionManager>();
        await mgr.StartAllConfiguredAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[Client AutoStart ERROR] {ex.Message}");
    }
}); */

await app.RunAsync();
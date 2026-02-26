using Agpme.Bbg.Playground.Admin.Components;
using MudBlazor.Services;
using Agpme.Bbg.Playground.Admin.Services;

var builder = WebApplication.CreateBuilder(args);

// Add MudBlazor services
builder.Services.AddMudServices();

// HttpClient to Subscriptions API
var baseAddress = builder.Configuration.GetSection("SubscriptionsApi:BaseAddress").Value
                  ?? "http://localhost:5171"; // fallback if config missing
builder.Services.AddHttpClient("subsapi", http =>
{
    http.BaseAddress = new Uri(baseAddress);
    http.Timeout = Timeout.InfiniteTimeSpan;
});

// Register your typed services
builder.Services.AddSingleton<SubscriptionsClient>();
builder.Services.AddSingleton<CompareClient>();

// Add services to the container.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

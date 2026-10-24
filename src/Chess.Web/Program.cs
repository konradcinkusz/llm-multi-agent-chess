using Chess.ServiceDefaults;
using Chess.Web.Api;
using Chess.Web.Components;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.ServiceDiscovery;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddServiceDiscovery();

// Chess.Api is resolved by service discovery (AppHost resource "api"). The request-scoped
// resilience handler is for the REST calls only: its total timeout would cut the SSE stream.
builder.Services.AddHttpClient<ArenaApiClient>(client => client.BaseAddress = new Uri("http://api"))
    .AddServiceDiscovery()
    .AddStandardResilienceHandler();
builder.Services.AddHttpClient<GameEventsClient>(client =>
    {
        client.BaseAddress = new Uri("http://api");
        client.Timeout = Timeout.InfiniteTimeSpan;
    })
    .AddServiceDiscovery();

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

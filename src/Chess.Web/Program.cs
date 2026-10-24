using Chess.ServiceDefaults;
using Chess.Web.Api;
using Chess.Web.Components;
using Microsoft.Extensions.ServiceDiscovery;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddServiceDiscovery();

// Chess.Api is resolved by service discovery (AppHost resource "api"). ServiceDefaults adds the standard resilience
// handler to every client, so no client adds another: each handler multiplies the attempts. The handler's timeouts
// end when the response headers arrive, so the SSE stream is read after them and is not cut (a 35 s stream completes).
// GameEventsClient's infinite Timeout is what would bound that read, so it stays infinite.
builder.Services.AddHttpClient<ArenaApiClient>(client => client.BaseAddress = new Uri("http://api"))
    .AddServiceDiscovery();
builder.Services.AddHttpClient<GameEventsClient>(client =>
    {
        client.BaseAddress = new Uri("http://api");
        client.Timeout = Timeout.InfiniteTimeSpan;
    })
    .AddServiceDiscovery();

var app = builder.Build();
app.UseOriginGuard();
app.MapDefaultEndpoints();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

// Lets Chess.Web.Tests host the real app (WebApplicationFactory), as Chess.Api.Tests does.
public partial class Program;

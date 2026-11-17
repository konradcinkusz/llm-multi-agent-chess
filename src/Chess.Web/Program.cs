using Chess.ServiceDefaults;
using Chess.Web.Api;
using Chess.Web.Components;
using Chess.Web.Site;
using Chess.Web.Site.Publishing;
using Microsoft.Extensions.ServiceDiscovery;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddServiceDiscovery();
builder.Services.AddSiteExport(builder.Configuration);
builder.Services.AddSitePublishing(builder.Configuration, builder.Environment);

// Chess.Api is resolved by service discovery (AppHost resource "api"). ServiceDefaults adds the standard resilience
// handler to every client, so no client adds another: each handler multiplies the attempts. The handler's timeouts
// end when the response headers arrive, so the SSE stream is read after them and is not cut (a 35 s stream completes).
// HttpClient.Timeout is infinite on both clients: the 100 s default would fire before the handler's 150 s total, so the
// handler alone decides how long a call may take. GameEventsClient's infinite Timeout also keeps the SSE read open.
builder.Services.AddHttpClient<ArenaApiClient>(client =>
    {
        client.BaseAddress = new Uri("http://api");
        client.Timeout = Timeout.InfiniteTimeSpan;
    })
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
// An unknown address answers 404 and the not-found page (a real page in the layout) is rendered for it, status kept.
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

// Lets Chess.Web.Tests host the real app (WebApplicationFactory), as Chess.Api.Tests does.
public partial class Program;

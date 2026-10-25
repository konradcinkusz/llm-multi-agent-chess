using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chess.ServiceDefaults;

/// <summary>
/// Rejects requests that a web page on another site could make through the owner's browser (ADR-006, amended).
/// Loopback binding does not stop them: a browser sends a simple cross-site POST without a preflight, and a websocket
/// handshake is not subject to CORS. A request with no <c>Origin</c> header (curl, server-side HttpClient, health
/// probes, Playwright's request context) is allowed.
/// </summary>
public static class OriginGuard
{
    public const string WebPortKey = "Arena:WebPort";
    public const string ApiPortKey = "Arena:ApiPort";
    public const string ExtraOriginsKey = "Arena:AllowedOrigins";
    public const int DefaultWebPort = 5070;
    public const int DefaultApiPort = 5080;

    private const string OriginHeader = "Origin";
    private const string SecFetchSiteHeader = "Sec-Fetch-Site";
    private const string SecFetchModeHeader = "Sec-Fetch-Mode";
    private const string SecFetchDestHeader = "Sec-Fetch-Dest";
    private const string CrossSite = "cross-site";
    private static readonly string[] Hosts = ["localhost", "127.0.0.1", "[::1]"];

    /// <summary>Registers the guard. Allowed origins are read from configuration once, at startup.</summary>
    public static IApplicationBuilder UseOriginGuard(this IApplicationBuilder app)
    {
        var allowed = AllowedOrigins(app.ApplicationServices.GetRequiredService<IConfiguration>());

        return app.Use(async (context, next) =>
        {
            if (IsForeign(context.Request, allowed))
            {
                await Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Cross-origin request rejected",
                    detail: "This arena accepts requests only from its own origins.").ExecuteAsync(context);
                return;
            }

            await next(context);
        });
    }

    /// <summary>The web and API origins on localhost, 127.0.0.1 and [::1], plus <c>Arena:AllowedOrigins</c>.</summary>
    public static IReadOnlySet<string> AllowedOrigins(IConfiguration configuration)
    {
        var ports = new[]
        {
            configuration.GetValue(WebPortKey, DefaultWebPort),
            configuration.GetValue(ApiPortKey, DefaultApiPort),
        };

        var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var port in ports)
        {
            foreach (var host in Hosts)
            {
                origins.Add($"http://{host}:{port}");
            }
        }

        foreach (var origin in configuration.GetSection(ExtraOriginsKey).Get<string[]>() ?? [])
        {
            origins.Add(origin.TrimEnd('/'));
        }

        return origins;
    }

    /// <summary>True when the request names an origin outside <paramref name="allowed"/>, or came from another site.</summary>
    public static bool IsForeign(HttpRequest request, IReadOnlySet<string> allowed)
    {
        if (request.Headers.TryGetValue(OriginHeader, out var origin) && !allowed.Contains(origin.ToString()))
        {
            return true;
        }

        return string.Equals(request.Headers[SecFetchSiteHeader], CrossSite, StringComparison.OrdinalIgnoreCase)
            && !IsTopLevelNavigation(request);
    }

    // Following a link from another site (the Aspire dashboard is https, the web app http, which browsers call
    // cross-site) is a plain page load. It changes nothing and the other site cannot read the response.
    private static bool IsTopLevelNavigation(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        && string.Equals(request.Headers[SecFetchModeHeader], "navigate", StringComparison.OrdinalIgnoreCase)
        && string.Equals(request.Headers[SecFetchDestHeader], "document", StringComparison.OrdinalIgnoreCase);
}

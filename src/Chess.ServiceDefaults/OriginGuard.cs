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
/// It also refuses a request whose <c>Host</c> header is not the arena's own (DNS rebinding): a page on
/// <c>attacker.example:5080</c> that re-resolves to 127.0.0.1 is same-origin for the browser, sends no <c>Origin</c> on a GET
/// and could otherwise read every response.
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

    // The Web to API calls name the API by its Aspire service name (http://api); the Host header may carry it.
    private const string ApiServiceName = "api";

    /// <summary>Registers the guard. Allowed origins are read from configuration once, at startup.</summary>
    public static IApplicationBuilder UseOriginGuard(this IApplicationBuilder app)
    {
        var configuration = app.ApplicationServices.GetRequiredService<IConfiguration>();
        var allowed = AllowedOrigins(configuration);
        var allowedHosts = AllowedHostValues(configuration);

        return app.Use(async (context, next) =>
        {
            if (!IsAllowedHost(context.Request, allowedHosts))
            {
                await Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Host rejected",
                    detail: "This arena answers only to localhost, 127.0.0.1 and [::1] on its own ports. "
                        + "Open it at one of those addresses, or add the host to Arena:AllowedOrigins.").ExecuteAsync(context);
                return;
            }

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

    /// <summary>
    /// The <c>Host</c> header values the arena answers to: the authority (host and port) of every allowed origin, the same
    /// hosts without a port (the default port, which TestServer and health probes use) and the Aspire service name of the API.
    /// </summary>
    public static IReadOnlySet<string> AllowedHostValues(IConfiguration configuration)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ApiServiceName,
            $"{ApiServiceName}:{configuration.GetValue(ApiPortKey, DefaultApiPort)}",
        };
        foreach (var host in Hosts)
        {
            values.Add(host);
        }

        foreach (var origin in AllowedOrigins(configuration))
        {
            if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            {
                values.Add(uri.Authority);
                values.Add(uri.Host);
            }
        }

        return values;
    }

    /// <summary>True when the request's <c>Host</c> header is one of <paramref name="allowedHosts"/>. A missing Host fails.</summary>
    public static bool IsAllowedHost(HttpRequest request, IReadOnlySet<string> allowedHosts) =>
        request.Host.HasValue && allowedHosts.Contains(request.Host.Value);

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

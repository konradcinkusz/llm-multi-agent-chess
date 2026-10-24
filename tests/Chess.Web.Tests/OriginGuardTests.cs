extern alias ChessWeb;

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Chess.Web.Tests;

/// <summary>
/// ADR-006 (amended): the Blazor circuit is an open websocket on the web port. A foreign page must not negotiate one,
/// and the websocket upgrade (GET /_blazor) must be refused before it starts.
/// </summary>
public class OriginGuardTests
{
    private const string Negotiate = "/_blazor/negotiate?negotiateVersion=1";
    private const string Socket = "/_blazor";
    private const string Foreign = "http://evil.example";

    [Fact]
    public async Task Foreign_origin_cannot_negotiate_a_blazor_circuit()
    {
        await using var app = new WebApp();
        var request = new HttpRequestMessage(HttpMethod.Post, Negotiate);
        request.Headers.Add("Origin", Foreign);

        var response = await app.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Foreign_origin_is_refused_before_the_websocket_upgrade()
    {
        await using var app = new WebApp();
        var request = new HttpRequestMessage(HttpMethod.Get, Socket);
        request.Headers.Add("Origin", Foreign);

        var response = await app.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cross_site_fetch_metadata_is_rejected_even_without_an_origin()
    {
        await using var app = new WebApp();
        var request = new HttpRequestMessage(HttpMethod.Post, Negotiate);
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        var response = await app.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("http://localhost:5070")]
    [InlineData("http://127.0.0.1:5070")]
    [InlineData("http://[::1]:5070")]
    [InlineData("http://localhost:5080")]
    public async Task Owner_origin_negotiates_a_circuit(string origin)
    {
        await using var app = new WebApp();
        var request = new HttpRequestMessage(HttpMethod.Post, Negotiate);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Sec-Fetch-Site", "same-origin");

        var response = await app.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Server_side_request_without_an_origin_negotiates_a_circuit()
    {
        await using var app = new WebApp();

        var response = await app.CreateClient().PostAsync(Negotiate, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Arena_AllowedOrigins_is_honoured_by_the_web_app()
    {
        await using var app = new WebApp(extraOrigin: "http://arena.test:9000");
        var request = new HttpRequestMessage(HttpMethod.Post, Negotiate);
        request.Headers.Add("Origin", "http://arena.test:9000");

        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient().SendAsync(request)).StatusCode);
    }

    private sealed class WebApp(string? extraOrigin = null) : WebApplicationFactory<ChessWeb::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            if (extraOrigin is not null)
            {
                builder.UseSetting("Arena:AllowedOrigins:0", extraOrigin);
            }
        }
    }
}

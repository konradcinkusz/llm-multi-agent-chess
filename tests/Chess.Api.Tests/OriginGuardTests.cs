using System.Net;
using Chess.Api.Tests.Broadcast;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Chess.Api.Tests;

/// <summary>ADR-006 (amended): a page on another site must not reach the API through the owner's browser.</summary>
public class OriginGuardTests
{
    private const string StartPath = "/api/broadcast/schedules/{0}/start";
    private const string NowPath = "/api/broadcast/now";
    private const string Foreign = "http://evil.example";

    [Fact]
    public async Task Foreign_origin_cannot_start_a_broadcast_schedule()
    {
        await using var app = new BroadcastApp(Guid.NewGuid().ToString());
        var client = app.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, string.Format(StartPath, Guid.NewGuid()));
        request.Headers.Add("Origin", Foreign);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(app.Controller.Calls);
    }

    [Fact]
    public async Task Without_an_origin_the_same_start_is_accepted()
    {
        await using var app = new BroadcastApp(Guid.NewGuid().ToString());
        var scheduleId = Guid.NewGuid();

        var response = await app.CreateClient().PostAsync(string.Format(StartPath, scheduleId), null);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        var call = Assert.Single(app.Controller.Calls);
        Assert.Equal(("start", scheduleId), call);
    }

    [Fact]
    public async Task Cross_site_fetch_metadata_is_rejected_even_without_an_origin()
    {
        await using var app = new BroadcastApp(Guid.NewGuid().ToString());
        var request = new HttpRequestMessage(HttpMethod.Post, string.Format(StartPath, Guid.NewGuid()));
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        var response = await app.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(app.Controller.Calls);
    }

    [Theory]
    [InlineData("same-site")]
    [InlineData("same-origin")]
    [InlineData("none")]
    public async Task Fetch_metadata_other_than_cross_site_is_allowed(string site)
    {
        await using var app = new BroadcastApp(Guid.NewGuid().ToString());
        var request = new HttpRequestMessage(HttpMethod.Get, NowPath);
        request.Headers.Add("Sec-Fetch-Site", site);

        var response = await app.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("http://localhost:5070")]
    [InlineData("http://127.0.0.1:5070")]
    [InlineData("http://[::1]:5070")]
    [InlineData("http://localhost:5080")]
    [InlineData("http://127.0.0.1:5080")]
    [InlineData("http://[::1]:5080")]
    public async Task Owner_origins_on_the_web_and_api_ports_are_allowed(string origin)
    {
        await using var app = new BroadcastApp(Guid.NewGuid().ToString());
        var request = new HttpRequestMessage(HttpMethod.Get, NowPath);
        request.Headers.Add("Origin", origin);

        var response = await app.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("http://localhost:9999")]
    [InlineData("https://localhost:5080")]
    [InlineData("http://localhost.evil.example:5080")]
    [InlineData("null")]
    public async Task Other_origins_are_rejected_on_reads_too(string origin)
    {
        await using var app = new BroadcastApp(Guid.NewGuid().ToString());
        var request = new HttpRequestMessage(HttpMethod.Get, NowPath);
        request.Headers.Add("Origin", origin);

        var response = await app.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Arena_AllowedOrigins_extends_the_list_and_nothing_else()
    {
        await using var app = new ExtraOriginApp();
        var client = app.CreateClient();

        var allowed = new HttpRequestMessage(HttpMethod.Get, NowPath);
        allowed.Headers.Add("Origin", "http://arena.test:9000");
        var foreign = new HttpRequestMessage(HttpMethod.Get, NowPath);
        foreign.Headers.Add("Origin", Foreign);

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(allowed)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(foreign)).StatusCode);
    }

    /// <summary>The API on the InMemory provider with one extra origin in configuration.</summary>
    private sealed class ExtraOriginApp : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Database:Provider", "InMemory");
            builder.UseSetting("Database:InMemoryName", Guid.NewGuid().ToString());
            builder.UseSetting("Llm:Provider", "Fake");
            builder.UseSetting("Arena:AllowedOrigins:0", "http://arena.test:9000/");
        }
    }
}

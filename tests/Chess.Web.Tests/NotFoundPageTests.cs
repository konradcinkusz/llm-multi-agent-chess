extern alias ChessWeb;

using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Chess.Web.Tests;

/// <summary>
/// An address the app does not know answered 404 with an empty body: no title, no layout, no way back. The status code is
/// kept, and a real page is rendered inside the layout for it.
/// </summary>
public class NotFoundPageTests
{
    [Theory]
    [InlineData("/no-such-page")]
    [InlineData("/games/abc")]
    [InlineData("/tournaments/not-a-guid")]
    public async Task An_unknown_address_answers_404_with_the_layout_and_a_way_back(string path)
    {
        await using var app = new WebApplicationFactory<ChessWeb::Program>();

        var response = await app.CreateClient().GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("<title>Page not found", html);
        Assert.Contains("Page not found", html);
        Assert.Contains("There is nothing at this address", html);
        Assert.Contains("class=\"app-shell\"", html); // the layout
        Assert.Contains("aria-label=\"Main\"", html); // the navigation
        Assert.Contains("href=\"/results\"", html);   // the way back
        Assert.Contains("data-testid=\"not-found-results\"", html);
    }

    [Fact]
    public async Task The_not_found_page_does_not_wait_for_the_api()
    {
        // The API is not running in this test: the status strip would hang or fail if the server rendering awaited it.
        await using var app = new WebApplicationFactory<ChessWeb::Program>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var response = await app.CreateClient().GetAsync("/no-such-page", cts.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // Server-rendered and never interactive: the strip must not claim to be checking, nothing will ever answer.
        Assert.DoesNotContain("Checking services", await response.Content.ReadAsStringAsync(cts.Token));
    }

    [Fact]
    public async Task A_page_is_not_prerendered_so_the_shell_arrives_without_waiting_for_the_api()
    {
        await using var app = new WebApplicationFactory<ChessWeb::Program>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var response = await app.CreateClient().GetAsync("/games", cts.Token);
        var html = await response.Content.ReadAsStringAsync(cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("_framework/blazor.web.js", html);
        Assert.DoesNotContain("class=\"app-shell\"", html); // nothing of the page is in the HTML: the circuit draws it
    }
}

extern alias ChessWeb;

using System.Net;
using Chess.Web.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Xunit;

namespace Chess.Web.Tests;

/// <summary>
/// ServiceDefaults adds the standard resilience handler to every client. A second one on the same client multiplies the
/// attempts (4 × 4 = 16 for a 5xx), and a POST such as "start" would then be sent sixteen times.
/// </summary>
public class ArenaApiClientResilienceTests
{
    [Fact]
    public async Task A_failing_call_is_attempted_once_with_three_retries()
    {
        await using var app = new FailingApiApp();

        await Assert.ThrowsAnyAsync<HttpRequestException>(() =>
            app.Services.GetRequiredService<ArenaApiClient>().GetStatusAsync());

        Assert.Equal(4, app.Attempts);
    }

    private sealed class FailingApiApp : WebApplicationFactory<ChessWeb::Program>
    {
        public int Attempts { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services =>
            {
                services.ConfigureAll<HttpStandardResilienceOptions>(options => options.Retry.Delay = TimeSpan.Zero);
                services.AddHttpClient<ArenaApiClient>(client => client.BaseAddress = new Uri("http://127.0.0.1:1"))
                    .ConfigurePrimaryHttpMessageHandler(() => new CountingHandler(() => Attempts++));
            });
    }

    private sealed class CountingHandler(Action onAttempt) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            onAttempt();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}

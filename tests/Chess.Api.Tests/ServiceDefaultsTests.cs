using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Chess.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Chess.Api.Tests;

public class ServiceDefaultsTests
{
    [Fact]
    public async Task HealthListsEveryRegisteredCheckWithStatusAndDescription()
    {
        await using var app = await StartHostAsync(checks =>
        {
            checks.AddCheck("database", () => HealthCheckResult.Healthy("connected"));
            checks.AddCheck("stockfish", () => HealthCheckResult.Unhealthy("binary not found"));
        });

        var response = await app.GetTestClient().GetAsync("/health");
        var checks = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("checks");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(["database", "self", "stockfish"], checks.EnumerateObject().Select(check => check.Name).Order());
        Assert.Equal("Healthy", checks.GetProperty("database").GetProperty("status").GetString());
        Assert.Equal("connected", checks.GetProperty("database").GetProperty("description").GetString());
        Assert.Equal("Unhealthy", checks.GetProperty("stockfish").GetProperty("status").GetString());
        Assert.Equal("binary not found", checks.GetProperty("stockfish").GetProperty("description").GetString());
    }

    [Fact]
    public async Task AliveIgnoresNonLiveChecksWhileHealthFails()
    {
        await using var app = await StartHostAsync(checks =>
            checks.AddCheck("stockfish", () => HealthCheckResult.Unhealthy("binary not found")));

        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/alive")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task AliveFailsWhenALiveCheckFails()
    {
        await using var app = await StartHostAsync(checks =>
            checks.AddCheck("broker", () => HealthCheckResult.Unhealthy("down"), tags: ["live"]));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await app.GetTestClient().GetAsync("/alive")).StatusCode);
    }

    [Fact]
    public async Task StartupLogsIntegrationStatusLineFromRegisteredChecks()
    {
        var logs = new CapturingLoggerProvider();
        await using var app = await StartHostAsync(
            checks => checks.AddCheck("database", () => HealthCheckResult.Healthy()),
            logs);

        string? line = null;
        for (var attempt = 0; attempt < 100 && line is null; attempt++)
        {
            line = logs.Messages.FirstOrDefault(message => message.StartsWith("Integrations: ", StringComparison.Ordinal));
            if (line is null)
            {
                await Task.Delay(50);
            }
        }

        Assert.NotNull(line);
        Assert.Contains("database=Healthy", line);
        Assert.Contains("self=Healthy", line);
    }

    private static async Task<WebApplication> StartHostAsync(Action<IHealthChecksBuilder> checks, ILoggerProvider? logProvider = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        if (logProvider is not null)
        {
            builder.Logging.AddProvider(logProvider);
        }

        builder.AddServiceDefaults();
        checks(builder.Services.AddHealthChecks());

        var app = builder.Build();
        app.MapDefaultEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                provider.Messages.Enqueue(formatter(state, exception));
        }
    }
}

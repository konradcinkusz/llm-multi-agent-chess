using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chess.ServiceDefaults;

internal static class HealthReportWriter
{
    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("status", report.Status.ToString());
            json.WriteString("totalDuration", report.TotalDuration.ToString("c"));
            json.WriteStartObject("checks");
            foreach (var (name, entry) in report.Entries)
            {
                json.WriteStartObject(name);
                json.WriteString("status", entry.Status.ToString());
                json.WriteString("description", entry.Description);
                json.WriteString("duration", entry.Duration.ToString("c"));
                json.WriteEndObject();
            }
            json.WriteEndObject();
            json.WriteEndObject();
        }

        await context.Response.Body.WriteAsync(stream.ToArray());
    }
}

// Logs one line once the host has started: "Integrations: <name>=<status>, …".
internal sealed class IntegrationStatusReporter(
    IHostApplicationLifetime lifetime,
    HealthCheckService health,
    ILogger<IntegrationStatusReporter> logger) : IHostedService
{
    private CancellationTokenRegistration _started;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _started = lifetime.ApplicationStarted.Register(() => _ = ReportAsync());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _started.Dispose();
        return Task.CompletedTask;
    }

    private async Task ReportAsync()
    {
        try
        {
            var report = await health.CheckHealthAsync();
            var line = string.Join(", ", report.Entries.Select(entry => $"{entry.Key}={entry.Value.Status}"));
            logger.LogInformation("Integrations: {Integrations}", line);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Integration status check failed");
        }
    }
}

using Chess.Api.Contracts;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Chess.Api.Catalog;

/// <summary>
/// Builds <see cref="StatusDto"/> from the health check results, read by name. A check that is not
/// registered (for example <c>database</c> before its task lands) is reported as <c>unknown</c>.
/// </summary>
public sealed class StatusReporter(HealthCheckService health)
{
    private const string Unknown = "unknown";

    public async Task<StatusDto> GetStatusAsync(CancellationToken ct)
    {
        var report = await health.CheckHealthAsync(ct);
        var copilot = Find(report, "copilot");

        return new StatusDto(
            Copilot: copilot.Status,
            CopilotDetail: copilot.Detail,
            Database: Find(report, "database").Status,
            Stockfish: Find(report, "stockfish").Status);
    }

    private static (string Status, string Detail) Find(HealthReport report, string name) =>
        report.Entries.TryGetValue(name, out var entry)
            ? (entry.Status.ToString().ToLowerInvariant(), entry.Description ?? string.Empty)
            : (Unknown, Unknown);
}

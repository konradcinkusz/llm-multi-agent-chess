using Chess.Api.Contracts;
using Chess.Llm;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Chess.Api.Catalog;

/// <summary>
/// Builds <see cref="StatusDto"/>. <c>Copilot</c> is the gateway's own state (<c>Ready | NotAuthenticated |
/// Unavailable | Fake</c>, 03 §8) because the Play page keys off it; the other fields come from the health
/// check results, read by name. A check that is not registered is reported as <c>unknown</c>.
/// </summary>
public sealed class StatusReporter(HealthCheckService health, ILlmGateway gateway)
{
    private const string Unknown = "unknown";

    public async Task<StatusDto> GetStatusAsync(CancellationToken ct)
    {
        var report = await health.CheckHealthAsync(ct);
        var gatewayStatus = gateway.Status;

        return new StatusDto(
            Copilot: gatewayStatus.State.ToString(),
            CopilotDetail: gatewayStatus.Detail,
            Database: Find(report, "database").Status,
            Stockfish: Find(report, "stockfish").Status);
    }

    private static (string Status, string Detail) Find(HealthReport report, string name) =>
        report.Entries.TryGetValue(name, out var entry)
            ? (entry.Status.ToString().ToLowerInvariant(), entry.Description ?? string.Empty)
            : (Unknown, Unknown);
}

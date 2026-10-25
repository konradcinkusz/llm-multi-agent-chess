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
    private const string Healthy = "healthy";
    private const string StockfishAvailable = "available";
    private const string StockfishAbsent = "absent";

    public async Task<StatusDto> GetStatusAsync(CancellationToken ct)
    {
        var report = await health.CheckHealthAsync(ct);
        var gatewayStatus = gateway.Status;

        return new StatusDto(
            Copilot: gatewayStatus.State.ToString(),
            CopilotDetail: gatewayStatus.Detail,
            Database: Find(report, "database").Status,
            Stockfish: Stockfish(Find(report, "stockfish")));
    }

    // Absent Stockfish is Healthy (optional, not broken), so the verdict cannot tell it from available. The check's
    // description can: "available" or "absent — move quality disabled" (03 §8). Any other verdict passes through.
    private static string Stockfish((string Status, string Detail) entry)
    {
        if (entry.Status != Healthy)
        {
            return entry.Status;
        }

        if (entry.Detail == StockfishAvailable)
        {
            return StockfishAvailable;
        }

        return entry.Detail.StartsWith(StockfishAbsent, StringComparison.Ordinal) ? StockfishAbsent : entry.Status;
    }

    private static (string Status, string Detail) Find(HealthReport report, string name) =>
        report.Entries.TryGetValue(name, out var entry)
            ? (entry.Status.ToString().ToLowerInvariant(), entry.Description ?? string.Empty)
            : (Unknown, Unknown);
}

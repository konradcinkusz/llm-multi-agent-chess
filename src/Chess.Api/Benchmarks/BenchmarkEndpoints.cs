using Chess.Api.Catalog;
using Chess.Api.Contracts;
using Chess.Api.Games;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Chess.Api.Benchmarks;

public static class BenchmarkEndpoints
{
    /// <summary>
    /// <c>/api/benchmarks</c> (03 §12.2): create, list, read, cancel, and the report as JSON or, with <c>?format=md</c>,
    /// as Markdown to share.
    /// </summary>
    public static IEndpointRouteBuilder MapBenchmarkEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/benchmarks", async Task<Results<Created<BenchmarkCreatedDto>, ValidationProblem>> (
            CreateBenchmarkRequest request,
            BenchmarkService benchmarks,
            ModelCatalog catalog,
            IOptions<GameRunnerOptions> limits,
            CancellationToken ct) =>
        {
            var listing = await catalog.GetModelsAsync(ct);
            var opponents = BenchmarkRules.OpponentsOf(request, listing);
            var errors = BenchmarkRules.Validate(request, opponents, listing, limits.Value.MaxConcurrentGames);
            if (errors.Count > 0)
            {
                return TypedResults.ValidationProblem(errors);
            }

            var created = await benchmarks.CreateAsync(request, opponents, ct);
            return TypedResults.Created($"/api/benchmarks/{created.Benchmark.Id}", created);
        });

        endpoints.MapGet("/benchmarks", (BenchmarkService benchmarks, CancellationToken ct) =>
            benchmarks.ListAsync(ct));

        endpoints.MapGet("/benchmarks/{id:guid}", async Task<Results<Ok<BenchmarkDto>, ProblemHttpResult>> (
            Guid id, BenchmarkService benchmarks, CancellationToken ct) =>
        {
            var benchmark = await benchmarks.GetAsync(id, ct);
            return benchmark is null ? NotFound(id) : TypedResults.Ok(benchmark);
        });

        endpoints.MapGet("/benchmarks/{id:guid}/report", async Task<IResult> (
            Guid id, string? format, BenchmarkService benchmarks, CancellationToken ct) =>
        {
            if (format is not (null or "json" or "md"))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["format"] = [$"Format must be 'json' or 'md', was '{format}'."],
                });
            }

            var report = await benchmarks.ReportAsync(id, ct);
            if (report is null)
            {
                return NotFound(id);
            }

            return format == "md"
                ? TypedResults.Text(BenchmarkMarkdown.Render(report), "text/markdown; charset=utf-8")
                : TypedResults.Ok(report);
        });

        endpoints.MapPost("/benchmarks/{id:guid}/cancel", async Task<Results<Ok<BenchmarkDto>, ProblemHttpResult>> (
            Guid id, BenchmarkService benchmarks, CancellationToken ct) =>
        {
            var benchmark = await benchmarks.CancelAsync(id, ct);
            return benchmark is null ? NotFound(id) : TypedResults.Ok(benchmark);
        });

        return endpoints;
    }

    private static ProblemHttpResult NotFound(Guid id) => TypedResults.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Benchmark not found",
        detail: $"No benchmark has the id {id}.");
}

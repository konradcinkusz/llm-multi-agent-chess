using Chess.Analysis;

namespace Chess.Api.Tests.Analysis;

/// <summary>Answers searches from a table keyed by position. An unscripted position fails the test.</summary>
internal sealed class ScriptedUciEngine : IUciEngine
{
    private readonly Dictionary<string, UciSearch> _answers = new();

    public List<(string Fen, string Moves, int Depth)> Requests { get; } = [];

    public ScriptedUciEngine Answer(string fen, string moves, UciSearch search)
    {
        _answers[Key(fen, moves)] = search;
        return this;
    }

    public Task<UciSearch> SearchAsync(string fen, IReadOnlyList<string> uciMoves, int depth, CancellationToken ct)
    {
        var moves = string.Join(' ', uciMoves);
        Requests.Add((fen, moves, depth));
        return _answers.TryGetValue(Key(fen, moves), out var search)
            ? Task.FromResult(search)
            : throw new InvalidOperationException($"No scripted answer for '{fen}' after '{moves}'.");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static string Key(string fen, string moves) => $"{fen}|{moves}";
}

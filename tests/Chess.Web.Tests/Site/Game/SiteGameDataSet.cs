using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Chess.Engine;
using Chess.Web.Api;

namespace Chess.Web.Tests.Site.Game;

/// <summary>
/// The per-game answers of the fake API for the game pages: the moves of a game (legal, replayed by the engine), its engine
/// log (one legal attempt per ply, and a rejected first attempt at ply 1) and its PGN. Any other call is answered by the caller.
/// </summary>
internal static class SiteGameDataSet
{
    /// <summary>The page size of <c>GET /api/attempts</c> here, so a game's log takes more than one request.</summary>
    public const int AttemptPageSize = 20;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The answer to a game call, or null when the call is not one of the game's.</summary>
    public static HttpResponseMessage? Respond(string path, NameValueCollection query, IReadOnlyList<GameSummaryDto> games)
    {
        if (path == "/api/attempts")
        {
            return Body(Attempts(Guid.Parse(query["gameId"]!), int.Parse(query["page"] ?? "1", CultureInfo.InvariantCulture), games));
        }

        // "", "api", "games", "{id}" and, for the PGN, "pgn".
        var parts = path.Split('/');
        if (parts.Length < 4 || parts[1] != "api" || parts[2] != "games" || !Guid.TryParse(parts[3], out var id))
        {
            return null;
        }

        var summary = games.FirstOrDefault(game => game.Id == id);
        if (summary is null)
        {
            return null;
        }

        return parts.Length switch
        {
            4 => Body(Play(summary, KnightShuffle(summary.PlyCount))),
            5 when parts[4] == "pgn" => Text(PgnText(summary)),
            _ => null,
        };
    }

    /// <summary>The game as the API answers it: its moves from the start position, each replayed by the engine.</summary>
    public static GameDetailDto Play(GameSummaryDto summary, IReadOnlyList<string> sans)
    {
        var board = ChessBoard.StartingPosition();
        var startFen = board.ToFen();
        var moves = new List<MoveDto>();
        for (var index = 0; index < sans.Count; index++)
        {
            var ply = index + 1;
            if (!board.ApplyMoveSan(sans[index]))
            {
                throw new InvalidOperationException($"{sans[index]} is not legal at ply {ply}.");
            }

            var color = ply % 2 == 1 ? "white" : "black";
            moves.Add(new MoveDto(ply, color, sans[index], board.ToFen(), Attempts: ply == 1 ? 2 : 1, LatencyMs: 1200,
                InputTokens: 500, OutputTokens: 20, CentipawnLoss: null, BestSan: null));
        }

        return new GameDetailDto(summary, startFen, board.ToFen(), moves);
    }

    /// <summary>The PGN the API writes for the game: its moves and result, with the two models as the players.</summary>
    public static string PgnText(GameSummaryDto game)
    {
        var tags = new Dictionary<string, string>
        {
            ["Site"] = "LLM Chess Arena",
            ["White"] = game.White.ModelId,
            ["Black"] = game.Black.ModelId,
        };
        return Pgn.Write(tags, null, KnightShuffle(game.PlyCount), game.Result);
    }

    /// <summary>
    /// Knight moves out and back, legal from the start and never a capture: a game of any length with no decisions in it.
    /// Ply 1 is White's Nf3, ply 2 Black's Nf6, and so on.
    /// </summary>
    public static IReadOnlyList<string> KnightShuffle(int plies) => Enumerable.Range(1, plies)
        .Select(ply => (ply % 2, ply % 4) switch
        {
            (1, 1) => "Nf3",
            (1, _) => "Ng1",
            (0, 2) => "Nf6",
            _ => "Ng8",
        })
        .ToList();

    private static PageDto<AttemptDto> Attempts(Guid gameId, int page, IReadOnlyList<GameSummaryDto> games)
    {
        var summary = games.FirstOrDefault(game => game.Id == gameId);
        var all = new List<AttemptDto>();
        if (summary is not null)
        {
            foreach (var move in Play(summary, KnightShuffle(summary.PlyCount)).Moves)
            {
                var model = move.Color == "white" ? summary.White.ModelId : summary.Black.ModelId;
                if (move.Ply == 1)
                {
                    all.Add(new AttemptDto(gameId, 1, 1, model, "Prompt for ply 1", "Qxe9", null, false, 900, null,
                        summary.CreatedAt, Reasoning: null, InputTokens: 500, OutputTokens: 20, RejectionKind: "illegal"));
                }

                all.Add(new AttemptDto(gameId, move.Ply, move.Ply == 1 ? 2 : 1, model, $"Prompt for ply {move.Ply}", move.San,
                    move.San, true, 1200, null, summary.CreatedAt,
                    Reasoning: move.Color == "white" ? "Developing the knight." : null,
                    InputTokens: 500, OutputTokens: 20, ReasoningTokens: null, AiCredits: 1.25));
            }
        }

        var items = all.Skip((page - 1) * AttemptPageSize).Take(AttemptPageSize).ToList();
        return new PageDto<AttemptDto>(items, page, AttemptPageSize, all.Count);
    }

    private static HttpResponseMessage Body<T>(T value) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value, Json), Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage Text(string text) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(text, Encoding.UTF8, "application/x-chess-pgn"),
        };
}

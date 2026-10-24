using Chess.Api.Tests.Players;

namespace Chess.Api.Tests.Evals;

/// <summary>
/// The offline scenario set (06-TASKS T4.2): 30 positions as data. Each one is a FEN and the replies a model gives,
/// including the adversarial ones: prose around the move, wrong case, UCI, illegal moves, several moves in one reply,
/// and empty replies. Replies are judged by <see cref="Chess.Engine.MoveNotation"/>; see docs/EVALS.md for the contract.
/// </summary>
internal static class EvalScenarios
{
    private const string AfterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";
    private const string AfterE4F6 = "rnbqkbnr/ppppp1pp/5p2/8/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2";

    /// <summary>After 1.e4 e5 2.Bc4 Nc6 3.Qh5 Nf6 with White to move: White can castle kingside (and only kingside).</summary>
    private const string ItalianCastling = "r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4";

    /// <summary>Both sides can castle on both wings.</summary>
    private const string BothWingCastling = "r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1";

    /// <summary>A pawn on the seventh rank: a7-a8 promotes only with a piece letter.</summary>
    private const string Promotion = "8/P7/8/8/8/8/8/k6K w - - 0 1";

    /// <summary>Two knights that both reach d2, so the SAN needs a file: Nbd2 and Nfd2.</summary>
    private const string TwoKnightsToD2 = "4k3/8/8/8/8/5N2/8/1N2K3 w - - 0 1";

    /// <summary>After 1.e4 e5 2.Bc4 Nc6 3.Qh5 Nf6 4.Qxf7 is mate.</summary>
    private const string ScholarsMate = "r1bqkb1r/pppp1ppp/2n2n2/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4";

    public static IReadOnlyList<EvalScenario> All { get; } =
    [
        // Accepted: the reply is the move, in one of the forms MoveNotation accepts.
        Accepted("accepted-san-e4", PlayerTestData.StartFen, "e4", "e4"),
        Accepted("accepted-san-nf3", PlayerTestData.StartFen, "Nf3", "Nf3"),
        Accepted("accepted-uci-e2e4", PlayerTestData.StartFen, "e2e4", "e4"),
        Accepted("accepted-uci-g1f3", PlayerTestData.StartFen, "g1f3", "Nf3"),
        Accepted("accepted-move-number-dot", PlayerTestData.StartFen, "1. d4", "d4"),
        Accepted("accepted-move-number-ellipsis", AfterE4, "1... e5", "e5"),
        Accepted("accepted-quoted", PlayerTestData.StartFen, "\"c4\"", "c4"),
        Accepted("accepted-trailing-punctuation", PlayerTestData.StartFen, "Nc3!", "Nc3"),
        Accepted("accepted-check-mark-optional", AfterE4F6, "Qh5", "Qh5+"),
        Accepted("accepted-castling-letters", ItalianCastling, "O-O", "O-O"),
        Accepted("accepted-castling-zeros", ItalianCastling, "0-0", "O-O"),
        Accepted("accepted-castling-queenside-zeros", BothWingCastling, "0-0-0", "O-O-O"),
        Accepted("accepted-promotion-uci", Promotion, "a7a8q", "a8=Q+"),
        Accepted("accepted-mate-suffix", ScholarsMate, "Qxf7#", "Qxf7#"),
        Accepted("accepted-disambiguated-san", TwoKnightsToD2, "Nbd2", "Nbd2"),

        // Recovers: adversarial replies are rejected, the corrective follow-up gets a legal move.
        Recovers("recovers-prose-then-san", PlayerTestData.StartFen, ["I would play e4.", "e4"], "e4"),
        Recovers("recovers-wrong-case-then-san", PlayerTestData.StartFen, ["nf3", "Nf3"], "Nf3"),
        Recovers("recovers-illegal-then-legal", PlayerTestData.StartFen, ["Qxf7", "d4"], "d4"),
        Recovers("recovers-empty-then-legal", PlayerTestData.StartFen, ["", "e4"], "e4"),
        Recovers("recovers-uci-without-promotion", Promotion, ["a7a8", "a7a8q"], "a8=Q+"),
        Recovers("recovers-two-moves-in-one-reply", PlayerTestData.StartFen, ["e4 e5", "e4"], "e4"),
        Recovers("recovers-ambiguous-san", TwoKnightsToD2, ["Nd2", "Nbd2"], "Nbd2"),
        Recovers("recovers-illegal-uci-then-legal", PlayerTestData.StartFen, ["e2e5", "e2e4"], "e4"),
        Recovers("recovers-on-last-attempt", PlayerTestData.StartFen, ["Ke2", "Ke2", "Nf3"], "Nf3"),
        Recovers("recovers-illegal-castling", ItalianCastling, ["O-O-O", "O-O"], "O-O"),

        // Forfeits: all three attempts are adversarial; the side loses on time and no move is made for it.
        Forfeits("forfeits-prose", PlayerTestData.StartFen, "I play e4", "My move is d4.", "Let's go with Nf3"),
        Forfeits("forfeits-wrong-case", PlayerTestData.StartFen, "NF3", "e2E4", "nf3"),
        Forfeits("forfeits-illegal", PlayerTestData.StartFen, "Qxf7", "Ke2", "e5"),
        Forfeits("forfeits-empty", PlayerTestData.StartFen, "", "   ", "\n"),
        Forfeits("forfeits-mixed", PlayerTestData.StartFen, "e2e5", "I think E4", "E4"),
    ];

    private static EvalScenario Accepted(string id, string fen, string reply, string expectedSan) =>
        new(id, ScenarioClass.Accepted, fen, [reply], expectedSan);

    private static EvalScenario Recovers(string id, string fen, string[] replies, string expectedSan) =>
        new(id, ScenarioClass.Recovers, fen, replies, expectedSan);

    private static EvalScenario Forfeits(string id, string fen, params string[] replies) =>
        new(id, ScenarioClass.Forfeits, fen, replies, null);
}

namespace Chess.Web.Api;

// Copies of the Chess.Api.Contracts records (03 §5.1). This project must not reference Chess.Api (P3),
// so the shapes are kept in sync by a round-trip test in Chess.Web.Tests, not by a shared assembly.

public sealed record PlayerSpec(string Kind, string ModelId);                       // Kind: "llm" | "random"
public sealed record CreateGameRequest(PlayerSpec White, PlayerSpec Black, bool Rated);
public sealed record GameSummaryDto(Guid Id, Guid? TournamentId, PlayerSpec White, PlayerSpec Black, bool Rated,
    string State, string Result, string? Termination, int PlyCount,
    DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt);                          // State: queued | running | finished
public sealed record MoveDto(int Ply, string Color, string San, string FenAfter, int Attempts, long LatencyMs,
    int? InputTokens, int? OutputTokens, int? CentipawnLoss, string? BestSan);
public sealed record GameDetailDto(GameSummaryDto Summary, string StartFen, string CurrentFen, IReadOnlyList<MoveDto> Moves);
public sealed record PageDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
public sealed record CreateTournamentRequest(string Name, IReadOnlyList<string> Models, int GamesPerPair, int MaxConcurrency);
public sealed record TournamentDto(Guid Id, string Name, IReadOnlyList<string> Models, int GamesPerPair, string Status,
    int GamesTotal, int GamesFinished, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt);  // Status: running | finished | cancelled
public sealed record TournamentCreatedDto(TournamentDto Tournament, IReadOnlyList<ModelPremiumEstimateDto> EstimatedPremiumRequests);
// Premium requests are null when the multiplier is unknown; the credit and USD estimates are null when the prices are.
// TokenBasis: history | assumed | free | unknown (03 §10.2); SampleMoves is set for history.
public sealed record ModelPremiumEstimateDto(string ModelId, double? EstimatedPremiumRequests,
    double? EstimatedAiCredits = null, double? EstimatedUsd = null, string? TokenBasis = null, int? SampleMoves = null);
public sealed record ModelDto(string Id, string DisplayName, double? PremiumMultiplier, bool Enabled, ModelPricingDto? Pricing = null,
    IReadOnlyList<string>? ReasoningEfforts = null, string? DefaultReasoningEffort = null, bool Custom = false);   // 03 §12.1
public sealed record ModelPricingDto(double? InputUsdPerMillionTokens, double? OutputUsdPerMillionTokens,
    double? CachedInputUsdPerMillionTokens);
public sealed record StatusDto(string Copilot, string CopilotDetail, string Database, string Stockfish);

// Local copies of Chess.Rating.ModelRating and PairwiseCoefficient (03 §3.4).
public sealed record ModelRating(string Model, double Rating, double CiLow, double CiHigh, int Games, double Score,
    double Legality, double? MoveQuality, double Quality, double ArenaScore, bool Provisional,
    double? ArenaScoreLow = null, double? ArenaScoreHigh = null);   // R1: 2.5 / 97.5 percentiles of the arena score
public sealed record PairwiseCoefficient(string A, string B, double ExpectedScore, double QualityShare,
    double Rqc, double RqcLow, double RqcHigh, double EloEquivalent, int HeadToHeadGames, double? HeadToHeadScore);

public sealed record LeaderboardDto(IReadOnlyList<ModelRating> Models, double WhiteAdvantage, int Games, bool MoveQualityAvailable);
public sealed record MatrixDto(IReadOnlyList<string> Models, IReadOnlyList<PairwiseCoefficient> Cells);
public sealed record RatingHistoryPointDto(DateTimeOffset TakenAt, int RatedGames, string Model, double Rating,
    double CiLow, double CiHigh, double ArenaScore, bool Provisional);
// M1 (03 §13): the two cost and forfeit coefficients of the model map; null when the API has none.
public sealed record ModelResultsDto(string Model, int Games, int Wins, int Draws, int Losses, int ForfeitLosses,
    int Moves, double FirstAttemptLegalRate, double AvgLatencyMs, double? AvgInputTokens, double? AvgOutputTokens,
    double? AverageCentipawnLoss, double? AvgEstimatedUsdPerGame = null, double? ForfeitRate = null);
public sealed record AttemptDto(Guid GameId, int Ply, int Number, string ModelId, string Prompt, string? RawReply,
    string? ParsedSan, bool Legal, long LatencyMs, string? Error, DateTimeOffset CreatedAt,
    string? Reasoning = null, int? InputTokens = null, int? OutputTokens = null, int? ReasoningTokens = null,
    double? AiCredits = null, string? RejectionKind = null);       // RejectionKind (03 §10): null when legal

// 03 §10: GET /api/games/{id}/usage. CostBasis: reported | estimated | partial | unknown | free.
public sealed record SideUsageDto(string Color, string ModelId, int Requests, long InputTokens, long OutputTokens,
    long ReasoningTokens, long CachedInputTokens, double? AiCredits, double? EstimatedUsd, string CostBasis);
public sealed record GameUsageDto(Guid GameId, SideUsageDto White, SideUsageDto Black, double? TotalAiCredits,
    double? TotalEstimatedUsd, double UsdPerAiCredit);

// 03 §10 (G5): GET /api/cost/inputs. Models lists the models with stored moves; the rest are assumed.
public sealed record CostInputsDto(double UsdPerAiCredit, int AssumedInputTokensPerMove, int AssumedOutputTokensPerMove,
    IReadOnlyList<ModelTokenAverageDto> Models);
public sealed record ModelTokenAverageDto(string ModelId, int Moves, double? AverageInputTokens, double? AverageOutputTokens);

// 03 §12.2 (B2): the benchmark records, copied from Chess.Api.Benchmarks. Status is a tournament's: running | finished | cancelled.
public sealed record CreateBenchmarkRequest(string Name, string Candidate, IReadOnlyList<string>? Opponents,
    int GamesPerOpponent = 30, int MaxConcurrency = 2);
public sealed record BenchmarkDto(Guid Id, string Name, string Candidate, IReadOnlyList<string> Opponents, int GamesPerOpponent,
    string Status, int GamesTotal, int GamesFinished, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt);
public sealed record BenchmarkCreatedDto(BenchmarkDto Benchmark, IReadOnlyList<ModelPremiumEstimateDto> EstimatedPremiumRequests);
public sealed record BenchmarkOpponentDto(string Opponent, int Games, int Wins, int Draws, int Losses, int ForfeitLosses,
    double Score, double? EloDiff, double? EloDiffLow, double? EloDiffHigh, double? Rqc, double? RqcLow, double? RqcHigh,
    double CandidateFirstAttemptLegalRate, double? CandidateEstimatedUsd);
public sealed record BenchmarkSummaryDto(int Games, double Score, double? Rating, double? RatingLow, double? RatingHigh,
    double? ArenaScore, bool Provisional, long CandidateTokens, double? CandidateEstimatedUsd, double? TotalEstimatedUsd);
public sealed record BenchmarkFingerprintDto(string AppVersion, string FormulaVersion, string PromptVersion, DateTimeOffset GeneratedAt);
public sealed record BenchmarkReportDto(BenchmarkDto Benchmark, IReadOnlyList<BenchmarkOpponentDto> Opponents,
    BenchmarkSummaryDto Summary, BenchmarkFingerprintDto Fingerprint);

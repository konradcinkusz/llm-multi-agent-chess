using Chess.Api.Tournaments;

namespace Chess.Api.Benchmarks;

// 03 §12.2: the benchmark records (frozen 2026-10-25). Status is a tournament's: running | finished | cancelled.
public sealed record CreateBenchmarkRequest(string Name, string Candidate, IReadOnlyList<string>? Opponents,
    int GamesPerOpponent = 30, int MaxConcurrency = 2);
public sealed record BenchmarkDto(Guid Id, string Name, string Candidate, IReadOnlyList<string> Opponents, int GamesPerOpponent,
    string Status, int GamesTotal, int GamesFinished, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt);

/// <summary>
/// The response to <c>POST /api/benchmarks</c>: the new run and the premium requests it is expected to cost, as
/// <see cref="TournamentCreatedDto"/> does for a tournament.
/// </summary>
public sealed record BenchmarkCreatedDto(BenchmarkDto Benchmark, IReadOnlyList<ModelPremiumEstimateDto> EstimatedPremiumRequests);
public sealed record BenchmarkOpponentDto(string Opponent, int Games, int Wins, int Draws, int Losses, int ForfeitLosses,
    double Score, double? EloDiff, double? EloDiffLow, double? EloDiffHigh, double? Rqc, double? RqcLow, double? RqcHigh,
    double CandidateFirstAttemptLegalRate, double? CandidateEstimatedUsd);
public sealed record BenchmarkSummaryDto(int Games, double Score, double? Rating, double? RatingLow, double? RatingHigh,
    double? ArenaScore, bool Provisional, long CandidateTokens, double? CandidateEstimatedUsd, double? TotalEstimatedUsd);
public sealed record BenchmarkFingerprintDto(string AppVersion, string FormulaVersion, string PromptVersion, DateTimeOffset GeneratedAt);
public sealed record BenchmarkReportDto(BenchmarkDto Benchmark, IReadOnlyList<BenchmarkOpponentDto> Opponents,
    BenchmarkSummaryDto Summary, BenchmarkFingerprintDto Fingerprint);

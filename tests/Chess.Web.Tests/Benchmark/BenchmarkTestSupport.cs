using System.Net;
using Chess.Web.Api;
using Chess.Web.Tests.Support;

namespace Chess.Web.Tests.Benchmark;

internal sealed record Reply(HttpStatusCode Status, string Json);

internal static class Serve
{
    /// <summary>Routes are "METHOD /path?query". An unknown route answers 404, so a call the test did not expect fails loudly.</summary>
    public static FakeHandler Routes(IReadOnlyDictionary<string, Reply> routes) => new(request =>
        routes.TryGetValue($"{request.Method} {request.Target}", out var reply)
            ? Canned.Json(reply.Json, reply.Status)
            : Canned.Json("{}", HttpStatusCode.NotFound));

    public static Reply Ok(string json) => new(HttpStatusCode.OK, json);

    public static Reply Created(string json) => new(HttpStatusCode.Created, json);

    public static Reply Fail(HttpStatusCode status, string json = "{}") => new(status, json);
}

internal static class Fixtures
{
    private const string Prices = """{"inputUsdPerMillionTokens":3,"outputUsdPerMillionTokens":15,"cachedInputUsdPerMillionTokens":0.3}""";

    /// <summary>
    /// Fake A and Fake B list three efforts (default medium). GPT is priced without efforts and has a premium multiplier.
    /// "My model" is a custom model. Auto and Retired are not offered as opponents by default (Auto) or at all (disabled).
    /// </summary>
    public static readonly string Models = $$"""
        [
          {"id":"fake-a","displayName":"Fake A","premiumMultiplier":0,"enabled":true,"pricing":{{Prices}},"reasoningEfforts":["low","medium","high"],"defaultReasoningEffort":"medium","custom":false},
          {"id":"fake-b","displayName":"Fake B","premiumMultiplier":0,"enabled":true,"pricing":{{Prices}},"reasoningEfforts":["low","medium","high"],"defaultReasoningEffort":"medium","custom":false},
          {"id":"gpt","displayName":"GPT","premiumMultiplier":1,"enabled":true,"pricing":{{Prices}},"custom":false},
          {"id":"local","displayName":"My model","premiumMultiplier":null,"enabled":true,"custom":true},
          {"id":"random","displayName":"Random","premiumMultiplier":0,"enabled":true,"custom":false},
          {"id":"auto","displayName":"Auto","premiumMultiplier":1,"enabled":true,"custom":false},
          {"id":"retired","displayName":"Retired","premiumMultiplier":1,"enabled":false,"custom":false}
        ]
        """;

    /// <summary>USD 1 000 in and 100 out per move at 3 and 15 per 1M tokens: 0.0045 USD a move, with no stored history.</summary>
    public const string CostInputs =
        """{"usdPerAiCredit":0.01,"assumedInputTokensPerMove":1000,"assumedOutputTokensPerMove":100,"models":[]}""";

    public const string RunningId = "44444444-4444-4444-4444-444444444444";
    public const string FinishedId = "55555555-5555-5555-5555-555555555555";

    public const string RunningBenchmark =
        """{"id":"44444444-4444-4444-4444-444444444444","name":"Spring check","candidate":"fake-a@high","opponents":["fake-b","random"],"gamesPerOpponent":2,"status":"running","gamesTotal":4,"gamesFinished":1,"createdAt":"2026-10-25T10:00:00Z","finishedAt":null}""";

    public const string FinishedBenchmark =
        """{"id":"55555555-5555-5555-5555-555555555555","name":"Summer check","candidate":"gpt","opponents":["fake-a","fake-b"],"gamesPerOpponent":4,"status":"finished","gamesTotal":8,"gamesFinished":8,"createdAt":"2026-09-01T10:00:00Z","finishedAt":"2026-09-02T10:00:00Z"}""";

    public static string Benchmarks(params string[] items) => $"[{string.Join(',', items)}]";

    /// <summary>
    /// A finished report with an unbounded Elo for a sweep (+∞ with a finite lower bound), a 50 % score with null bounds, and the
    /// global fit. Its totals are the sums of the opponents' rows.
    /// </summary>
    public const string Report =
        """
        {"benchmark":{"id":"44444444-4444-4444-4444-444444444444","name":"Spring check","candidate":"fake-a@high","opponents":["fake-b","random"],"gamesPerOpponent":2,"status":"finished","gamesTotal":4,"gamesFinished":4,"createdAt":"2026-10-25T10:00:00Z","finishedAt":"2026-10-25T11:00:00Z"},
         "opponents":[
           {"opponent":"fake-b","games":2,"wins":2,"draws":0,"losses":0,"forfeitLosses":0,"score":1,"eloDiff":null,"eloDiffLow":120,"eloDiffHigh":null,"rqc":0.7,"rqcLow":0.6,"rqcHigh":0.8,"candidateFirstAttemptLegalRate":1,"candidateEstimatedUsd":0.36},
           {"opponent":"random","games":2,"wins":1,"draws":0,"losses":1,"forfeitLosses":0,"score":0.5,"eloDiff":0,"eloDiffLow":null,"eloDiffHigh":null,"rqc":0.5,"rqcLow":0.4,"rqcHigh":0.6,"candidateFirstAttemptLegalRate":0.75,"candidateEstimatedUsd":0}
         ],
         "summary":{"games":4,"score":0.75,"rating":1500,"ratingLow":1400,"ratingHigh":1600,"arenaScore":61.5,"provisional":true,"candidateTokens":12000,"candidateEstimatedUsd":0.36,"totalEstimatedUsd":0.36},
         "fingerprint":{"appVersion":"1.2.3","formulaVersion":"RATING-1","promptVersion":"abc123","generatedAt":"2026-10-25T11:00:00Z"}}
        """;

    public const string Markdown = "# Spring check\n\nCandidate fake-a@high.\n";
}

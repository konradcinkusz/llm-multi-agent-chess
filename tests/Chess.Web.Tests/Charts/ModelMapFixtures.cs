using Chess.Web.Components.Charts;

namespace Chess.Web.Tests.Charts;

/// <summary>Canned models for the model map. Every coefficient is set, so a test changes only the ones it is about.</summary>
internal static class ModelMapFixtures
{
    /// <summary>A model with a 95 % interval of ±50 on its rating and ±10 on its arena score.</summary>
    public static MapModel Model(
        string name,
        int games = 10,
        double? rating = 1500,
        double? cost = 0.01,
        double? legality = 0.9,
        double? latency = 1.5,
        double? tokens = 1000,
        double? forfeit = 0,
        double? arena = 50,
        double? moveQuality = null,
        bool provisional = false,
        int? slot = 0) =>
        new(name, games, provisional, slot,
            rating, rating - 50, rating + 50,
            arena, arena - 10, arena + 10,
            legality, moveQuality, latency, tokens, cost, forfeit);

    /// <summary>The model with no cost, as a free player or a model whose replies were never priced has.</summary>
    public static MapModel Unpriced(string name, int games = 10) => Model(name, games, cost: null);
}

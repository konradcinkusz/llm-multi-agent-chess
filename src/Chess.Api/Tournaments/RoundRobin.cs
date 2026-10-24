namespace Chess.Api.Tournaments;

/// <summary>One game of a round robin: which model plays white and which plays black.</summary>
public sealed record Pairing(string White, string Black);

/// <summary>
/// The games of a round robin (03 §5): every unordered pair of models meets <c>gamesPerPair</c> times, and the colours
/// alternate, so with an even <c>gamesPerPair</c> each pair plays half its games with each colour. Pure: no I/O.
/// </summary>
public static class RoundRobin
{
    public static IReadOnlyList<Pairing> Pairings(IReadOnlyList<string> models, int gamesPerPair)
    {
        var pairings = new List<Pairing>();
        for (var i = 0; i < models.Count; i++)
        {
            for (var j = i + 1; j < models.Count; j++)
            {
                for (var game = 0; game < gamesPerPair; game++)
                {
                    pairings.Add(game % 2 == 0 ? new Pairing(models[i], models[j]) : new Pairing(models[j], models[i]));
                }
            }
        }

        return pairings;
    }
}

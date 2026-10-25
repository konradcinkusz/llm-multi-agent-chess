using Chess.Api.Tournaments;

namespace Chess.Api.Benchmarks;

/// <summary>
/// The games of a benchmark (03 §12.2): the candidate plays <c>gamesPerOpponent</c> games against each opponent, in the order
/// of the opponents, and the colours alternate, so the candidate has half of them with White. Pure: no I/O.
/// </summary>
public static class GauntletSchedule
{
    public static IReadOnlyList<Pairing> Pairings(string candidate, IReadOnlyList<string> opponents, int gamesPerOpponent)
    {
        var pairings = new List<Pairing>(opponents.Count * gamesPerOpponent);
        foreach (var opponent in opponents)
        {
            for (var game = 0; game < gamesPerOpponent; game++)
            {
                pairings.Add(game % 2 == 0 ? new Pairing(candidate, opponent) : new Pairing(opponent, candidate));
            }
        }

        return pairings;
    }
}

using Chess.Web.Api;
using Chess.Web.Components.Pages.Shared;

namespace Chess.Web.Components.Pages.HeadToHead;

/// <summary>The verbal reading of an RQC, as RATING.md §5 defines it. Pure: no I/O, no clock.</summary>
public static class VerbalReading
{
    /// <summary>
    /// How strongly the coefficient favours its leader. Symmetric in the two models: the strength
    /// is read from the larger of RQC and 1 − RQC.
    /// </summary>
    public static string Strength(double rqc)
    {
        var lead = Math.Max(rqc, 1 - rqc);
        return lead switch
        {
            < 0.55 => "indistinguishable",
            < 0.65 => "slightly better",
            <= 0.80 => "clearly better",
            _ => "dominant",
        };
    }

    /// <summary>
    /// The reading of the pair. Not significant results say why: too few games for a provisional model, a point
    /// interval, or an interval that contains 0.5 (see <see cref="Significance"/>).
    /// </summary>
    public static string Sentence(PairwiseCoefficient pair, bool bothEstablished)
    {
        var strength = Strength(pair.Rqc);
        var sentence = strength switch
        {
            "indistinguishable" => $"{pair.A} and {pair.B} are indistinguishable.",
            "dominant" => $"{Leader(pair)} is dominant over {Trailer(pair)}.",
            _ => $"{Leader(pair)} is {strength} than {Trailer(pair)}.",
        };

        if (Significance.IsSignificant(pair, bothEstablished))
        {
            return sentence;
        }

        if (!bothEstablished)
        {
            return $"{sentence} {Significance.TooFewGames}";
        }

        return pair.RqcHigh - pair.RqcLow > Significance.Tolerance
            ? sentence + " Not significant: the 95 % interval contains 0.5."
            : sentence + " Not significant: the 95 % interval has no width.";
    }

    private static string Leader(PairwiseCoefficient pair) => pair.Rqc >= 0.5 ? pair.A : pair.B;

    private static string Trailer(PairwiseCoefficient pair) => pair.Rqc >= 0.5 ? pair.B : pair.A;
}

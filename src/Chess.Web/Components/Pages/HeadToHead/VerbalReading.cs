using Chess.Web.Api;

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

    /// <summary>Significant only when the 95 % interval excludes 0.5, whatever the point value.</summary>
    public static bool IsSignificant(double low, double high) => low > 0.5 || high < 0.5;

    public static string Sentence(PairwiseCoefficient pair)
    {
        var strength = Strength(pair.Rqc);
        var sentence = strength switch
        {
            "indistinguishable" => $"{pair.A} and {pair.B} are indistinguishable.",
            "dominant" => $"{Leader(pair)} is dominant over {Trailer(pair)}.",
            _ => $"{Leader(pair)} is {strength} than {Trailer(pair)}.",
        };

        return IsSignificant(pair.RqcLow, pair.RqcHigh)
            ? sentence
            : sentence + " Not significant: the 95 % interval contains 0.5.";
    }

    private static string Leader(PairwiseCoefficient pair) => pair.Rqc >= 0.5 ? pair.A : pair.B;

    private static string Trailer(PairwiseCoefficient pair) => pair.Rqc >= 0.5 ? pair.B : pair.A;
}

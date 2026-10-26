using System.Text;

namespace Chess.Web.Site;

/// <summary>
/// Where each page of the export lives, relative to the export root (03 §11). Every path ends in <c>index.html</c>, so
/// the links work from <c>file://</c> too. Pure: no I/O.
/// </summary>
public static class SitePaths
{
    public const string Results = "index.html";
    public const string HeadToHead = "head-to-head/index.html";
    public const string Games = "games/index.html";
    public const string Tournaments = "tournaments/index.html";
    public const string Menu = "menu/index.html";
    public const string About = "about/index.html";
    public const string Assets = "assets/";
    public const string Data = "data/";

    /// <summary>The first page of the game archive is <c>games/index.html</c>; the others are <c>games/page-n/index.html</c>.</summary>
    public static string GamesPage(int page) => page <= 1 ? Games : $"games/page-{page}/index.html";

    public static string Game(Guid id) => $"games/{id:D}/index.html";

    public static string Tournament(Guid id) => $"tournaments/{id:D}/index.html";

    public static string Pair(string a, string b) => $"head-to-head/{Slug(a)}--{Slug(b)}/index.html";

    /// <summary>
    /// A model id as a folder name: lower case letters, digits, dots and dashes; anything else becomes a dash. Two ids that
    /// map to the same folder are refused by <see cref="SitePair.Build"/>, so no page overwrites another.
    /// </summary>
    public static string Slug(string model)
    {
        var builder = new StringBuilder(model.Length);
        foreach (var ch in model.ToLowerInvariant())
        {
            builder.Append(ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-' ? ch : '-');
        }

        return builder.Length == 0 ? "-" : builder.ToString();
    }
}

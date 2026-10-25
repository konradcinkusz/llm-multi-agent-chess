namespace Chess.Web.Site.Publishing;

/// <summary>
/// Arena:Site publishing options (03 §11). The section is shared with the export options of the site, which bind the same
/// section; each class reads only its own properties.
/// </summary>
public sealed class SitePublishingOptions
{
    public const string Section = "Arena:Site";

    /// <summary>Where to push. Null: the origin of the repository the app runs from.</summary>
    public string? Remote { get; set; }

    /// <summary>The branch GitHub Pages serves.</summary>
    public string Branch { get; set; } = "gh-pages";

    /// <summary>The publisher's own clone. It is deleted and recreated on every publish; never point it at a folder you keep.</summary>
    public string? WorkDir { get; set; }

    /// <summary>The folder the export is written to. It is emptied before every export; never point it at a folder you keep.</summary>
    public string? ExportDirectory { get; set; }

    /// <summary>The public address. Null: https://&lt;owner&gt;.github.io/&lt;repo&gt;/ derived from a GitHub remote.</summary>
    public string? PublicUrl { get; set; }

    /// <summary>The git executable. Only tests change it.</summary>
    public string GitExecutable { get; set; } = "git";

    /// <summary>Publish when a game has finished since the last publish, every this many minutes. 0 = off.</summary>
    public int AutoPublishMinutes { get; set; }

    public string ResolvedWorkDir => Path.GetFullPath(WorkDir ?? Path.Combine(Path.GetTempPath(), "llm-chess-arena-gh-pages"));

    public string ResolvedExportDirectory => Path.GetFullPath(ExportDirectory ?? Path.Combine(Path.GetTempPath(), "llm-chess-arena-site"));
}

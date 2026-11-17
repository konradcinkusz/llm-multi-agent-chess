namespace Chess.Web.Site.Publishing;

/// <summary>
/// The rules for the two folders the site code owns, the export folder and the publisher's work folder. Both are emptied
/// or deleted by the app, so each is checked before anything is deleted: it must be a folder of its own, not the repository
/// the app runs from (or a folder above it, such as the home directory) and not nested in the other one.
/// </summary>
public static class SiteFolders
{
    /// <summary>The file that marks an export folder as made by the site export. Without it a folder with content is never emptied.</summary>
    public const string ExportMarker = ".arena-site-export";

    /// <summary>
    /// The folders the app runs from: the top level of the git repository it is in, when it is in one (<c>git rev-parse
    /// --show-toplevel</c>, not the content root, which is <c>src/Chess.Web</c>), and the content root itself.
    /// </summary>
    public static async Task<IReadOnlyList<string>> AppRootsAsync(string git, string contentRoot, CancellationToken ct)
    {
        var roots = new List<string> { Path.GetFullPath(contentRoot) };
        var top = await GitProcess.RunAsync(git, contentRoot, ct, "rev-parse", "--show-toplevel");
        if (top.Ok && top.Stdout.Length > 0)
        {
            roots.Add(Path.GetFullPath(top.Stdout));
        }

        return roots;
    }

    /// <summary>The reason the export folder cannot be used, or null. <paramref name="workDir"/> is the publisher's folder.</summary>
    public static string? ExportProblem(string exportDir, string workDir, IReadOnlyList<string> appRoots)
    {
        if (appRoots.Any(root => IsInsideOrAbove(exportDir, root)))
        {
            return $"Arena:Site:ExportDirectory ({exportDir}) must be a folder of its own, not the repository the app runs from, a folder inside it or a folder above it.";
        }

        if (IsInsideOrAbove(exportDir, workDir))
        {
            return $"Arena:Site:ExportDirectory ({exportDir}) and Arena:Site:WorkDir ({workDir}) must be two separate folders, neither inside the other.";
        }

        return null;
    }

    /// <summary>True when the two paths are the same, or one is inside the other.</summary>
    public static bool IsInsideOrAbove(string a, string b)
    {
        var left = a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var right = b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return left.Equals(right, comparison)
            || right.StartsWith(left + Path.DirectorySeparatorChar, comparison)
            || left.StartsWith(right + Path.DirectorySeparatorChar, comparison);
    }
}

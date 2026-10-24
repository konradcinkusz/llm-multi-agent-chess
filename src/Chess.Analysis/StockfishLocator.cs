namespace Chess.Analysis;

/// <summary>Finds the Stockfish binary: the configured path, or else the first <c>stockfish</c> on PATH.</summary>
internal static class StockfishLocator
{
    /// <returns>The binary's full path, or null when there is none.</returns>
    public static string? Resolve(string? configuredPath, string? pathVariable)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            // An explicit path is never second-guessed by PATH.
            return File.Exists(configuredPath) ? Path.GetFullPath(configuredPath) : null;
        }

        var fileName = OperatingSystem.IsWindows() ? "stockfish.exe" : "stockfish";
        foreach (var directory in (pathVariable ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

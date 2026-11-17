using System.Text.Json;

namespace Chess.Api.Results.Archive;

public sealed record ArchiveRead(IReadOnlyList<GameRecord> Records, IReadOnlyList<string> Errors);

/// <summary>
/// Reads the archive folder: every <c>games/**/*.json</c>. A file must parse, sit at the path its content names, be valid by
/// <see cref="GameRecordValidator"/>, and hold a game id no other file holds. Every problem is reported with its file.
/// </summary>
public static class GameRecordFolder
{
    /// <summary>The largest file taken in: a 1000-ply game with every figure is about 150 KB.</summary>
    public const long MaxFileBytes = 256 * 1024;

    public static ArchiveRead Read(string root)
    {
        var records = new List<GameRecord>();
        var errors = new List<string>();
        var seen = new Dictionary<Guid, string>();
        var siteKeys = new Dictionary<string, (string Model, string File)>(StringComparer.Ordinal);
        var games = Path.Combine(root, "games");
        if (!Directory.Exists(games)) return new ArchiveRead(records, errors);

        foreach (var file in Directory.EnumerateFiles(games, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(file).StartsWith('.')) continue; // .gitkeep and the like

            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            var record = ReadFile(file, relative, errors);
            if (record is null) continue;

            if (seen.TryGetValue(record.Id, out var other))
            {
                errors.Add($"{relative}: game {record.Id} is already in {other}");
                continue;
            }

            if (!CheckPages(record, relative, siteKeys, errors)) continue;

            seen[record.Id] = relative;
            records.Add(record);
        }

        return new ArchiveRead(records, errors);
    }

    /// <summary>
    /// Two model ids that differ only by case or by characters the site turns into dashes would get one page on the public
    /// site, and the build of the site would fail after the merge. They are refused here instead, at the file that adds the second.
    /// </summary>
    private static bool CheckPages(GameRecord record, string file, Dictionary<string, (string Model, string File)> siteKeys, List<string> errors)
    {
        var ok = true;
        foreach (var model in new[] { record.White.Model, record.Black.Model })
        {
            var key = GameRecordValidator.SiteKey(model);
            if (!siteKeys.TryGetValue(key, out var first))
            {
                siteKeys[key] = (model, file);
            }
            else if (!string.Equals(first.Model, model, StringComparison.Ordinal))
            {
                errors.Add($"{file}: model '{model}' and model '{first.Model}' (in {first.File}) would share one page on the public site; use one spelling");
                ok = false;
            }
        }

        return ok;
    }

    private static GameRecord? ReadFile(string file, string relative, List<string> errors)
    {
        if (!relative.EndsWith(".json", StringComparison.Ordinal))
        {
            errors.Add($"{relative}: only .json game files belong in games/");
            return null;
        }

        if (new FileInfo(file).Length > MaxFileBytes)
        {
            errors.Add($"{relative}: the file is larger than {MaxFileBytes / 1024} KB");
            return null;
        }

        GameRecord record;
        try
        {
            record = GameRecordJson.Deserialize(File.ReadAllText(file));
        }
        catch (JsonException ex)
        {
            errors.Add($"{relative}: not a game file: {ex.Message}");
            return null;
        }

        var problems = GameRecordValidator.Validate(record);
        foreach (var problem in problems) errors.Add($"{relative}: {problem}");
        if (problems.Count == 0 && GameRecordJson.RelativePath(record) is var expected && expected != relative)
        {
            errors.Add($"{relative}: the file belongs at {expected}");
            return null;
        }

        return problems.Count == 0 ? record : null;
    }
}

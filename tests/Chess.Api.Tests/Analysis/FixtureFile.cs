using System.Runtime.CompilerServices;

namespace Chess.Api.Tests.Analysis;

/// <summary>Reads recorded engine output from <c>Fixtures/</c>, next to this file (no csproj change needed).</summary>
internal static class FixtureFile
{
    public static IReadOnlyList<string> Lines(string name, [CallerFilePath] string? source = null)
    {
        var path = Path.Combine(Path.GetDirectoryName(source)!, "Fixtures", name);
        return File.ReadAllLines(path);
    }
}

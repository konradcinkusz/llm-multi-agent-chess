namespace Chess.Web.Tests.Charts;

/// <summary>Locates the chart source files, for tests that check the stylesheet and the colour rules on disk.</summary>
internal static class ChartSources
{
    public static string ChartsPath { get; } = FindChartsPath();

    private static string FindChartsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Chess.sln")))
            dir = dir.Parent;
        return dir is null
            ? throw new DirectoryNotFoundException("Chess.sln was not found above the test binaries.")
            : Path.Combine(dir.FullName, "src", "Chess.Web", "Components", "Charts");
    }
}

using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Results;
using Chess.Web.Components.Site;
using Chess.Web.Site;
using Xunit;

namespace Chess.Web.Tests.Results;

/// <summary>The static Results page shows "Prompt styles compared" the way the live page does, and carries no script (03 §6.1, §14.5).</summary>
public class SiteResultsPageTests
{
    private static readonly SiteFrame Frame = new(0, new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero), 90, null, []);

    private static readonly string[] Models = ["gpt@high~think", "gpt@high", "claude"];

    [Fact]
    public void TheSnapshotShowsThePromptStyleSectionAndNoScript()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, Styled());

        var section = cut.Find("section[data-section='prompt-styles']");
        Assert.Equal("Prompt styles compared", section.QuerySelector("h2")!.TextContent);
        Assert.Equal(2, section.QuerySelectorAll("tbody[data-configuration='gpt@high'] tr").Length);
        Assert.Empty(cut.FindAll("script"));
        Assert.DoesNotContain("blazor", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSectionIsLastAndHiddenWhenEveryPlayerIsClassic()
    {
        using var ctx = new BunitContext();
        var board = new LeaderboardDto(
        [
            new ModelRating("gpt@high", 120, 100, 140, 30, 0.6, 0.97, null, 0.6, 60, false),
            new ModelRating("claude", 90, 70, 110, 30, 0.3, 0.95, null, 0.3, 30, false),
        ], 12, 60, false);
        var cut = Render(ctx, new ResultsInputs(board, board.Models.Select(m => m.Model).ToList(), Matrix(), [], null,
            new Dictionary<string, IReadOnlyList<RatingHistoryPointDto>>(StringComparer.Ordinal)));

        Assert.Empty(cut.FindAll("section[data-section='prompt-styles']"));
        Assert.Equal("table", cut.FindAll("section[data-section]").Last().GetAttribute("data-section"));
    }

    private static ResultsInputs Styled()
    {
        var board = new LeaderboardDto(
        [
            new ModelRating("gpt@high~think", 130, 110, 150, 30, 0.7, 0.99, null, 0.7, 66, false),
            new ModelRating("gpt@high", 120, 100, 140, 30, 0.6, 0.97, null, 0.6, 60, false),
            new ModelRating("claude", 90, 70, 110, 30, 0.3, 0.95, null, 0.3, 30, false),
        ], 12, 90, false);
        return new ResultsInputs(board, Models, Matrix(), [], null,
            new Dictionary<string, IReadOnlyList<RatingHistoryPointDto>>(StringComparer.Ordinal));
    }

    private static MatrixDto Matrix() => new(Models,
    [
        new PairwiseCoefficient("gpt@high~think", "gpt@high", 0.61, 0.5, 0.61, 0.55, 0.70, 70, 5, 0.6),
        new PairwiseCoefficient("gpt@high", "gpt@high~think", 0.39, 0.5, 0.39, 0.30, 0.45, -70, 5, 0.4),
        new PairwiseCoefficient("gpt@high~think", "claude", 0.80, 0.5, 0.80, 0.70, 0.90, 240, 5, 0.8),
        new PairwiseCoefficient("claude", "gpt@high~think", 0.20, 0.5, 0.20, 0.10, 0.30, -240, 5, 0.2),
        new PairwiseCoefficient("gpt@high", "claude", 0.70, 0.5, 0.70, 0.60, 0.80, 150, 5, 0.7),
        new PairwiseCoefficient("claude", "gpt@high", 0.30, 0.5, 0.30, 0.20, 0.40, -150, 5, 0.3),
    ]);

    private static IRenderedComponent<SiteResultsPage> Render(BunitContext ctx, ResultsInputs inputs)
    {
        var report = ResultsModel.Build(inputs);
        return ctx.Render<SiteResultsPage>(parameters => parameters
            .Add(p => p.Frame, Frame)
            .Add(p => p.Board, inputs.Board)
            .Add(p => p.Report, report));
    }
}

using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Chess.E2E;

/// <summary>Journey 3: the Results page opens on the Big Picture, with both players in the ranking.</summary>
[Collection(ArenaCollection.Name)]
public sealed class ResultsJourney(ArenaFixture arena)
{
    // 03 §6.1: the order of the sections on the page is fixed.
    private static readonly string[] SectionOrder =
    [
        "big-picture", "rating-history", "head-to-head", "breakdown", "move-quality", "strength-cost", "table",
    ];

    [Fact]
    public async Task BigPictureLeadsAndNamesBothPlayers()
    {
        await arena.PlayRatedGameAsync(Players.Random, Players.FakeB);

        await using var context = await arena.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/results");

        // The ranking covers every rated game in the database, so the leader is whoever the table ranks first.
        var leaderboard = page.GetByRole(AriaRole.Region, new() { Name = "Leaderboard table", Exact = true });
        var leader = leaderboard.GetByRole(AriaRole.Row).Nth(1).GetByRole(AriaRole.Cell).Nth(1);
        await Expect(leader).ToHaveTextAsync(new Regex(".+"), new() { Timeout = 60_000 });
        var leaderName = (await leader.TextContentAsync())!.Trim();

        var bigPicture = page.GetByRole(AriaRole.Region, new() { Name = "Big picture", Exact = true });
        var headline = bigPicture.GetByRole(AriaRole.Paragraph).Filter(new() { HasText = "Best overall:" });
        await Expect(headline).ToHaveTextAsync(new Regex($@"Best overall: {Regex.Escape(leaderName)}\b"));
        await Expect(bigPicture.GetByRole(AriaRole.Img, new() { Name = "Arena score by model", Exact = true })).ToBeVisibleAsync();

        // The Big Picture is the first section on the page, and the sections follow in the fixed order.
        await Expect(page.Locator("section").First).ToHaveAttributeAsync("data-section", "big-picture");
        var sections = await page.Locator("section[data-section]")
            .EvaluateAllAsync<string[]>("sections => sections.map(section => section.dataset.section)");
        Assert.Equal(SectionOrder, sections);

        await Expect(leaderboard.GetByRole(AriaRole.Cell, new() { Name = Players.Random, Exact = true })).ToBeVisibleAsync();
        await Expect(leaderboard.GetByRole(AriaRole.Cell, new() { Name = Players.FakeB, Exact = true })).ToBeVisibleAsync();
    }
}

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
        "big-picture", "model-map", "rating-history", "head-to-head", "breakdown", "move-quality", "strength-cost", "table",
    ];

    // The page loads its data in its first render, and the interactive circuit that starts after the prerendered page
    // runs that load again: until it finishes the page shows "Loading results…" and no sections. Every assertion on
    // the loaded page waits this long, which is what a loaded page needs on a busy machine.
    private const float LoadTimeout = 60_000;

    [Fact]
    public async Task BigPictureLeadsAndNamesBothPlayers()
    {
        await arena.PlayRatedGameAsync(Players.Random, Players.FakeB);

        await using var context = await arena.NewContextAsync();
        var page = await context.NewPageAsync();

        // The server prerenders the page after its data loads, so the load event waits on the same work.
        await page.GotoAsync("/results", new() { Timeout = LoadTimeout });

        // The ranking covers every rated game in the database, so the leader is whoever the table ranks first.
        var leaderboard = page.GetByRole(AriaRole.Region, new() { Name = "Leaderboard table", Exact = true });
        var leader = leaderboard.GetByRole(AriaRole.Row).Nth(1).GetByRole(AriaRole.Cell).Nth(1);
        await Expect(leader).ToHaveTextAsync(new Regex(".+"), new() { Timeout = LoadTimeout });
        var leaderName = (await leader.TextContentAsync())!.Trim();

        var bigPicture = page.GetByRole(AriaRole.Region, new() { Name = "Big picture", Exact = true });
        var headline = bigPicture.GetByRole(AriaRole.Paragraph).Filter(new() { HasText = "Best overall:" });
        await Expect(headline).ToHaveTextAsync(
            new Regex($@"Best overall: {Regex.Escape(leaderName)}\b"), new() { Timeout = LoadTimeout });
        await Expect(bigPicture.GetByRole(AriaRole.Img, new() { Name = "Arena score by model", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = LoadTimeout });

        // The Big Picture is the first section on the page, and the sections follow in the fixed order. These are
        // web-first assertions: a reload can replace the sections between two reads, so a one-shot read of them can
        // see none. Each position is retried until the loaded page shows it.
        await Expect(page.Locator("section").First)
            .ToHaveAttributeAsync("data-section", "big-picture", new() { Timeout = LoadTimeout });
        var sections = page.Locator("section[data-section]");

        // The table is the last fixed section, so once it is on the page the sections are loaded.
        await Expect(sections.Nth(SectionOrder.Length - 1))
            .ToHaveAttributeAsync("data-section", "table", new() { Timeout = LoadTimeout });

        // "Prompt styles compared" (03 §6.1, item 8) appears only when a player is styled, and then it comes last.
        var optional = await page.Locator("section[data-section='prompt-styles']").CountAsync() > 0 ? 1 : 0;
        await Expect(sections).ToHaveCountAsync(SectionOrder.Length + optional, new() { Timeout = LoadTimeout });
        for (var index = 0; index < SectionOrder.Length; index++)
        {
            await Expect(sections.Nth(index))
                .ToHaveAttributeAsync("data-section", SectionOrder[index], new() { Timeout = LoadTimeout });
        }

        await Expect(leaderboard.GetByRole(AriaRole.Cell, new() { Name = Players.Random, Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = LoadTimeout });
        await Expect(leaderboard.GetByRole(AriaRole.Cell, new() { Name = Players.FakeB, Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = LoadTimeout });

        // A styled player from another journey can put "Prompt styles compared" on the page. It is the last section then.
        if (optional == 1)
        {
            await Expect(sections.Last).ToHaveAttributeAsync("data-section", "prompt-styles", new() { Timeout = LoadTimeout });
        }
    }
}

using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Chess.E2E;

/// <summary>Journey 2: open a finished game on its replay page, step to the last move, and read its engine log and summary.</summary>
[Collection(ArenaCollection.Name)]
public sealed class ReplayJourney(ArenaFixture arena)
{
    [Fact]
    public async Task ReplayStepsToTheLastMove()
    {
        var gameId = await arena.PlayRatedGameAsync(Players.Random, Players.FakeB);

        await using var context = await arena.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"/games/{gameId}");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = $"{Players.Random} vs {Players.FakeB}", Exact = true }))
            .ToBeVisibleAsync();
        await Expect(page.GetByTestId("replay-summary")).ToContainTextAsync(new Regex(@"result (1-0|0-1|1/2-1/2)"));

        // The replay starts at ply 0 of a game that has moves.
        var ply = page.GetByTestId("replay-ply");
        await Expect(ply).ToHaveTextAsync(new Regex(@"^Ply 0 of [1-9]\d*$"));

        // At the last move both counters match, and the controls that step forward are disabled.
        await Interaction.RepeatUntilAsync(
            () => page.GetByTestId("replay-last").ClickAsync(),
            () => Expect(ply).ToHaveTextAsync(new Regex(@"^Ply ([1-9]\d*) of \1$"), new() { Timeout = 2_000 }));
        await Expect(page.GetByTestId("replay-last")).ToBeDisabledAsync();
        await Expect(page.GetByTestId("replay-next")).ToBeDisabledAsync();

        // The engine log lists the attempts the models made, and the summary shows what they cost.
        await Expect(page.GetByTestId("engine-panel")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("engine-entry").First).ToBeVisibleAsync();
        await Expect(page.GetByTestId("engine-entry").First.Locator(".engine-model")).ToHaveTextAsync("Fake B");
        await Expect(page.GetByTestId("game-summary")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("usage-table")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("usage-total-usd")).ToHaveTextAsync(new Regex(@"^\$\d"));
    }
}

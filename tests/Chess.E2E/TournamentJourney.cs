using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Chess.E2E;

/// <summary>Journey 4: create a small round robin on the Tournaments page and watch it finish.</summary>
[Collection(ArenaCollection.Name)]
public sealed class TournamentJourney(ArenaFixture arena)
{
    [Fact]
    public async Task RoundRobinRunsToFinished()
    {
        // A name no other journey uses, so the list and the detail page identify this tournament alone.
        var name = $"E2E round robin {Guid.NewGuid():N}";

        await using var context = await arena.NewContextAsync();
        var page = await context.NewPageAsync();
        var circuit = new CircuitWatch(page);

        await page.GotoAsync("/tournaments");
        await circuit.WaitAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "New round robin", Exact = true })).ToBeVisibleAsync();

        var start = page.GetByRole(AriaRole.Button, new() { Name = "Start tournament", Exact = true });
        await Interaction.RepeatUntilAsync(
            async () =>
            {
                await page.GetByLabel("Name", new() { Exact = true }).FillAsync(name);
                await page.GetByRole(AriaRole.Checkbox, new() { Name = "Fake A" }).CheckAsync();
                await page.GetByRole(AriaRole.Checkbox, new() { Name = "Fake B" }).CheckAsync();
                await page.GetByRole(AriaRole.Checkbox, new() { Name = "Think, then move" }).CheckAsync();
                await page.GetByLabel("Games per pair").SelectOptionAsync("2");
            },
            // Start stays disabled until the server-bound form has a name and two models.
            () => Expect(start).ToBeEnabledAsync(new() { Timeout = 2_000 }));

        await start.ClickAsync();

        // Two fake models in two styles are four players: six pairs, two games each, twelve games.
        await Expect(page.GetByText($"Started {name} with 12 games.")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Open the tournament" }).ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex(@"/tournaments/[0-9a-f]{8}-"));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = name, Exact = true })).ToBeVisibleAsync();

        await Expect(page.GetByRole(AriaRole.Definition).GetByText("Finished", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 180_000 });
        await Expect(page.GetByText("12 of 12 games")).ToBeVisibleAsync();

        var leaderboard = page.GetByRole(AriaRole.Region, new() { Name = "Leaderboard", Exact = true });
        await Expect(leaderboard.GetByRole(AriaRole.Cell, new() { Name = Players.FakeA, Exact = true })).ToBeVisibleAsync();
        await Expect(leaderboard.GetByRole(AriaRole.Cell, new() { Name = Players.FakeB, Exact = true })).ToBeVisibleAsync();
    }
}

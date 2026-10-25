using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Chess.E2E;

/// <summary>
/// Journey 1: start a random-vs-fake game on the live page and watch it finish. The fake game ends in well under a second,
/// so the turn indicator is checked by the bUnit tests (Play/PlayPageTests), not here.
/// </summary>
[Collection(ArenaCollection.Name)]
public sealed class LiveGameJourney(ArenaFixture arena)
{
    // The page shows "<PGN result> · <termination>", e.g. "1-0 · checkmate".
    private const string NoGame = "No game yet. Choose two players and start one.";

    private static readonly Regex FinishedResult = new(@"^(1-0|0-1|1/2-1/2) · \S");

    [Fact]
    public async Task RandomVsFakeGameRunsToAResult()
    {
        await using var context = await arena.NewContextAsync();
        var page = await context.NewPageAsync();
        var circuit = new CircuitWatch(page);

        await page.GotoAsync("/");
        await circuit.WaitAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Live game", Exact = true })).ToBeVisibleAsync();

        // The pickers are disabled until the circuit has bound the page, and the start button until the models are
        // loaded. Waiting for both means the selections below reach the server.
        await Expect(page.GetByTestId("white-model")).ToBeEnabledAsync();
        await Expect(page.GetByTestId("black-model")).ToBeEnabledAsync();
        await Expect(page.GetByTestId("start-game")).ToBeEnabledAsync();

        // Starting creates the game, so the input is repeated only while the page still shows no game.
        await Interaction.RepeatUntilAsync(
            async () =>
            {
                await page.GetByTestId("white-model").SelectOptionAsync(Players.Random);
                await page.GetByTestId("black-model").SelectOptionAsync(Players.FakeA);
                await page.GetByTestId("start-game").ClickAsync();
            },
            () => Expect(page.GetByTestId("game-status")).Not.ToHaveTextAsync(NoGame, new() { Timeout = 5_000 }));

        // The live page renders the players from the server's state once the game is running, so this also proves
        // the selection reached the server.
        await Expect(page.GetByTestId("white-model")).ToHaveValueAsync(Players.Random);
        await Expect(page.GetByTestId("black-model")).ToHaveValueAsync(Players.FakeA);

        await Expect(page.GetByTestId("game-result")).ToHaveTextAsync(FinishedResult, new() { Timeout = 120_000 });
        await Expect(page.GetByTestId("game-status")).ToHaveTextAsync("Game over.");
        await Expect(page.GetByTestId("move-list").GetByRole(AriaRole.Listitem).First).ToBeVisibleAsync();

        // Both players are named on their bars, and the fake model's attempt is one entry of the engine log.
        await Expect(page.GetByTestId("player-white").GetByTestId("player-name")).ToHaveTextAsync("Random");
        await Expect(page.GetByTestId("player-black").GetByTestId("player-name")).ToHaveTextAsync("Fake A");
        await Expect(page.GetByTestId("engine-entry").First).ToBeVisibleAsync();

        // The summary states the tokens and the estimated cost, with the rate the dollars were computed at.
        await Expect(page.GetByTestId("game-summary")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("usage-rate")).ToContainTextAsync("per AI credit");
        await Expect(page.GetByTestId("usage-total")).ToBeVisibleAsync();
    }
}

using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Chess.E2E;

/// <summary>Journey 5: set up a small benchmark on the Benchmark page, watch it run, and read its report and downloads.</summary>
[Collection(ArenaCollection.Name)]
public sealed class BenchmarkJourney(ArenaFixture arena)
{
    [Fact]
    public async Task TinyBenchmarkRunsToItsReport()
    {
        // A name no other journey uses, so the report identifies this benchmark alone.
        var id = Guid.NewGuid().ToString("N");
        var name = $"E2E benchmark {id}";

        await using var context = await arena.NewContextAsync();
        var page = await context.NewPageAsync();
        var circuit = new CircuitWatch(page);

        await page.GotoAsync("/benchmark");
        await circuit.WaitAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "New benchmark", Exact = true })).ToBeVisibleAsync();

        var start = page.GetByRole(AriaRole.Button, new() { Name = "Start benchmark", Exact = true });
        await Interaction.RepeatUntilAsync(
            async () =>
            {
                await page.GetByLabel("Name", new() { Exact = true }).FillAsync(name);
                await page.Locator("#candidate").SelectOptionAsync("fake-a");
                // Fake A lists three efforts, so the effort picker is there; the candidate is Fake A at high.
                await page.Locator("#effort").SelectOptionAsync("high");
                await page.GetByRole(AriaRole.Radio, new() { Name = "Choose opponents" }).CheckAsync();
                await page.GetByRole(AriaRole.Checkbox, new() { Name = "Fake B", Exact = true }).CheckAsync();
                await page.GetByRole(AriaRole.Checkbox, new() { Name = "Random", Exact = true }).CheckAsync();
                await page.Locator("#games-per-opponent").SelectOptionAsync("2");
            },
            // Start stays disabled until the form has a name, a candidate and an opponent.
            () => Expect(start).ToBeEnabledAsync(new() { Timeout = 2_000 }));

        await start.ClickAsync();

        // Two opponents, two games each: four games in all.
        await Expect(page.GetByText($"Started {name} with 4 games.")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Open the report" }).ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex(@"/benchmark/[0-9a-f]{8}-"));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = name, Exact = true })).ToBeVisibleAsync();

        await Expect(page.GetByText("Finished", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 180_000 });

        var opponents = page.GetByTestId("benchmark-opponents");
        await Expect(opponents.GetByRole(AriaRole.Rowheader, new() { Name = "fake-b", Exact = true })).ToBeVisibleAsync();
        await Expect(opponents.GetByRole(AriaRole.Rowheader, new() { Name = "random", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByTestId("benchmark-fingerprint")).ToContainTextAsync("Rating formula");

        // The downloads are links that carry the report: no request from the browser, the file is named after the benchmark.
        var markdown = page.GetByRole(AriaRole.Link, new() { Name = "Download Markdown", Exact = true });
        await Expect(markdown).ToHaveAttributeAsync("href", new Regex("^data:text/markdown;charset=utf-8,"));
        await Expect(markdown).ToHaveAttributeAsync("download", $"benchmark-e2e-benchmark-{id}.md");

        var json = page.GetByRole(AriaRole.Link, new() { Name = "Download JSON", Exact = true });
        await Expect(json).ToHaveAttributeAsync("href", new Regex("^data:application/json;charset=utf-8,"));
        await Expect(json).ToHaveAttributeAsync("download", $"benchmark-e2e-benchmark-{id}.json");
    }
}

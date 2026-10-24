using Microsoft.Playwright;
using Xunit;

namespace Chess.E2E;

// Harness smoke test only: the Playwright driver starts without a browser download.
// The real end-to-end scenarios (AppHost + fake gateway) arrive with T4.1.
public class PlaywrightHarnessTests
{
    [Fact]
    public async Task PlaywrightDriverStarts()
    {
        using var playwright = await Playwright.CreateAsync();

        Assert.NotNull(playwright.Chromium);
    }
}

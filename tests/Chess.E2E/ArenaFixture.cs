using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Xunit;

namespace Chess.E2E;

/// <summary>
/// Starts Chess.AppHost once for the whole collection, on the fake profile (fake LLM gateway, InMemory database),
/// and hands each journey its own browser context. Games the journeys need are created through the API as test
/// setup, so no journey depends on what another one left behind.
/// </summary>
public sealed class ArenaFixture : IAsyncLifetime
{
    /// <summary>Short games: the fake players reach this many plies and the game is adjudicated (RATING.md §2).</summary>
    private const int MaxPlies = 30;

    private static readonly TimeSpan GameTimeout = TimeSpan.FromMinutes(2);

    private DistributedApplication? _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private string? _previousMaxPlies;

    /// <summary>The Blazor web app, the only origin a browser talks to.</summary>
    public Uri WebUrl { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // The browser is launched before the AppHost starts, so a machine without Chromium fails at once.
        _playwright = await Playwright.CreateAsync();
        _browser = await LaunchChromiumAsync(_playwright);

        // The AppHost starts the API as a child process, which inherits this environment (Arena:MaxPlies).
        _previousMaxPlies = Environment.GetEnvironmentVariable("Arena__MaxPlies");
        Environment.SetEnvironmentVariable("Arena__MaxPlies", MaxPlies.ToString(CultureInfo.InvariantCulture));

        using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = startup.Token;

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Chess_AppHost>(["--ARENA_FAKE=1"], ct);
        _app = await builder.BuildAsync(ct);
        await _app.StartAsync(ct);

        await _app.ResourceNotifications.WaitForResourceHealthyAsync("api", ct);
        await _app.ResourceNotifications.WaitForResourceAsync("web", KnownResourceStates.Running, ct);
        WebUrl = _app.GetEndpoint("web");
        await WaitUntilServingAsync(WebUrl, ct);

        Assertions.SetDefaultExpectTimeout(15_000);
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        Environment.SetEnvironmentVariable("Arena__MaxPlies", _previousMaxPlies);
    }

    /// <summary>A browser context with no state from earlier journeys, rooted at the web app.</summary>
    public Task<IBrowserContext> NewContextAsync() =>
        _browser!.NewContextAsync(new BrowserNewContextOptions { BaseURL = WebUrl.AbsoluteUri });

    /// <summary>
    /// Plays a rated game through the API and returns once the server reports it finished. Test setup only: the
    /// journeys assert on the UI that shows the game.
    /// </summary>
    public async Task<Guid> PlayRatedGameAsync(string white, string black)
    {
        using var client = _app!.CreateHttpClient("api");
        using var created = await client.PostAsJsonAsync(
            "/api/games",
            new { white = Player(white), black = Player(black), rated = true },
            CancellationToken.None);
        created.EnsureSuccessStatusCode();

        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var deadline = DateTimeOffset.UtcNow + GameTimeout;
        while (true)
        {
            var detail = await client.GetFromJsonAsync<JsonElement>($"/api/games/{id}");
            if (detail.GetProperty("summary").GetProperty("state").GetString() == "finished")
            {
                return id;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException($"Game {id} did not finish within {GameTimeout}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }

    private static object Player(string modelId) =>
        modelId == Players.Random ? new { kind = "random", modelId } : new { kind = "llm", modelId };

    /// <summary>
    /// The Chromium named by E2E_CHROMIUM_PATH when it is set (a build already on the machine, e.g. a cloud session whose
    /// Playwright cache holds another revision), otherwise the Playwright cache. The journeys never download a browser:
    /// when neither holds one, the run fails here and says what to run.
    /// </summary>
    private static async Task<IBrowser> LaunchChromiumAsync(IPlaywright playwright)
    {
        var executablePath = Environment.GetEnvironmentVariable("E2E_CHROMIUM_PATH");
        var options = new BrowserTypeLaunchOptions
        {
            ExecutablePath = string.IsNullOrEmpty(executablePath) ? null : executablePath,
        };

        try
        {
            return await playwright.Chromium.LaunchAsync(options);
        }
        catch (PlaywrightException ex)
        {
            throw new InvalidOperationException(
                "Chromium could not be launched for the browser journeys. Either install the Playwright build once with "
                + "`pwsh tests/Chess.E2E/bin/Debug/net10.0/playwright.ps1 install chromium`, or point E2E_CHROMIUM_PATH at an "
                + "existing Chromium and run: E2E_CHROMIUM_PATH=<path to chromium> dotnet test tests/Chess.E2E",
                ex);
        }
    }

    /// <summary>The AppHost reports a resource running before its HTTP listener accepts requests; wait for the health endpoint.</summary>
    private static async Task WaitUntilServingAsync(Uri baseUrl, CancellationToken ct)
    {
        using var http = new HttpClient();
        while (true)
        {
            try
            {
                using var response = await http.GetAsync(new Uri(baseUrl, "/health"), ct);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet; retry until the startup token expires.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }
    }
}

/// <summary>The model ids the fake profile offers (Chess.Llm FakeLlmGateway), plus the Random player.</summary>
public static class Players
{
    public const string Random = "random";
    public const string FakeA = "fake-a";
    public const string FakeB = "fake-b";
}

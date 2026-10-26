using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chess.Api.Requests;
using Chess.Api.Requests.YouTube;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chess.Api.Tests.Requests.YouTube;

/// <summary>Reads a recorded JSON fixture from the Fixtures folder next to these tests (no copy step: the file is read from the source tree).</summary>
internal static class YouTubeFixtures
{
    public static string Read(string name, [CallerFilePath] string here = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", name));
}

/// <summary>One request the scripted handler saw: its method, full URL and bearer token (null for the token endpoint).</summary>
internal sealed record RecordedRequest(HttpMethod Method, string Url, string? Authorization);

/// <summary>
/// Answers each request with the next scripted response, in order, and records every request. A request with no response
/// left fails the test, so an unexpected call cannot pass silently.
/// </summary>
internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public void Respond(HttpStatusCode status, string fixture) => _responses.Enqueue((status, YouTubeFixtures.Read(fixture)));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString()));
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No scripted response left for {request.Method} {request.RequestUri}");
        }

        var (status, body) = _responses.Dequeue();
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class MovableClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Records the events the poller hands over, and can fail a number of calls first, as a busy database would.</summary>
internal sealed class RecordingIntake : IChatIntake
{
    public List<ChatEvent> Events { get; } = [];

    public int FailuresLeft { get; set; }

    public Task<IntakeResult> AcceptAsync(ChatEvent chatEvent, CancellationToken ct)
    {
        if (FailuresLeft > 0)
        {
            FailuresLeft--;
            throw new InvalidOperationException("the database is busy");
        }

        Events.Add(chatEvent);
        return Task.FromResult(new IntakeResult(Duplicate: false, DepositUsd: null, Request: null));
    }
}

/// <summary>A poller wired to a scripted HTTP handler, a movable clock and a recording intake.</summary>
internal sealed class YouTubeHarness
{
    public ScriptedHandler Http { get; } = new();

    public MovableClock Clock { get; } = new(DateTimeOffset.Parse("2026-10-26T10:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture));

    public RecordingIntake Intake { get; } = new();

    public YouTubeIntakeOptions Options { get; } = new()
    {
        ClientId = "client-id-fixture",
        ClientSecret = "client-secret-fixture",
        RefreshToken = "refresh-token-fixture",
        ApiBaseUrl = "https://youtube.test/youtube/v3",
        TokenUrl = "https://oauth.test/token",
    };

    public YouTubeChatPoller Poller { get; }

    public YouTubeHarness()
    {
        var http = new HttpClient(Http, disposeHandler: false);
        var scopes = new ServiceCollection().AddSingleton<IChatIntake>(Intake).BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        Poller = new YouTubeChatPoller(
            new YouTubeChatClient(http, Options),
            new GoogleTokenProvider(http, Options, Clock),
            scopes,
            Options,
            Clock,
            NullLogger<YouTubeChatPoller>.Instance);
    }

    /// <summary>The first response of a run that finds the active broadcast: the token, then the broadcast lookup.</summary>
    public void RespondWithActiveBroadcast()
    {
        Http.Respond(HttpStatusCode.OK, "oauth-token.json");
        Http.Respond(HttpStatusCode.OK, "broadcasts-active.json");
    }
}

/// <summary>The JSON options the test fixtures are read with, matching the intake's wire options.</summary>
internal static class FixtureJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}

using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Chess.E2E;

/// <summary>
/// Journey 6 (03 §15, mock intake): a $1 Super Chat is simulated on the admin page and queued. The broadcast runner takes
/// requests only while a schedule is running or paused, so the journey then starts a short schedule through the API (its
/// slots begin an hour away, so the request is the first game), and the stream names the payer in its status line while
/// the requested game is on the board.
/// </summary>
[Collection(ArenaCollection.Name)]
public sealed class RequestJourney(ArenaFixture arena)
{
    private const string Payer = "Ana E2E";

    [Fact]
    public async Task ASuperChatIsQueuedAndTheStreamShowsThePayer()
    {
        await using var context = await arena.NewContextAsync();
        var admin = await context.NewPageAsync();
        var circuit = new CircuitWatch(admin);

        await admin.GotoAsync("/admin/requests");
        // The form takes input only once the circuit is up (see CircuitWatch).
        await circuit.WaitAsync();
        var form = admin.GetByTestId("requests-simulate");
        await Expect(form).ToBeVisibleAsync();
        await form.GetByLabel("Payer id").FillAsync("viewer-e2e");
        await form.GetByLabel("Display name").FillAsync(Payer);
        await form.GetByLabel("Amount").FillAsync("1");
        await form.GetByLabel("Currency").FillAsync("USD");
        await form.GetByLabel("Message").FillAsync("!g fa vs fb");
        await form.GetByRole(AriaRole.Button, new() { Name = "Simulate" }).ClickAsync();

        await Expect(admin.GetByTestId("requests-message")).ToContainTextAsync("Sent to the intake");
        await Expect(admin.GetByTestId("requests-queued")).ToContainTextAsync(Payer);

        // A queued request stays queued while no schedule is active; starting one makes the runner play it.
        var scheduleId = await arena.StartBroadcastAsync();
        try
        {
            var stream = await context.NewPageAsync();
            await stream.GotoAsync("/broadcast");
            // The request panels are gone from the stream; the status line names the payer while their game is on air.
            await Expect(stream.GetByTestId("broadcast-status")).ToContainTextAsync(Payer);
        }
        finally
        {
            await arena.StopBroadcastAsync(scheduleId);
        }
    }
}

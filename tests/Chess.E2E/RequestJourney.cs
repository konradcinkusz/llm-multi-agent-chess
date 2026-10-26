using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Chess.E2E;

/// <summary>
/// Journey 6 (03 §15, mock intake): a $1 Super Chat is simulated on the admin page, queued, and the stream shows the
/// payer in its queue panel. The "playing" step waits for V3, when the broadcast runner takes requests.
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

        var stream = await context.NewPageAsync();
        await stream.GotoAsync("/broadcast");
        var queue = stream.GetByTestId("broadcast-queue");
        await Expect(queue).ToContainTextAsync(Payer);

        // The stream shows the parsed configuration and the payer's name, never the typed text.
        await Expect(queue).Not.ToContainTextAsync("!g");
        await Expect(stream.GetByTestId("broadcast-menu").Locator(".bc-code").First).ToHaveTextAsync("fa");
    }
}

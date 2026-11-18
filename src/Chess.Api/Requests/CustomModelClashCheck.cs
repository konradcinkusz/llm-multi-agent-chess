using Chess.Api.Catalog;
using Chess.Llm;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chess.Api.Requests;

/// <summary>
/// Stops the host when a menu model is a configured custom model whose id a Copilot model also uses, with
/// <c>Arena:Requests:RequireApiKeyModels</c> on. The gateway drops such a custom model and the id resolves to the Copilot model,
/// so a paid game would run on the subscription (03 §15.2). The Copilot model ids are known only once the runtime has listed
/// them, and the runtime starts in the background (it does not hold up the host), so this check cannot run at start-up and cannot
/// be an options validator. It is a background service that asks the catalog until the catalog is ready, then checks once: a
/// clash throws out of <see cref="ExecuteAsync"/>, which stops the host with the message. With the fake gateway the catalog is
/// ready at once, so the host stops during start-up. When the catalog does not become ready within <see cref="GiveUpAfter"/>
/// (no Copilot login, no runtime) the check ends with a warning: no Copilot model is known to clash, and the per-request
/// guards (<c>RequestMenu</c> quotes no such model, <c>BroadcastRequests</c> refuses and credits it) still hold.
/// </summary>
public sealed class CustomModelClashCheck(
    ModelCatalog catalog,
    IOptions<RequestOptions> requests,
    IOptions<ArenaOptions> arena,
    ILogger<CustomModelClashCheck>? logger = null) : BackgroundService
{
    /// <summary>How often the catalog is asked while it is not ready.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long the check waits for the catalog before it gives up.</summary>
    public TimeSpan GiveUpAfter { get; init; } = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (requests.Value.Mode == RequestMode.Off || !requests.Value.RequireApiKeyModels)
        {
            return;
        }

        var waited = TimeSpan.Zero;
        try
        {
            while (true)
            {
                var listing = await catalog.GetModelsAsync(stoppingToken);
                if (listing.Warning is null)
                {
                    var clashes = Find(requests.Value, arena.Value, listing);
                    if (clashes.Count > 0)
                    {
                        throw new InvalidOperationException(Message(clashes));
                    }

                    return;
                }

                if (waited >= GiveUpAfter)
                {
                    logger?.LogWarning(
                        "The custom-model clash check could not run: {Warning}. A custom model that clashes with a Copilot model is refused when it is quoted or played",
                        listing.Warning);
                    return;
                }

                await Task.Delay(PollInterval, stoppingToken);
                waited += PollInterval;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping before the catalog was ready.
        }
    }

    /// <summary>The menu models that are configured under <c>Arena:CustomModels</c> but listed as a model that is not custom.</summary>
    public static IReadOnlyList<string> Find(RequestOptions options, ArenaOptions arena, ModelListing listing)
    {
        var configured = arena.CustomModels.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        return options.Menu
            .Select(entry => ModelVariant.BaseOf(entry.PlayerId))
            .Where(model => configured.Contains(model) && listing.Models.Any(m => m.Id == model && !m.Custom))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static string Message(IReadOnlyList<string> clashes) =>
        $"Arena:CustomModels id(s) {string.Join(", ", clashes.Select(id => $"'{id}'"))} clash with a GitHub Copilot model of the same id, "
        + "so the model would run on the Copilot subscription, not on your own key. With Arena:Requests:RequireApiKeyModels on, "
        + "rename the custom model (Arena:CustomModels:<n>:Id) and its Arena:Requests:Menu entry.";
}

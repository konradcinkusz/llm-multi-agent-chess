using Chess.Api.Catalog;
using Chess.Llm;
using Microsoft.Extensions.Options;

namespace Chess.Api.Requests;

/// <summary>
/// Stops start-up when a menu model is a configured custom model whose id a Copilot model also uses, with
/// <c>Arena:Requests:RequireApiKeyModels</c> on. The gateway drops such a custom model and the id resolves to the Copilot model,
/// so a paid game would run on the subscription (03 §15.2). The Copilot model ids are known only once the runtime has listed
/// them, so the check runs after the gateway has started (it is registered after it) and cannot be an options validator.
/// When the gateway is not ready the listing holds no Copilot models and the check passes; the broadcast runner then refuses
/// the request at play time (<c>BroadcastRequests</c>).
/// </summary>
public sealed class CustomModelClashCheck(ModelCatalog catalog, IOptions<RequestOptions> requests, IOptions<ArenaOptions> arena)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (requests.Value.Mode == RequestMode.Off || !requests.Value.RequireApiKeyModels)
        {
            return;
        }

        var clashes = Find(requests.Value, arena.Value, await catalog.GetModelsAsync(cancellationToken));
        if (clashes.Count > 0)
        {
            throw new InvalidOperationException(Message(clashes));
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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

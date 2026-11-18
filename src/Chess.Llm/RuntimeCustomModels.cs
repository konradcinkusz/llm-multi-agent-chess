namespace Chess.Llm;

/// <summary>
/// Custom models that are added while the app runs, next to those of <c>Arena:CustomModels</c>: today the models an Anthropic
/// API key gives access to. The API replaces the list when the key is saved, refreshed or removed; the gateways read it on
/// every listing and every new conversation. A model here carries its key in memory only, and is never written anywhere.
/// </summary>
public sealed class RuntimeCustomModels
{
    private volatile IReadOnlyList<CustomModelOptions> _models = [];

    public IReadOnlyList<CustomModelOptions> Current => _models;

    public void Replace(IReadOnlyList<CustomModelOptions> models) => _models = models;
}

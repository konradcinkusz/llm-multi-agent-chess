using Chess.Api.Usage;

namespace Chess.Api.Catalog;

/// <summary>
/// Bound from the <c>Catalog</c> section, and from <c>Arena</c> for the two settings that shape the model lists:
/// <c>Arena:ShowAutoModel</c> and <c>Arena:Cost</c> (03 §10.2).
/// </summary>
public sealed class CatalogOptions
{
    /// <summary>How long the gateway's model list stays in memory before it is asked again.</summary>
    public TimeSpan ModelCacheTtl { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Copilot's Auto model picks a different model for each request, so it is not one player. It is hidden unless this is true.
    /// </summary>
    public bool ShowAutoModel { get; set; }

    /// <summary>The dollar value of an AI credit, which turns the models' prices into dollars.</summary>
    public CostOptions Cost { get; set; } = new();
}

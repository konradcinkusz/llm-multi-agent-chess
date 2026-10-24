namespace Chess.Api.Catalog;

/// <summary>Bound from the <c>Catalog</c> configuration section.</summary>
public sealed class CatalogOptions
{
    /// <summary>How long the gateway's model list stays in memory before it is asked again.</summary>
    public TimeSpan ModelCacheTtl { get; set; } = TimeSpan.FromSeconds(60);
}

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Chess.Web.Site;

/// <summary>
/// Renders static components to HTML with Blazor's <see cref="HtmlRenderer"/>. One renderer serves a whole export, and
/// every call runs on its dispatcher, as the renderer requires.
/// </summary>
internal sealed class SiteRenderer(IServiceProvider services, ILoggerFactory loggerFactory) : IAsyncDisposable
{
    private readonly HtmlRenderer _renderer = new(services, loggerFactory);

    public Task<string> RenderAsync<TComponent>(params (string Name, object? Value)[] parameters) where TComponent : IComponent
    {
        var values = parameters.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        return _renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await _renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(values));
            return output.ToHtmlString();
        });
    }

    public ValueTask DisposeAsync() => _renderer.DisposeAsync();
}

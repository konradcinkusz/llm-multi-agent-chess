using Xunit;

namespace Chess.Web.Tests;

/// <summary>
/// Without a render mode on <c>Routes</c> every page renders once as static HTML and no button, form or live
/// stream ever responds — bUnit cannot see that, so the root component is pinned here.
/// </summary>
public class InteractiveRenderModeTests
{
    private static string AppRazor()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Chess.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir.FullName, "src", "Chess.Web", "Components", "App.razor"));
    }

    // Both root components take their mode from PageRenderMode, which is InteractiveServer WITHOUT prerender: a prerender
    // awaits the API, so a page load stayed blank for as long as the API did not answer (150 s). The only exception is the
    // not-found page, which the server renders to HTML so that an unknown address answers 404 with the layout.
    [Fact]
    public void Routes_are_rendered_interactively()
    {
        Assert.Contains("<Routes @rendermode=\"PageRenderMode\"", AppRazor());
    }

    [Fact]
    public void Head_outlet_is_rendered_interactively_so_page_titles_update()
    {
        Assert.Contains("<HeadOutlet @rendermode=\"PageRenderMode\"", AppRazor());
    }

    [Fact]
    public void The_interactive_render_mode_does_not_prerender()
    {
        var app = AppRazor();

        Assert.Contains("new InteractiveServerRenderMode(prerender: false)", app);
        Assert.DoesNotContain("prerender: true", app);
        Assert.DoesNotContain("@rendermode=\"InteractiveServer\"", app);
    }

    [Fact]
    public void Only_the_not_found_page_is_rendered_without_a_render_mode()
    {
        Assert.Contains("StartsWithSegments(NotFoundPage.Path) == true ? null : Interactive", AppRazor());
    }

    [Fact]
    public void Scoped_component_styles_bundle_is_linked()
    {
        Assert.Contains("Chess.Web.styles.css", AppRazor());
    }
}

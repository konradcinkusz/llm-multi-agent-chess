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

    [Fact]
    public void Routes_are_rendered_interactively()
    {
        Assert.Contains("<Routes @rendermode=\"InteractiveServer\"", AppRazor());
    }

    [Fact]
    public void Head_outlet_is_rendered_interactively_so_page_titles_update()
    {
        Assert.Contains("<HeadOutlet @rendermode=\"InteractiveServer\"", AppRazor());
    }
}

using Bunit;
using Chess.Web.Components.Pages;
using Xunit;

namespace Chess.Web.Tests;

public class HomePageTests
{
    [Fact]
    public void HomeShowsTheProductName()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<Home>();

        Assert.Equal("LLM Chess Arena", cut.Find("h1").TextContent);
    }
}

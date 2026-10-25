using Chess.Web.Api;
using Xunit;

namespace Chess.Web.Tests.Api;

public class PlayerIdTests
{
    [Theory]
    [InlineData("gpt", "gpt", null, "classic")]
    [InlineData("gpt@high", "gpt", "high", "classic")]
    [InlineData("gpt~think", "gpt", null, "think")]
    [InlineData("claude-sonnet-5.5@medium~board", "claude-sonnet-5.5", "medium", "board")]
    public void Parse_reads_model_effort_and_style(string id, string model, string? effort, string style)
    {
        Assert.Equal(new PlayerId(model, effort, style), PlayerId.Parse(id));
    }

    [Theory]
    [InlineData("gpt", null, null, "gpt")]
    [InlineData("gpt", null, "classic", "gpt")]
    [InlineData("gpt", "high", "classic", "gpt@high")]
    [InlineData("gpt", "high", "think", "gpt@high~think")]
    [InlineData("gpt", "", "board", "gpt~board")]
    public void Compose_leaves_the_default_style_out(string model, string? effort, string? style, string expected)
    {
        Assert.Equal(expected, PlayerId.Compose(model, effort, style));
    }

    [Fact]
    public void Without_style_groups_the_styles_of_one_configuration()
    {
        Assert.Equal("gpt@high", PlayerId.Parse("gpt@high~think").WithoutStyle);
        Assert.Equal("gpt@high", PlayerId.Parse("gpt@high").WithoutStyle);
        Assert.Equal("gpt@high~think", PlayerId.Parse("gpt@high~think").ToString());
    }
}

using Chess.Api.Requests;
using Xunit;

namespace Chess.Api.Tests.Requests;

public class RequestCommandParserTests
{
    [Fact]
    public void A_plain_message_is_not_a_command_and_a_word_that_only_starts_with_the_trigger_is_not_one_either()
    {
        Assert.Null(RequestCommandParser.Parse("hello there"));
        Assert.Null(RequestCommandParser.Parse("x !g c1 vs d3"));
        Assert.Null(RequestCommandParser.Parse("!gx c1 vs d3"));
        Assert.False(RequestCommandParser.IsCommand("!gx c1 vs d3"));
    }

    [Fact]
    public void The_versus_word_is_optional_and_a_trailing_style_applies_to_both_sides()
    {
        var withVs = RequestCommandParser.Parse("!g c1 vs d3 think")!.Command!;
        var withoutVs = RequestCommandParser.Parse("!g c1 d3 think")!.Command!;

        Assert.Equal(new RequestCommand("c1", "think", "d3", "think"), withVs);
        Assert.Equal(withVs, withoutVs);
    }

    [Fact]
    public void A_style_on_one_side_applies_to_that_side_and_a_side_style_wins_over_the_trailing_one()
    {
        var parsed = RequestCommandParser.Parse("!g c1~board vs d3")!.Command!;
        Assert.Equal(new RequestCommand("c1", "board", "d3", null), parsed);

        var overridden = RequestCommandParser.Parse("!g c1~board vs d3~classic think")!.Command!;
        Assert.Equal(new RequestCommand("c1", "board", "d3", "classic"), overridden);
    }

    [Fact]
    public void Matching_is_case_insensitive_and_extra_words_are_ignored()
    {
        var parsed = RequestCommandParser.Parse("!G C1 VS D3 THINK please, thanks, go go!")!.Command!;

        Assert.Equal(new RequestCommand("c1", "think", "d3", "think"), parsed);
    }

    [Theory]
    [InlineData("!g")]
    [InlineData("!g c1")]
    [InlineData("!g c1 vs")]
    [InlineData("!g vs d3")]
    public void A_command_without_two_sides_is_malformed(string text)
    {
        var parse = RequestCommandParser.Parse(text);

        Assert.NotNull(parse);
        Assert.Null(parse.Command);
        Assert.Contains("menu code", parse.Error!);
    }

    [Theory]
    [InlineData("!g c1~nope vs d3")]
    [InlineData("!g c1~ vs d3")]
    public void An_unknown_or_empty_style_is_refused(string text)
    {
        var parse = RequestCommandParser.Parse(text);

        Assert.NotNull(parse);
        Assert.Null(parse.Command);
        Assert.Contains("prompt style", parse.Error!);
    }

    [Fact]
    public void An_unknown_code_parses_and_is_refused_later_by_the_menu()
    {
        // The parser knows no menu, so a code it has never heard of is still a well-formed command.
        Assert.Equal(new RequestCommand("zz", null, "d3", null), RequestCommandParser.Parse("!g zz vs d3")!.Command);
    }
}

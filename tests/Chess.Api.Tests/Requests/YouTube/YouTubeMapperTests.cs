using Chess.Api.Requests;
using System.Text.Json;
using Chess.Api.Requests.YouTube;
using Xunit;

namespace Chess.Api.Tests.Requests.YouTube;

/// <summary>The message-to-event rules of the mapper, on inline messages: the edge cases the fixtures do not cover.</summary>
public sealed class YouTubeMapperTests
{
    private static readonly DateTimeOffset Fallback = DateTimeOffset.Parse("2026-10-26T12:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Super_chat_currency_is_upper_cased()
    {
        var chat = Map(SuperChat(amountMicros: "3000000", currency: "usd"));

        Assert.NotNull(chat);
        Assert.Equal("USD", chat.Currency);
    }

    [Fact]
    public void Super_chat_with_a_currency_that_is_not_a_code_is_not_recorded()
    {
        Assert.Null(Map(SuperChat(amountMicros: "3000000", currency: "US$")));
    }

    [Fact]
    public void Super_chat_with_a_zero_amount_is_not_recorded()
    {
        Assert.Null(Map(SuperChat(amountMicros: "0", currency: "USD")));
    }

    [Fact]
    public void Command_prefix_is_matched_without_regard_to_case()
    {
        var chat = Map(TextMessage("!G c1 vs d3"));

        Assert.NotNull(chat);
        Assert.Null(chat.AmountMicros);
    }

    [Fact]
    public void A_text_message_without_the_command_prefix_is_ignored()
    {
        Assert.Null(Map(TextMessage("g c1 vs d3")));
    }

    [Fact]
    public void A_message_without_a_channel_id_is_ignored_because_credit_belongs_to_a_payer()
    {
        var json = """
            {"id":"LCC.no-channel","snippet":{"type":"textMessageEvent","textMessageDetails":{"messageText":"!g c1 vs d3"}},"authorDetails":{"displayName":"Nobody"}}
            """;

        Assert.Null(Map(json));
    }

    [Fact]
    public void A_message_without_publishedAt_takes_the_fallback_time()
    {
        var chat = Map(TextMessage("!g c1 vs d3"));

        Assert.NotNull(chat);
        Assert.Equal(Fallback, chat.At);
    }

    private static ChatEvent? Map(string json)
    {
        var message = System.Text.Json.JsonSerializer.Deserialize<LiveChatMessage>(json, FixtureJson.Options)!;
        return YouTubeChatMapper.Map(message, Fallback);
    }

    private static string SuperChat(string amountMicros, string currency) => $$$"""
        {"id":"LCC.test-superchat","snippet":{"type":"superChatEvent","authorChannelId":"UCtest","superChatDetails":{"amountMicros":"{{{amountMicros}}}","currency":"{{{currency}}}","userComment":"!g c1 vs d3"}},"authorDetails":{"channelId":"UCtest","displayName":"Test"}}
        """;

    private static string TextMessage(string text) => $$$"""
        {"id":"LCC.test-text","snippet":{"type":"textMessageEvent","authorChannelId":"UCtest","textMessageDetails":{"messageText":"{{{text}}}"}},"authorDetails":{"channelId":"UCtest","displayName":"Test"}}
        """;
}

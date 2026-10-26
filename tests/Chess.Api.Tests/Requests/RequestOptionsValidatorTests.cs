using Chess.Api.Requests;
using Chess.Llm;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chess.Api.Tests.Requests;

public class RequestOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(RequestOptions options, params string[] customModelIds)
    {
        var arena = new ArenaOptions { CustomModels = customModelIds.Select(id => new CustomModelOptions { Id = id }).ToList() };
        return new RequestOptionsValidator(Options.Create(arena)).Validate(null, options);
    }

    private static RequestOptions WithMenu(string playerId, RequestMode mode = RequestMode.Mock) => new()
    {
        Mode = mode,
        RequireApiKeyModels = true,
        Menu = [new RequestMenuEntry { Code = "x", PlayerId = playerId }],
    };

    [Fact]
    public void A_copilot_model_in_the_menu_is_refused_when_api_key_models_are_required()
    {
        var result = Validate(WithMenu("gpt-copilot"), "my-custom");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains("gpt-copilot") && failure.Contains("bring-your-own-key"));
    }

    [Fact]
    public void A_custom_model_in_the_menu_is_accepted_with_or_without_an_effort()
    {
        Assert.True(Validate(WithMenu("my-custom"), "my-custom").Succeeded);
        Assert.True(Validate(WithMenu("my-custom@high"), "my-custom").Succeeded);
    }

    [Fact]
    public void A_copilot_model_is_allowed_only_when_api_key_models_are_not_required()
    {
        var options = WithMenu("gpt-copilot");
        options.RequireApiKeyModels = false;

        Assert.True(Validate(options).Succeeded);
    }

    [Fact]
    public void An_enabled_mode_needs_a_menu_but_the_off_mode_does_not()
    {
        Assert.False(Validate(new RequestOptions { Mode = RequestMode.Mock }).Succeeded);
        Assert.True(Validate(new RequestOptions { Mode = RequestMode.Off }).Succeeded);
    }

    [Fact]
    public void Tiers_must_be_positive_and_the_fee_share_below_one()
    {
        Assert.False(Validate(new RequestOptions { Tiers = [0, 2] }).Succeeded);
        Assert.False(Validate(new RequestOptions { FeeShare = 1 }).Succeeded);
        Assert.True(Validate(new RequestOptions()).Succeeded);
    }
}

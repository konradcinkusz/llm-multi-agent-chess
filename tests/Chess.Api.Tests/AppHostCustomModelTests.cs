using Chess.AppHost;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Chess.Api.Tests;

/// <summary>
/// The AppHost's <c>Arena:CustomModels</c> forwarding (03 §12.1, ADR-014): plain fields become API environment variables, and
/// each key is a secret parameter. The rules are in <c>src/Chess.AppHost/CustomModelSettings.cs</c>, compiled into this project.
/// </summary>
public class AppHostCustomModelTests
{
    private const string KeyValue = "dummy-key-for-a-unit-test";

    private static IConfiguration Config(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))).Build();

    private static readonly (string Key, string? Value)[] MyLlama =
    [
        ("Arena:CustomModels:0:Id", "my-llama"),
        ("Arena:CustomModels:0:ProviderType", "openai"),
        ("Arena:CustomModels:0:BaseUrl", "http://127.0.0.1:8000/v1"),
        ("Arena:CustomModels:0:Model", "llama-local"),
    ];

    [Fact]
    public void Each_plain_field_is_forwarded_as_an_environment_variable_under_its_entry_index()
    {
        var settings = CustomModelSettings.Fields(Config(MyLlama)).ToDictionary(s => s.Name, s => s.Value);

        Assert.Equal("my-llama", settings["Arena__CustomModels__0__Id"]);
        Assert.Equal("openai", settings["Arena__CustomModels__0__ProviderType"]);
        Assert.Equal("http://127.0.0.1:8000/v1", settings["Arena__CustomModels__0__BaseUrl"]);
        Assert.Equal("llama-local", settings["Arena__CustomModels__0__Model"]);
    }

    [Fact]
    public void A_second_entry_keeps_its_own_index()
    {
        var settings = CustomModelSettings.Fields(Config(MyLlama.Concat([("Arena:CustomModels:1:Id", "other"), ("Arena:CustomModels:1:Model", "m2")]).ToArray()))
            .ToDictionary(s => s.Name, s => s.Value);

        Assert.Equal("other", settings["Arena__CustomModels__1__Id"]);
        Assert.Equal("m2", settings["Arena__CustomModels__1__Model"]);
        Assert.Equal("my-llama", settings["Arena__CustomModels__0__Id"]);
    }

    [Fact]
    public void An_api_key_written_as_a_plain_value_is_refused_and_the_message_does_not_quote_it()
    {
        var config = Config(MyLlama.Append(("Arena:CustomModels:0:ApiKey", KeyValue)).ToArray());

        var error = Assert.Throws<InvalidOperationException>(() => CustomModelSettings.Fields(config));

        Assert.Contains("custom-model-my-llama-key", error.Message);
        Assert.DoesNotContain(KeyValue, error.Message);
    }

    [Fact]
    public void An_empty_api_key_is_not_forwarded_and_is_not_refused()
    {
        var settings = CustomModelSettings.Fields(Config(MyLlama.Append(("Arena:CustomModels:0:ApiKey", "")).ToArray()));

        Assert.DoesNotContain(settings, s => s.Name.EndsWith("__ApiKey", StringComparison.Ordinal));
    }

    [Fact]
    public void A_configured_key_parameter_is_forwarded_as_the_entry_api_key()
    {
        var keys = CustomModelSettings.Keys(Config(MyLlama.Append(("Parameters:custom-model-my-llama-key", KeyValue)).ToArray()));

        var key = Assert.Single(keys);
        Assert.Equal("Arena__CustomModels__0__ApiKey", key.Name);
        Assert.Equal("custom-model-my-llama-key", key.ParameterName);
    }

    [Fact]
    public void A_key_parameter_that_is_not_configured_forwards_no_key()
    {
        Assert.Empty(CustomModelSettings.Keys(Config(MyLlama)));
    }

    [Fact]
    public void Only_the_entry_whose_parameter_is_configured_gets_a_key()
    {
        var config = Config(MyLlama.Concat([("Arena:CustomModels:1:Id", "other")])
            .Append(("Parameters:custom-model-other-key", KeyValue)).ToArray());

        var key = Assert.Single(CustomModelSettings.Keys(config));
        Assert.Equal("Arena__CustomModels__1__ApiKey", key.Name);
        Assert.Equal("custom-model-other-key", key.ParameterName);
    }

    [Fact]
    public void Without_custom_models_nothing_is_forwarded_so_the_fake_profile_is_unchanged()
    {
        var config = Config(("Llm:Provider", "Fake"));

        Assert.Empty(CustomModelSettings.Fields(config));
        Assert.Empty(CustomModelSettings.Keys(config));
    }
}

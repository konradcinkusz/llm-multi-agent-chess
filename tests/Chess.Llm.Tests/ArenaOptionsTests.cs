using Chess.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chess.Llm.Tests;

public class ArenaOptionsTests
{
    private const string Secret = "sk-test-secret-value-do-not-print";

    [Fact]
    public void AValidCustomModelPassesValidation()
    {
        var result = Validate(Valid());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AnIdWithAtIsRejectedWithAMessageNamingTheEntry()
    {
        var result = Validate(Valid(c => c.Id = "my-model@high"));

        Assert.False(result.Succeeded);
        Assert.Contains(Failures(result), f => f.StartsWith("Arena:CustomModels[0].Id", StringComparison.Ordinal) && f.Contains("'@'", StringComparison.Ordinal));
    }

    [Fact]
    public void AnIdWithSpacesIsRejected()
    {
        var result = Validate(Valid(c => c.Id = "my model"));

        Assert.False(result.Succeeded);
        Assert.Contains(Failures(result), f => f.Contains("must not contain spaces", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoCustomModelsWithTheSameIdAreRejectedWhateverTheCase()
    {
        var options = new ArenaOptions();
        options.CustomModels.Add(Valid(c => c.Id = "local"));
        options.CustomModels.Add(Valid(c => c.Id = "LOCAL"));

        var result = new ArenaOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(Failures(result), f => f.StartsWith("Arena:CustomModels[1].Id", StringComparison.Ordinal) && f.Contains("already used", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("azure-foundry")]
    [InlineData("")]
    public void AnUnknownProviderTypeIsRejected(string providerType)
    {
        var result = Validate(Valid(c => c.ProviderType = providerType));

        Assert.False(result.Succeeded);
        Assert.Contains(Failures(result), f => f.StartsWith("Arena:CustomModels[0].ProviderType", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://models.example.test")]
    [InlineData("/relative/path")]
    public void ABaseUrlMustBeAbsoluteHttpOrHttps(string baseUrl)
    {
        var result = Validate(Valid(c => c.BaseUrl = baseUrl));

        Assert.False(result.Succeeded);
        Assert.Contains(Failures(result), f => f.StartsWith("Arena:CustomModels[0].BaseUrl", StringComparison.Ordinal));
    }

    [Fact]
    public void TheModelNameAndWireApiAreRequired()
    {
        var result = Validate(Valid(c =>
        {
            c.Model = " ";
            c.WireApi = "";
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(Failures(result), f => f.StartsWith("Arena:CustomModels[0].Model is required", StringComparison.Ordinal));
        Assert.Contains(Failures(result), f => f.StartsWith("Arena:CustomModels[0].WireApi is required", StringComparison.Ordinal));
    }

    [Fact]
    public void AValidationMessageNeverQuotesTheApiKey()
    {
        var result = Validate(Valid(c =>
        {
            c.ApiKey = Secret;
            c.BaseUrl = "ftp://models.example.test";
            c.Model = "";
        }));

        Assert.False(result.Succeeded);
        Assert.DoesNotContain(Failures(result), f => f.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public void ToStringOfACustomModelNeverShowsTheApiKey()
    {
        var custom = Valid(c => c.ApiKey = Secret);

        Assert.DoesNotContain(Secret, custom.ToString(), StringComparison.Ordinal);
        Assert.Contains(custom.Id, custom.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInvalidCustomModelStopsTheHostAtStartupWithAClearMessage()
    {
        using var host = HostWithArena(new Dictionary<string, string?>
        {
            ["Arena:CustomModels:0:Id"] = "bad@id",
            ["Arena:CustomModels:0:ProviderType"] = "openai",
            ["Arena:CustomModels:0:BaseUrl"] = "http://127.0.0.1:8000/v1",
            ["Arena:CustomModels:0:Model"] = "tiny",
        });

        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("'@'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AValidCustomModelBindsWithItsApiKeyAndStartsTheHost()
    {
        using var host = HostWithArena(new Dictionary<string, string?>
        {
            ["Arena:CustomModels:0:Id"] = "tiny",
            ["Arena:CustomModels:0:DisplayName"] = "Tiny local",
            ["Arena:CustomModels:0:ProviderType"] = "OpenAI",
            ["Arena:CustomModels:0:BaseUrl"] = "http://127.0.0.1:8000/v1",
            ["Arena:CustomModels:0:Model"] = "tiny-7b",
            ["Arena:CustomModels:0:ApiKey"] = Secret,
        });

        await host.StartAsync();

        var bound = host.Services.GetRequiredService<IOptions<ArenaOptions>>().Value.CustomModels.Single();
        Assert.Equal("tiny", bound.Id);
        Assert.Equal("chat-completions", bound.WireApi);
        Assert.Equal(Secret, bound.ApiKey);
        await host.StopAsync();
    }

    private static CustomModelOptions Valid(Action<CustomModelOptions>? change = null)
    {
        var custom = new CustomModelOptions
        {
            Id = "tiny",
            ProviderType = "openai",
            BaseUrl = "http://127.0.0.1:8000/v1",
            Model = "tiny-7b",
            WireApi = "chat-completions",
        };
        change?.Invoke(custom);
        return custom;
    }

    private static ValidateOptionsResult Validate(CustomModelOptions custom)
    {
        var options = new ArenaOptions();
        options.CustomModels.Add(custom);
        return new ArenaOptionsValidator().Validate(null, options);
    }

    private static IEnumerable<string> Failures(ValidateOptionsResult result) => result.Failures ?? [];

    private static IHost HostWithArena(IDictionary<string, string?> arena)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["Llm:Provider"] = "Fake";
        foreach (var (key, value) in arena)
        {
            builder.Configuration[key] = value;
        }

        builder.AddLlmGateway();
        return builder.Build();
    }
}

using Chess.Api.Requests.YouTube;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Chess.Api.Tests.Requests.YouTube;

/// <summary>The poller starts only in YouTube mode, and a YouTube start without its secrets fails at startup (03 §15.5).</summary>
public sealed class YouTubeIntakeRegistrationTests
{
    [Theory]
    [InlineData("Mock")]
    [InlineData("Off")]
    public void Mock_and_Off_modes_register_nothing(string mode)
    {
        var services = new ServiceCollection();

        services.AddYouTubeIntake(Config(
            ("Arena:Requests:Mode", mode),
            ("Arena:Requests:YouTube:ClientId", "client-id-fixture"),
            ("Arena:Requests:YouTube:ClientSecret", "client-secret-fixture"),
            ("Arena:Requests:YouTube:RefreshToken", "refresh-token-fixture")));

        Assert.Empty(services);
    }

    [Fact]
    public void No_mode_registers_nothing()
    {
        var services = new ServiceCollection();

        services.AddYouTubeIntake(Config(("Arena:Requests:YouTube:ClientId", "client-id-fixture")));

        Assert.Empty(services);
    }

    [Fact]
    public void YouTube_mode_with_its_secrets_registers_the_poller_as_a_hosted_service()
    {
        var services = new ServiceCollection();

        services.AddYouTubeIntake(Config(
            ("Arena:Requests:Mode", "YouTube"),
            ("Arena:Requests:YouTube:ClientId", "client-id-fixture"),
            ("Arena:Requests:YouTube:ClientSecret", "client-secret-fixture"),
            ("Arena:Requests:YouTube:RefreshToken", "refresh-token-fixture")));

        Assert.Contains(services, service => service.ServiceType == typeof(IHostedService)
            && service.ImplementationType == typeof(YouTubeChatPollerService));
    }

    [Fact]
    public void YouTube_mode_without_a_refresh_token_fails_and_names_only_the_missing_setting()
    {
        var services = new ServiceCollection();

        var error = Assert.Throws<InvalidOperationException>(() => services.AddYouTubeIntake(Config(
            ("Arena:Requests:Mode", "YouTube"),
            ("Arena:Requests:YouTube:ClientId", "client-id-fixture"),
            ("Arena:Requests:YouTube:ClientSecret", "client-secret-sentinel"))));

        Assert.Contains("Arena:Requests:YouTube:RefreshToken", error.Message);
        Assert.DoesNotContain("client-secret-sentinel", error.Message);
        Assert.DoesNotContain("client-id-fixture", error.Message);
    }

    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.ToDictionary(pair => pair.Key, pair => (string?)pair.Value)).Build();
}

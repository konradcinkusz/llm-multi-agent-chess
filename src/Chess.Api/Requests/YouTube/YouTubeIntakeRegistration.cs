using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Chess.Api.Requests.YouTube;

/// <summary>
/// Registers the YouTube intake (03 §15.5). It is added only when <c>Arena:Requests:Mode</c> is <c>YouTube</c>: in Mock and Off
/// mode nothing is registered and nothing polls. A YouTube start without its secrets fails at startup, and the error names
/// the settings that are missing, never their values.
/// </summary>
public static class YouTubeIntakeRegistration
{
    public const string ModeKey = "Arena:Requests:Mode";

    /// <summary>Adds the YouTube intake to the builder, in <c>YouTube</c> mode only.</summary>
    public static WebApplicationBuilder AddYouTubeIntake(this WebApplicationBuilder builder)
    {
        builder.Services.AddYouTubeIntake(builder.Configuration);
        return builder;
    }

    /// <summary>Adds the YouTube intake when <c>Arena:Requests:Mode</c> is <c>YouTube</c>; otherwise changes nothing.</summary>
    /// <exception cref="InvalidOperationException">YouTube mode without its OAuth secrets.</exception>
    public static IServiceCollection AddYouTubeIntake(this IServiceCollection services, IConfiguration configuration)
    {
        if (!string.Equals(configuration[ModeKey], YouTubeIntakeOptions.Mode, StringComparison.OrdinalIgnoreCase))
        {
            return services;
        }

        var options = configuration.GetSection(YouTubeIntakeOptions.Section).Get<YouTubeIntakeOptions>() ?? new YouTubeIntakeOptions();
        if (options.MissingSecrets() is { Count: > 0 } missing)
        {
            throw new InvalidOperationException(
                $"Arena:Requests:Mode is {YouTubeIntakeOptions.Mode}, but these settings are missing: {string.Join(", ", missing)}. " +
                "Set the AppHost secret parameters youtube-client-id, youtube-client-secret and youtube-refresh-token (docs/REQUESTS.md).");
        }

        services.AddSingleton(options);
        services.AddSingleton(sp => new GoogleTokenProvider(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, options,
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(_ => new YouTubeChatClient(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, options));
        services.AddSingleton<YouTubeChatPoller>();
        services.AddHostedService<YouTubeChatPollerService>();
        return services;
    }
}

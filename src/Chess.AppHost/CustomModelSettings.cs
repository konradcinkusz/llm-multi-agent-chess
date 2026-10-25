using Microsoft.Extensions.Configuration;

namespace Chess.AppHost;

/// <summary>
/// Turns the AppHost's <c>Arena:CustomModels</c> configuration into what the API process is given (03 §12.1, ADR-014).
/// Pure: no Aspire types, so <c>Chess.Api.Tests</c> compiles this file and tests the rules directly (see its csproj).
/// <para>
/// The plain fields (<c>Id</c>, <c>DisplayName</c>, <c>ProviderType</c>, <c>BaseUrl</c>, <c>Model</c>, <c>WireApi</c>) are
/// forwarded as environment variables, <c>Arena__CustomModels__{n}__{Field}</c>. An API key is never a value here: it is a
/// secret parameter <c>custom-model-{id}-key</c>, read from <c>Parameters:</c> configuration (user secrets), and forwarded as
/// <c>Arena__CustomModels__{n}__ApiKey</c>. A key written as a plain value is refused, so it cannot sit in a file.
/// </para>
/// </summary>
internal static class CustomModelSettings
{
    public const string Section = "Arena:CustomModels";
    private const string ApiKeyField = "ApiKey";

    /// <summary>The secret parameter name that holds the API key of the custom model <paramref name="id"/>.</summary>
    public static string ParameterNameFor(string id) => $"custom-model-{id}-key";

    /// <summary>The API environment variable for one field of entry <paramref name="index"/>, e.g. <c>Arena__CustomModels__0__BaseUrl</c>.</summary>
    public static string EnvironmentNameFor(string index, string field) => $"Arena__CustomModels__{index}__{field}";

    /// <summary>
    /// Every plain field of every entry, with its value. Entries with no value are left out.
    /// </summary>
    /// <exception cref="InvalidOperationException">An API key is set as a plain value. The message names the parameter to use instead, never the value.</exception>
    public static IReadOnlyList<EnvironmentSetting> Fields(IConfiguration configuration)
    {
        var settings = new List<EnvironmentSetting>();
        foreach (var entry in configuration.GetSection(Section).GetChildren())
        {
            foreach (var field in entry.GetChildren())
            {
                if (field.Key == ApiKeyField)
                {
                    if (field.Value is { Length: > 0 })
                    {
                        throw new InvalidOperationException(
                            $"{Section}:{entry.Key}:{ApiKeyField} is set as a plain value. Put the key in the secret parameter " +
                            $"'{ParameterNameFor(entry["Id"] ?? "")}' (user secrets: Parameters:{ParameterNameFor(entry["Id"] ?? "")}) instead.");
                    }

                    continue;
                }

                if (field.Value is { Length: > 0 } value)
                {
                    settings.Add(new EnvironmentSetting(EnvironmentNameFor(entry.Key, field.Key), value));
                }
            }
        }

        return settings;
    }

    /// <summary>
    /// The API key of every entry whose secret parameter is set, as the environment variable that carries it. An entry with no
    /// parameter gets none: its endpoint is called without a key, which is right for an endpoint that takes none.
    /// </summary>
    public static IReadOnlyList<SecretSetting> Keys(IConfiguration configuration)
    {
        var keys = new List<SecretSetting>();
        foreach (var entry in configuration.GetSection(Section).GetChildren())
        {
            var id = entry["Id"];
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var parameter = ParameterNameFor(id);
            if (configuration[$"Parameters:{parameter}"] is { Length: > 0 })
            {
                keys.Add(new SecretSetting(EnvironmentNameFor(entry.Key, ApiKeyField), parameter));
            }
        }

        return keys;
    }
}

/// <summary>A plain environment variable for the API, with its value.</summary>
internal sealed record EnvironmentSetting(string Name, string Value);

/// <summary>An environment variable for the API whose value is the secret parameter <see cref="ParameterName"/>.</summary>
internal sealed record SecretSetting(string Name, string ParameterName);

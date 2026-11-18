using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Chess.Api.Anthropic;

/// <summary>A model the Anthropic API lists for a key: its id and display name.</summary>
public sealed record AnthropicModel(string Id, string DisplayName);

/// <summary>The key was not accepted by the Anthropic API (401 or 403).</summary>
public sealed class AnthropicKeyRejectedException : Exception
{
    public AnthropicKeyRejectedException() : base("Anthropic did not accept this API key.")
    {
    }
}

/// <summary>The Anthropic API could not be reached or answered something else than a model list. The message never holds the key.</summary>
public sealed class AnthropicUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Lists the models an API key can use: <c>GET /v1/models</c>, followed page by page.</summary>
public sealed class AnthropicModelDiscovery(IHttpClientFactory httpClients, IOptions<AnthropicOptions> options)
{
    public const string HttpClientName = "anthropic";
    private const string ApiVersion = "2023-06-01";
    private const int MaxPages = 20;

    /// <exception cref="AnthropicKeyRejectedException">Anthropic refused the key.</exception>
    /// <exception cref="AnthropicUnavailableException">Anthropic could not be reached, or its answer was not a model list.</exception>
    public async Task<IReadOnlyList<AnthropicModel>> ListAsync(string apiKey, CancellationToken ct)
    {
        var settings = options.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(settings.Timeout);

        var models = new List<AnthropicModel>();
        string? after = null;
        try
        {
            var http = httpClients.CreateClient(HttpClientName);
            for (var page = 0; page < MaxPages; page++)
            {
                var url = $"{settings.BaseUrl.TrimEnd('/')}/v1/models?limit=1000" + (after is null ? "" : $"&after_id={Uri.EscapeDataString(after)}");
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", ApiVersion);
                using var response = await http.SendAsync(request, timeout.Token);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    throw new AnthropicKeyRejectedException();
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new AnthropicUnavailableException($"Anthropic answered {(int)response.StatusCode} to the model list.");
                }

                await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var json = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);
                var (listed, hasMore, lastId) = Parse(json.RootElement);
                models.AddRange(listed);
                if (!hasMore || lastId is null)
                {
                    break;
                }

                after = lastId;
            }
        }
        catch (HttpRequestException ex)
        {
            throw new AnthropicUnavailableException("Anthropic could not be reached.", ex);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new AnthropicUnavailableException("Anthropic did not answer in time.", ex);
        }
        catch (JsonException ex)
        {
            throw new AnthropicUnavailableException("Anthropic's model list could not be read.", ex);
        }

        return models;
    }

    /// <summary>Reads one page of <c>/v1/models</c>: the models, whether more follow, and the id to continue after.</summary>
    public static (IReadOnlyList<AnthropicModel> Models, bool HasMore, string? LastId) Parse(JsonElement root)
    {
        var models = new List<AnthropicModel>();
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } modelId)
                {
                    var name = item.TryGetProperty("display_name", out var display) ? display.GetString() : null;
                    models.Add(new AnthropicModel(modelId, string.IsNullOrWhiteSpace(name) ? modelId : name));
                }
            }
        }

        var hasMore = root.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True;
        var lastId = root.TryGetProperty("last_id", out var last) ? last.GetString() : null;
        return (models, hasMore, lastId);
    }
}

using System.Text.Json;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Chess.Web.Api;

/// <summary>
/// What a failed call to the API looks like to a page, in plain words. The resilience handler throws
/// <see cref="BrokenCircuitException"/> while its circuit is open and <see cref="TimeoutRejectedException"/> or a
/// <see cref="TaskCanceledException"/> on a timeout: none of them is an <see cref="HttpRequestException"/>, so a loader that
/// catches only that one ends the Blazor circuit.
/// </summary>
public static class ApiFailure
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// True for a failure a page shows and survives: anything except the cancellation the page asked for itself. A
    /// timeout surfaces as a <see cref="TaskCanceledException"/> that the page's own token did not cause.
    /// </summary>
    public static bool IsFailure(Exception ex, CancellationToken ct) =>
        !(ct.IsCancellationRequested && ex is OperationCanceledException);

    /// <summary>The words to show after "Could not …:" or on their own. They never name a library type.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        BrokenCircuitException => "The arena API is not answering right now. This page tries again on its own.",
        TimeoutRejectedException or TimeoutException or OperationCanceledException =>
            "The arena API took too long to answer.",
        HttpRequestException { StatusCode: null } => $"The arena API cannot be reached. {ex.Message}".TrimEnd(),
        JsonException => "The arena API sent a reply this page cannot read.",
        _ => ex.Message,
    };

    /// <summary>
    /// The text of a refused call: the validation messages when the API sent them, else its detail or title, else the
    /// status code. The API answers with <c>application/problem+json</c>, read here as text.
    /// </summary>
    internal static async Task<string> ProblemTextAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var problem = JsonSerializer.Deserialize<ProblemBody>(body, JsonOptions);
            var messages = problem?.Errors?.Values.SelectMany(values => values).ToList();
            if (messages is { Count: > 0 })
            {
                return string.Join(" ", messages);
            }

            var text = problem?.Detail ?? problem?.Title;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }
        catch (JsonException)
        {
            // Not a problem document: fall through to the status code.
        }

        return $"HTTP {(int)response.StatusCode}";
    }

    /// <summary>Throws an <see cref="HttpRequestException"/> carrying the API's reason and its status code for a refused call.</summary>
    internal static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await ProblemTextAsync(response, ct), null, response.StatusCode);
        }
    }

    private sealed record ProblemBody(string? Title, string? Detail, Dictionary<string, string[]>? Errors);
}

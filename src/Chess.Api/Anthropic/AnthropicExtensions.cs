using Chess.Api.Catalog;
using Microsoft.AspNetCore.Http;
using HttpResults = Microsoft.AspNetCore.Http.Results;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Chess.Api.Anthropic;

public sealed record SaveAnthropicKeyRequest(string? ApiKey);

public static class AnthropicExtensions
{
    public static IHostApplicationBuilder AddAnthropic(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<AnthropicOptions>().BindConfiguration("Arena:Anthropic");
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddHttpClient(AnthropicModelDiscovery.HttpClientName);
        builder.Services.AddSingleton<AnthropicKeyStore>();
        builder.Services.AddSingleton<AnthropicModelDiscovery>();
        builder.Services.AddSingleton<AnthropicService>();
        builder.Services.AddHostedService<AnthropicStartupService>();
        return builder;
    }

    /// <summary>The key never leaves the API: a status says whether one is saved and how many models it lists.</summary>
    public static IEndpointRouteBuilder MapAnthropicEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/anthropic");

        group.MapGet("", (AnthropicService anthropic) => anthropic.Status);

        group.MapPut("", async (SaveAnthropicKeyRequest request, AnthropicService anthropic, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.ApiKey))
            {
                return HttpResults.Problem("The API key is empty.", statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                return HttpResults.Ok(await anthropic.SaveAsync(request.ApiKey.Trim(), ct));
            }
            catch (AnthropicKeyRejectedException ex)
            {
                return HttpResults.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (AnthropicUnavailableException ex)
            {
                return HttpResults.Problem($"{ex.Message} The key was not saved.", statusCode: StatusCodes.Status502BadGateway);
            }
        });

        group.MapPost("/refresh", async (AnthropicService anthropic, CancellationToken ct) =>
        {
            try
            {
                return await anthropic.RefreshAsync(ct) is { } status
                    ? HttpResults.Ok(status)
                    : HttpResults.Problem("No Anthropic API key is saved.", statusCode: StatusCodes.Status409Conflict);
            }
            catch (AnthropicKeyRejectedException ex)
            {
                return HttpResults.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (AnthropicUnavailableException ex)
            {
                return HttpResults.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        group.MapDelete("", async (AnthropicService anthropic, CancellationToken ct) =>
        {
            await anthropic.RemoveAsync(ct);
            return HttpResults.NoContent();
        });

        return endpoints;
    }
}

using Chess.Api.Catalog;
using Chess.Llm;
using Microsoft.Extensions.Options;

namespace Chess.Api.Anthropic;

/// <summary>What the owner may know about the Anthropic key. The key itself is never part of it.</summary>
public sealed record AnthropicStatusDto(bool Configured, int ModelCount, DateTimeOffset? RefreshedAt, string? Error);

/// <summary>
/// The Anthropic key and the models it unlocks. Saving a key checks it against the Anthropic API, keeps it in the
/// <see cref="AnthropicKeyStore"/>, lists every model the key can use and hands them to the gateway as custom models
/// (<see cref="RuntimeCustomModels"/>). Discovered models carry no price: they can be played and shown, but not quoted to a viewer
/// (03 §15.2). They enter no rating differently from any other model.
/// </summary>
public sealed class AnthropicService(
    AnthropicKeyStore store,
    AnthropicModelDiscovery discovery,
    RuntimeCustomModels runtime,
    ModelCatalog catalog,
    IOptions<AnthropicOptions> options,
    TimeProvider clock,
    ILogger<AnthropicService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile AnthropicStatusDto _status = new(Configured: false, 0, null, null);

    public AnthropicStatusDto Status => _status;

    /// <summary>Loads the saved key, if any, and lists its models. A failure is recorded in the status and never thrown.</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        if (store.Load() is not { } key)
        {
            return;
        }

        await _gate.WaitAsync(ct);
        try
        {
            await RefreshCoreAsync(key, throwOnFailure: false, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Checks <paramref name="apiKey"/>, saves it and lists its models. Nothing is saved when Anthropic refuses the key.</summary>
    /// <exception cref="AnthropicKeyRejectedException">Anthropic refused the key.</exception>
    /// <exception cref="AnthropicUnavailableException">Anthropic could not be asked, so the key was not saved.</exception>
    public async Task<AnthropicStatusDto> SaveAsync(string apiKey, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var models = await discovery.ListAsync(apiKey, ct);
            store.Save(apiKey);
            Apply(apiKey, models);
            return _status;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Asks Anthropic for the models again with the saved key.</summary>
    /// <returns>The status, or null when no key is saved.</returns>
    public async Task<AnthropicStatusDto?> RefreshAsync(CancellationToken ct)
    {
        if (store.Load() is not { } key)
        {
            return null;
        }

        await _gate.WaitAsync(ct);
        try
        {
            await RefreshCoreAsync(key, throwOnFailure: true, ct);
            return _status;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Deletes the saved key and takes its models off the list.</summary>
    public async Task RemoveAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            store.Delete();
            runtime.Replace([]);
            catalog.Invalidate();
            _status = new AnthropicStatusDto(Configured: false, 0, null, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RefreshCoreAsync(string key, bool throwOnFailure, CancellationToken ct)
    {
        try
        {
            Apply(key, await discovery.ListAsync(key, ct));
        }
        catch (Exception ex) when (ex is AnthropicKeyRejectedException or AnthropicUnavailableException)
        {
            logger.LogWarning("Anthropic models could not be listed: {Reason}", ex.Message);
            _status = new AnthropicStatusDto(Configured: true, runtime.Current.Count, _status.RefreshedAt, ex.Message);
            if (throwOnFailure)
            {
                throw;
            }
        }
    }

    private void Apply(string key, IReadOnlyList<AnthropicModel> models)
    {
        var baseUrl = options.Value.BaseUrl;
        runtime.Replace([.. models.Select(m => new CustomModelOptions
        {
            Id = m.Id,
            DisplayName = $"{m.DisplayName} (Anthropic API)",
            ProviderType = "anthropic",
            BaseUrl = baseUrl,
            Model = m.Id,
            ApiKey = key,
        })]);
        catalog.Invalidate();
        _status = new AnthropicStatusDto(Configured: true, models.Count, clock.GetUtcNow(), null);
        logger.LogInformation("{ModelCount} Anthropic models listed", models.Count);
    }
}

/// <summary>Loads the saved key when the app starts, without holding the start up: a refused or unreachable Anthropic is only a status.</summary>
public sealed class AnthropicStartupService(AnthropicService anthropic, ILogger<AnthropicStartupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await anthropic.StartAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("The saved Anthropic key could not be loaded: {ExceptionType}", ex.GetType().Name);
        }
    }
}

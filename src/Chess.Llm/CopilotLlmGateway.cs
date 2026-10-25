using System.Diagnostics;
using System.Globalization;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chess.Llm;

/// <summary>
/// <see cref="ILlmGateway"/> on the GitHub Copilot SDK. The SDK client is started by
/// <see cref="StartAsync"/> (called from the hosted service) and never throws out of it: a runtime that
/// cannot start, is not signed in or lists no models is reported through <see cref="Status"/>.
/// </summary>
public sealed class CopilotLlmGateway(IOptions<CopilotOptions> options, ILogger<CopilotLlmGateway> logger) : ILlmGateway
{
    private static readonly ActivitySource ActivitySource = new("Chess.Llm");

    private readonly CopilotOptions _options = options.Value;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private volatile LlmGatewayStatus _status = new(LlmGatewayState.Unavailable, "Copilot runtime not started");
    private CopilotClient? _client;

    public LlmGatewayStatus Status => _status;

    /// <summary>Starts the runtime, checks sign-in and lists models. Records the outcome in <see cref="Status"/>; never throws.</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (_client is not null)
            {
                return;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_options.StartupTimeout);

            try
            {
                _client = new CopilotClient(BuildClientOptions());
                await _client.StartAsync(timeout.Token);

                var auth = await _client.GetAuthStatusAsync(timeout.Token);
                if (!auth.IsAuthenticated)
                {
                    await FailAsync(LlmGatewayState.NotAuthenticated, auth.StatusMessage ?? "Not signed in to GitHub Copilot.");
                    return;
                }

                var models = await _client.ListModelsAsync(timeout.Token);
                foreach (var model in models)
                {
                    logger.LogDebug("Model {ModelId} billing: {Billing}", model.Id, DescribeBilling(model));
                }

                _status = new LlmGatewayStatus(LlmGatewayState.Ready, $"Signed in as {auth.Login}; {models.Count} models.");
                logger.LogInformation("Copilot runtime ready with {ModelCount} models", models.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Copilot runtime unavailable");
                await FailAsync(LlmGatewayState.Unavailable, $"Copilot runtime unavailable: {ex.GetType().Name}: {ex.Message}");
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>Closes the runtime. Safe to call when it never started.</summary>
    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            await DisposeClientAsync();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

#pragma warning disable GHCP001 // The SDK marks the token price unit experimental and CachePrice deprecated; both are read here only.
    public async Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct)
    {
        var client = RequireReady();
        var models = await client.ListModelsAsync(ct);
        var priceUnits = await ListPriceUnitsAsync(client, ct);

        return models
            .Select(m => new LlmModelInfo(m.Id, m.Name, m.Billing?.Multiplier, !IsDisabled(m.Policy),
                TokenPriceMapping.Map(
                    m.Billing?.TokenPrices?.BatchSize,
                    m.Billing?.TokenPrices?.InputPrice,
                    m.Billing?.TokenPrices?.OutputPrice,
                    m.Billing?.TokenPrices?.CacheReadPrice,
                    m.Billing?.TokenPrices?.CachePrice,
                    priceUnits.TryGetValue(m.Id, out var nanoAiu) ? nanoAiu : null)))
            .ToList();
    }

    /// <summary>
    /// The unit each model's token prices are quoted in, by model id (<c>models.listTokenPriceUnits</c>). A failure
    /// leaves the models listed without prices; it does not make the gateway unavailable.
    /// </summary>
    private async Task<Dictionary<string, bool>> ListPriceUnitsAsync(CopilotClient client, CancellationToken ct)
    {
        var nanoAiuByModel = new Dictionary<string, bool>(StringComparer.Ordinal);
        try
        {
            var units = await client.Rpc.Models.ListTokenPriceUnitsAsync(ct);
            foreach (var entry in units.Units)
            {
                if (entry.Unit == TokenPriceUnit.AiCredits || entry.Unit == TokenPriceUnit.NanoAiu)
                {
                    nanoAiuByModel[entry.ModelId] = entry.Unit == TokenPriceUnit.NanoAiu;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not list token price units; models are listed without prices");
        }

        return nanoAiuByModel;
    }

    private static string DescribeBilling(ModelInfo model)
    {
        var prices = model.Billing?.TokenPrices;
        var multiplier = model.Billing?.Multiplier?.ToString(CultureInfo.InvariantCulture) ?? "none";
        var tokenPrices = prices is null
            ? "none"
            : $"batch {prices.BatchSize}, input {prices.InputPrice}, output {prices.OutputPrice}, cache read {prices.CacheReadPrice}, cache {prices.CachePrice}";
        return $"multiplier {multiplier}; token prices {tokenPrices}";
    }
#pragma warning restore GHCP001

    public async Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct)
    {
        var client = RequireReady();
        var session = await client.CreateSessionAsync(new SessionConfig
        {
            Model = options.ModelId,
            SystemMessage = new SystemMessageConfig { Mode = SystemMessageMode.Replace, Content = options.SystemPrompt },
            // No tools: an empty allow-list, and every permission request is rejected as a second guard.
            AvailableTools = [],
            Streaming = false,
            OnPermissionRequest = RejectAllPermissions,
        }, ct);

        return new CopilotConversation(session, options, logger);
    }

#pragma warning disable GHCP001 // The SDK marks its permission decision type experimental; it is the only way to reject a request.
    private static Task<PermissionDecision> RejectAllPermissions(PermissionRequest request, PermissionInvocation invocation) =>
        Task.FromResult(PermissionDecision.Reject("Chess arena: this session has no tools."));
#pragma warning restore GHCP001

    // The wire value of Rpc.ModelPolicyState.Disabled; the SDK type itself is experimental.
    private static bool IsDisabled(GitHub.Copilot.ModelPolicy? policy) =>
        string.Equals(policy?.State, "disabled", StringComparison.OrdinalIgnoreCase);

    private CopilotClient RequireReady() =>
        _client is { } client && _status.State == LlmGatewayState.Ready
            ? client
            : throw new LlmUnavailableException(_status.Detail);

    private CopilotClientOptions BuildClientOptions() => new()
    {
        Connection = _options switch
        {
            { CliUrl: not null } => RuntimeConnection.ForUri(_options.CliUrl, null),
            { CliPath: not null } => RuntimeConnection.ForStdio(_options.CliPath, []),
            _ => null,
        },
        GitHubToken = _options.GitHubToken,
    };

    private async Task FailAsync(LlmGatewayState state, string detail)
    {
        _status = new LlmGatewayStatus(state, detail);
        await DisposeClientAsync();
    }

    private async Task DisposeClientAsync()
    {
        if (_client is not { } client)
        {
            return;
        }

        _client = null;
        try
        {
            await client.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Copilot runtime did not stop cleanly");
        }
    }

    /// <summary>One SDK session = one side of one game. Replies are serialised by the caller (one move at a time).</summary>
    private sealed class CopilotConversation : ILlmConversation
    {
        private readonly CopilotSession _session;
        private readonly LlmConversationOptions _options;
        private readonly ILogger _logger;
        // The usage and reasoning events of the reply in flight. Replies are serialised, so one reply at a time.
        private readonly object _replyGate = new();
        private readonly IDisposable _usageSubscription;
        private readonly IDisposable _reasoningSubscription;
        private UsageTally _usage = new();
        private readonly List<string> _reasoningBlocks = [];

        public CopilotConversation(CopilotSession session, LlmConversationOptions options, ILogger logger)
        {
            _session = session;
            _options = options;
            _logger = logger;
            _usageSubscription = session.On<AssistantUsageEvent>(AddUsage);
            _reasoningSubscription = session.On<AssistantReasoningEvent>(AddReasoning);
        }

        public string ModelId => _options.ModelId;

        public async Task<LlmReply> AskAsync(string prompt, CancellationToken ct)
        {
            using var activity = ActivitySource.StartActivity($"chat {ModelId}", ActivityKind.Client);
            activity?.SetTag("gen_ai.request.model", ModelId);

            ResetReply();
            var stopwatch = Stopwatch.StartNew();

            AssistantMessageEvent? final;
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(_options.Timeout);
                final = await _session.SendAndWaitAsync(new MessageOptions { Prompt = prompt }, _options.Timeout, linked.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException && !ct.IsCancellationRequested)
            {
                await AbortQuietlyAsync();
                throw new TimeoutException($"Model {ModelId} did not finish its reply within {_options.Timeout}.", ex);
            }
            catch (OperationCanceledException)
            {
                await AbortQuietlyAsync();
                throw;
            }

            stopwatch.Stop();
            var text = final?.Data.Content
                ?? throw new InvalidOperationException($"Model {ModelId} finished without an assistant message.");

            ReplyUsage usage;
            string? reasoning;
            lock (_replyGate)
            {
                usage = _usage.Snapshot();
                reasoning = ReplyReasoning.Choose(final.Data.ReasoningText, _reasoningBlocks);
            }

            if (usage.InputTokens is { } inputTokens)
            {
                activity?.SetTag("gen_ai.usage.input_tokens", inputTokens);
            }

            if (usage.OutputTokens is { } outputTokens)
            {
                activity?.SetTag("gen_ai.usage.output_tokens", outputTokens);
            }

            _logger.LogDebug("Model {ModelId} replied in {LatencyMs} ms", ModelId, stopwatch.ElapsedMilliseconds);
            return new LlmReply(text, stopwatch.Elapsed, usage.InputTokens, usage.OutputTokens,
                reasoning, usage.ReasoningTokens, usage.CachedInputTokens, usage.AiCredits);
        }

        public async ValueTask DisposeAsync()
        {
            _usageSubscription.Dispose();
            _reasoningSubscription.Dispose();
            await _session.DisposeAsync();
        }

        private void AddUsage(AssistantUsageEvent e)
        {
            lock (_replyGate)
            {
                _usage.Add(
                    e.Data.InputTokens,
                    e.Data.OutputTokens,
                    e.Data.ReasoningTokens,
                    e.Data.CacheReadTokens,
                    e.Data.CopilotUsage?.TotalNanoAiu,
                    e.Data.AiCreditsStatus == AiCreditsStatus.Unavailable);
            }
        }

        private void AddReasoning(AssistantReasoningEvent e)
        {
            lock (_replyGate)
            {
                _reasoningBlocks.Add(e.Data.Content);
            }
        }

        private void ResetReply()
        {
            lock (_replyGate)
            {
                _usage = new UsageTally();
                _reasoningBlocks.Clear();
            }
        }

        private async Task AbortQuietlyAsync()
        {
            try
            {
                using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _session.AbortAsync(cancel.Token);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogWarning(ex, "Could not abort the in-flight reply of model {ModelId}", ModelId);
            }
        }
    }
}

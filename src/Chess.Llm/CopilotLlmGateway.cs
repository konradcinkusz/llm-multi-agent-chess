using System.Diagnostics;
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

    public async Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct)
    {
        var client = RequireReady();
        var models = await client.ListModelsAsync(ct);

        return models
            .Select(m => new LlmModelInfo(m.Id, m.Name, m.Billing?.Multiplier, !IsDisabled(m.Policy)))
            .ToList();
    }

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
        private readonly object _usageGate = new();
        private readonly IDisposable _usageSubscription;
        private int? _inputTokens;
        private int? _outputTokens;

        public CopilotConversation(CopilotSession session, LlmConversationOptions options, ILogger logger)
        {
            _session = session;
            _options = options;
            _logger = logger;
            _usageSubscription = session.On<AssistantUsageEvent>(AddUsage);
        }

        public string ModelId => _options.ModelId;

        public async Task<LlmReply> AskAsync(string prompt, CancellationToken ct)
        {
            using var activity = ActivitySource.StartActivity($"chat {ModelId}", ActivityKind.Client);
            activity?.SetTag("gen_ai.request.model", ModelId);

            ResetUsage();
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

            var (input, output) = SnapshotUsage();
            if (input is { } inputTokens)
            {
                activity?.SetTag("gen_ai.usage.input_tokens", inputTokens);
            }

            if (output is { } outputTokens)
            {
                activity?.SetTag("gen_ai.usage.output_tokens", outputTokens);
            }

            _logger.LogDebug("Model {ModelId} replied in {LatencyMs} ms", ModelId, stopwatch.ElapsedMilliseconds);
            return new LlmReply(text, stopwatch.Elapsed, input, output);
        }

        public async ValueTask DisposeAsync()
        {
            _usageSubscription.Dispose();
            await _session.DisposeAsync();
        }

        private void AddUsage(AssistantUsageEvent e)
        {
            lock (_usageGate)
            {
                if (e.Data.InputTokens is { } input)
                {
                    _inputTokens = (_inputTokens ?? 0) + (int)input;
                }

                if (e.Data.OutputTokens is { } output)
                {
                    _outputTokens = (_outputTokens ?? 0) + (int)output;
                }
            }
        }

        private void ResetUsage()
        {
            lock (_usageGate)
            {
                _inputTokens = null;
                _outputTokens = null;
            }
        }

        private (int? Input, int? Output) SnapshotUsage()
        {
            lock (_usageGate)
            {
                return (_inputTokens, _outputTokens);
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

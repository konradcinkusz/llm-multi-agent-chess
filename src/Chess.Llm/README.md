# Chess.Llm

The only project that references `GitHub.Copilot.SDK`. Copilot SDK types never appear in a public
signature here (ADR-004, 03 §3.1, checked by `PublicSurfaceTests`).

## Spike: `GitHub.Copilot.SDK` 1.0.26

Inspected from the NuGet package: the bundled XML docs (`lib/net10.0/GitHub.Copilot.SDK.xml`) and a
reflection probe of `lib/net10.0/GitHub.Copilot.SDK.dll`.

### What the gateway uses

| Need | SDK member (namespace `GitHub.Copilot` unless noted) |
|---|---|
| Client | `CopilotClient(CopilotClientOptions)`; `StartAsync(ct)`; `StopAsync()`; `DisposeAsync()` |
| Client options | `CopilotClientOptions.Connection`, `.GitHubToken`. `UseLoggedInUser` is left at its default (`true`, which the SDK turns off when a token is given) |
| Connection | `RuntimeConnection.ForStdio(string path, IList<string> args)` for `Copilot:CliPath`; `RuntimeConnection.ForUri(string url, string? connectionToken)` for `Copilot:CliUrl`; `null` = SDK-bundled runtime |
| Sign-in | `CopilotClient.GetAuthStatusAsync(ct)` → `GetAuthStatusResponse { bool IsAuthenticated; string AuthType, Host, Login, StatusMessage }` |
| Models | `CopilotClient.ListModelsAsync(ct)` → `IList<ModelInfo>`; `ModelInfo { Id, Name, Capabilities, Policy: ModelPolicy?, Billing: ModelBilling? }`; `ModelBilling.Multiplier` is `double?`; `ModelPolicy.State` is a string |
| Session | `CopilotClient.CreateSessionAsync(SessionConfig, ct)` → `CopilotSession`, with `SessionConfig.Model`, `SessionConfig.SystemMessage = new SystemMessageConfig { Mode = SystemMessageMode.Replace, Content }`, `SessionConfig.AvailableTools = []`, `SessionConfig.Streaming = false`, `SessionConfig.OnPermissionRequest` |
| Permissions | `Func<PermissionRequest, PermissionInvocation, Task<Rpc.PermissionDecision>>`; the handler returns `Rpc.PermissionDecision.Reject(string feedback)` for every request |
| One move | `CopilotSession.SendAndWaitAsync(MessageOptions { Prompt }, TimeSpan? timeout, ct)` → `Task<AssistantMessageEvent?>`; `.Data.Content` is the reply text. It resolves on the session's idle event, and its own timeout defaults to 60 s, so the gateway always passes `LlmConversationOptions.Timeout` (the API sets it from `Arena:MoveTimeoutSeconds`, 180 s by default) |
| Abort on timeout | `CopilotSession.AbortAsync(ct)`. The docs say `SendAndWait` does not stop in-flight work, so a timed-out move is aborted |
| Usage | `CopilotSession.On<AssistantUsageEvent>(handler)` (returns `IDisposable`); per event `Data.InputTokens`, `OutputTokens`, `ReasoningTokens`, `CacheReadTokens` (nullable), `CopilotUsage.TotalNanoAiu` and `AiCreditsStatus`. Summed per reply (see Usage and cost) |
| Reasoning | `CopilotSession.On<AssistantReasoningEvent>(handler)`, `Data.Content`; the final `AssistantMessageData.ReasoningText` (see Usage and cost) |
| Token prices | `CopilotClient.ListModelsAsync` → `ModelInfo.Billing.TokenPrices` (`Rpc.ModelBillingTokenPrices`: `BatchSize`, `InputPrice`, `OutputPrice`, `CacheReadPrice`, deprecated `CachePrice`) |
| Price unit | `CopilotClient.Rpc.Models.ListTokenPriceUnitsAsync(ct)` → `Units` of `ModelTokenPriceUnit { ModelId, Unit }`, `Unit` is `Rpc.TokenPriceUnit` (`AiCredits` or `NanoAiu`) |
| Teardown | `CopilotSession.DisposeAsync()` (per conversation) |

## Usage and cost (G1, owner request 2026-10-25)

What the gateway returns, and where each number comes from. Cost is never part of the rating (RATING.md §8).

**Per reply** (`LlmReply`), the events of that reply are summed (`UsageTally`):

| Field | Source | Null when |
|---|---|---|
| `InputTokens`, `OutputTokens` | `AssistantUsageData.InputTokens` / `OutputTokens` | no event reported it |
| `ReasoningTokens` | `AssistantUsageData.ReasoningTokens`. The SDK documents them as output tokens used for reasoning, so they are already inside `OutputTokens` and are not added again | no event reported it |
| `CachedInputTokens` | `AssistantUsageData.CacheReadTokens` | no event reported it |
| `AiCredits` | `CopilotUsage.TotalNanoAiu` ÷ 1 000 000 000, summed | no event carried `CopilotUsage`, or every such event has `AiCreditsStatus = Unavailable` |

- `AiCreditsStatus.Unavailable` means "not reported", not "free": that event adds no cost and no zero.
- `AiCreditsStatus.Partial` (the SDK: "the numeric value is only a reported subtotal") is summed as reported. The reply's credits can therefore undercount when the runtime says partial.
- Unit assumption, not yet verified live: **1 AI unit = 1 AI credit**, so `credits = nanoAIU ÷ 1e9`. It is isolated in one constant, `UsageTally.NanoAiuPerAiCredit` for the usage side, and confirmed or corrected by the owner's live smoke run (`scripts/smoke-copilot.sh` prints the credits per request).

**Reasoning** (`ReplyReasoning`): the final message's `ReasoningText` when it is not blank; otherwise the `AssistantReasoningEvent` contents of the reply, in order, joined by a blank line; otherwise null. Null means the model did not expose its reasoning.

**Token prices** (`TokenPriceMapping`):

- The runtime quotes each model's prices per `BatchSize` tokens, in the unit `models.listTokenPriceUnits` reports for that model. `ModelBillingTokenPrices` carries no unit of its own, so the unit is read from the second call.
- `TokenPriceUnit.AiCredits`: the prices are already in AI credits and are kept as they are.
- `TokenPriceUnit.NanoAiu`: the prices are in nano-AIU and are divided by 1 000 000 000.
- The call returns one entry per model that publishes prices. A model with no entry, or with a unit other than these two, gets no `Pricing` (it is not guessed at). The same goes for a model with no `BatchSize`.
- `CacheReadPrice` is the cached-input price. `CachePrice` is its deprecated alias and is used only when `CacheReadPrice` is absent.
- The multiplier (`Billing.Multiplier`) is still read and returned in `PremiumMultiplier`. It is null for token-billed models.
- If `models.listTokenPriceUnits` fails, the models are still listed, without prices, and a warning is logged. It does not change the gateway state.
- Startup logs each model's raw billing at Debug (`multiplier …; token prices batch …, input …, output …, cache read …, cache …`). The unit is not in that line; it comes from the call above.

**Fake gateway.** Usage is approximated from the text, four characters per token: input is the prompt, output is the reply plus the reasoning. The reasoning names the chosen move. Both fake models are priced at 1.0 input and 4.0 output AI credits per 1 000 000 tokens, so a fake reply costs well under a thousandth of a credit. The fake reports these credits on every reply, so a fake game's
usage shows the `reported` cost basis (measured on a 100-reply game of `fake-a@high` against `random`: about 0.00016 credits a reply).

Startup status: `GetStatusAsync` → `GetStatusResponse { Version, ProtocolVersion }` exists but is not
used; the gateway's state comes from `GetAuthStatusAsync` and `ListModelsAsync`.

### Decisions taken from the spike

- **No tools.** `AvailableTools = []` plus a reject-all permission handler. The XML doc for
  `AvailableTools` only says "only these tools will be available when specified", so whether an empty
  list means "no tools" rather than "not specified" is **not confirmed**. The permission handler is
  the second guard, and the owner's live smoke test (T4.2) must confirm it.
- **System prompt replaces the default.** `SystemMessageMode.Replace` drops the CLI's own system
  message, which is the intent for a move-only session. Not confirmed live.
- **Experimental types.** `Rpc.PermissionDecision` and `Rpc.ModelPolicyState` are marked
  `GHCP001` (evaluation only). The permission pragma is scoped to the one method that uses it. The
  "disabled" policy check compares to the literal wire value `"disabled"`, not to the experimental enum.
- **Pinned.** The version is pinned in `Directory.Packages.props` (1.0.26). Nothing here relies on
  anything newer.

### Not verified here

No Copilot CLI or sign-in exists in this sandbox, so none of the runtime calls above has run.
Unit tests cover only the degraded paths: a missing CLI path and a refused loopback URI both end in
`LlmGatewayState.Unavailable`, with no exception and no network. Checked against the docs alone:
the session/send/usage flow, the abort on timeout, and the `ModelInfo.Billing` multiplier. The live
check is the owner's, through the T4.2 smoke script.

Also checked against the docs alone (G1): the usage and reasoning events, the token price fields and the
`models.listTokenPriceUnits` unit. Not seen on a live reply: the 1 AI unit = 1 AI credit assumption, whether
`CopilotUsage.TotalNanoAiu` is set on the usage events of a real move, and the price values for each model.
The owner's live smoke run confirms all three.

## Configuration

| Key | Default | Meaning |
|---|---|---|
| `Llm:Provider` | `Copilot` | `Copilot` or `Fake`; anything else fails at startup |
| `Copilot:CliPath` | SDK-bundled runtime | Copilot CLI executable. No extra arguments are passed (see Open points) |
| `Copilot:CliUrl` | unset | Already-running runtime (`port`, `host:port` or URL); takes precedence over `CliPath` |
| `Copilot:GitHubToken` | unset | Overrides the logged-in user. Never logged or stored |
| `Copilot:StartupTimeout` | 30 s | Start, sign-in check and model listing together |
| `Llm:Seed` | `1` | Seed for `FakeLlmGateway` |
| `Llm:FakeLatencyMs` | `0` | Simulated reply delay for `FakeLlmGateway` |
| `Arena:CustomModels` | empty | Models on an OpenAI-compatible or Azure endpoint (see below); validated at startup |

## Reasoning effort and custom models (B1, ADR-014)

- **Effort.** `LlmConversationOptions.ReasoningEffort` becomes `SessionConfig.ReasoningEffort`. A Copilot model
  lists its efforts (`ModelInfo.SupportedReasoningEfforts`) and its default (`DefaultReasoningEffort`) in
  `LlmModelInfo`; a model that lists none gets `null` for both. The gateway does not check an effort against the
  list for Copilot models; the API validates it (B2). `FakeLlmGateway`'s `fake-a` and `fake-b` list `low`,
  `medium` and `high` with `medium` as default, and refuse any other effort with an `ArgumentException`.
- **Custom models.** Each `Arena:CustomModels:N` entry has `Id`, `DisplayName` (defaults to `Id`), `ProviderType`
  (`openai` or `azure`), `BaseUrl` (absolute http or https), `Model` (the name the endpoint expects, sent as
  `ProviderConfig.WireModel`), `WireApi` (default `chat-completions`) and `ApiKey`. A custom model is listed with
  `Custom = true` and started with `SessionConfig.Provider` and no Copilot model id.
- **Validation at startup.** An `Id` may not contain `@` or spaces and must be unique, ignoring case. Every entry needs a
  valid `ProviderType`, `BaseUrl`, `Model` and `WireApi`. A bad entry stops the host with a message that names it
  (`Arena:CustomModels[0].Id 'x@y' must not contain '@' ...`). Messages never quote a URL or a key.
- **Clash with a Copilot id.** The Copilot ids are known only once the runtime has listed them. A custom id that a
  Copilot model already uses is not listed, and an error is logged naming it. The gateway keeps running.
- **The key.** `ApiKey` is never a plain value. The AppHost (`src/Chess.AppHost/CustomModelSettings.cs`) reads it from the
  secret parameter `Parameters:custom-model-<Id>-key` (user secrets) and passes it to the API as `Arena__CustomModels__N__ApiKey`.
  A plain `ApiKey`, in a file or an environment variable, stops the AppHost at startup. The key is never logged, printed or
  stored. `CustomModelOptions.ToString()` leaves it out.
- **Fake profile.** Configured custom models are listed and served by `FakeLlmGateway` as well, so the whole path
  runs offline. Set them in the AppHost's user secrets (`Arena:CustomModels:0:Id` and the other fields), as
  `docs/BENCHMARK.md` section 4 shows, or as environment variables such as `Arena__CustomModels__0__Id`. The AppHost forwards
  both to the API.
- **Not verified live.** Whether a provider session also needs a Copilot sign-in is unknown. Custom models are listed
  only while the Copilot runtime is `Ready`, so a runtime must be up even for a local endpoint. The owner's live run
  will show whether it must also be signed in.
- **Prices.** Custom models have no `Pricing` yet, so their cost estimate is unknown.

## Status and health

- `CopilotLlmGateway.StartAsync` (called from `CopilotClientHostedService`) never throws: it records
  `Ready` (signed in and models listed), `NotAuthenticated` (sign-in check failed) or `Unavailable`
  (anything else, including the startup timeout). Non-`Ready` states dispose the client.
- Health check `copilot` (`LlmGatewayHealthCheck`): `Ready` and `Fake` map to Healthy; `NotAuthenticated`
  and `Unavailable` map to Degraded. It is never Unhealthy (P8).

## Open points

- For a user-supplied `Copilot:CliPath`, the gateway passes no command-line arguments. The docs say
  `ForStdio` takes "extra arguments" and does not say what a runtime started from a path needs. If
  the CLI needs a server-mode flag, the live check will show it, and the fix is one line in
  `BuildClientOptions`.
- `Activity` records (`chat {model}` with `gen_ai.request.model` and the usage tags) are emitted by
  `CopilotLlmGateway` but cannot be unit-tested without a live session.

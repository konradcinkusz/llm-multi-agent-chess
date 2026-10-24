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
| One move | `CopilotSession.SendAndWaitAsync(MessageOptions { Prompt }, TimeSpan? timeout, ct)` → `Task<AssistantMessageEvent?>`; `.Data.Content` is the reply text. It resolves on the session's idle event, and its own timeout defaults to 60 s, so the gateway always passes `LlmConversationOptions.Timeout` |
| Abort on timeout | `CopilotSession.AbortAsync(ct)`. The docs say `SendAndWait` does not stop in-flight work, so a timed-out move is aborted |
| Usage | `CopilotSession.On<AssistantUsageEvent>(handler)` (returns `IDisposable`); `AssistantUsageEvent.Data.InputTokens` / `.OutputTokens` (nullable numbers, summed per move) |
| Teardown | `CopilotSession.DisposeAsync()` (per conversation) |

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

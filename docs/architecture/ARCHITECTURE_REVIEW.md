# Final architecture review — LLM Chess Arena

**Date:** 2026-10-24 · **Task:** T4.4 in [06-TASKS](06-TASKS.md) · **Mode:** REVIEW (playbook)
**Reviewed against:** [00-REFERENCE-ARCHITECTURE](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/architecture/00-REFERENCE-ARCHITECTURE.md) (P1–P15 and the §3 checklist) and the architecture-standards PLAYBOOK (REVIEW output shape).
**Code reviewed:** `origin/main` at `cee42c6` (#62). Since the review began, only `.github/workflows/deploy-pages.yml` changed on `main` relative to the commit first checked out (`223a346`); the evidence below holds for both.

Every claim is backed by a command result, a `path:line` reference or a live check. Where something could not be checked, the section says so.

## Summary

- **Blocking: none.** The build passes with `-warnaserror`; the full test run passes once a Chromium is available (the browser journeys need one, see M-1); the CI guard jobs and all six CI jobs on `main` pass.
- **High (1):** the loopback-only design has no protection against cross-origin requests. A page on another site can start a broadcast day, and the Blazor websocket accepts a foreign origin. Tracked as follow-up **F1** in [06-TASKS](06-TASKS.md).
- **Medium (4):** `dotnet test Chess.sln` needs a browser download; several docs contradict the code; broadcast games sit outside the Copilot concurrency cap; the kernel has no architecture test.
- **Low (10):** small wiring, hygiene and consistency items (section 4).
- The architecture is largely as specified. Deviations are recorded in section 5. Nothing has been fixed in this task; the findings are for follow-up.

## 1. Scope and method

**Scope.** All of `main`: the solution (`Chess.sln`, 14 projects), the CI workflows, the docs (`README.md`, `CLAUDE.md`, `docs/`), and the code in `src/` and `tests/`. The review does not read every test body. Test coverage was judged from test names, counts and the parts of the code cited below.

**Commands run** (environment: Ubuntu sandbox; .NET SDK installed with `sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0`, which gave `10.0.113`):

| # | Command | Result |
|---|---|---|
| 1 | `dotnet build Chess.sln -warnaserror` | Succeeded, 0 warnings, 0 errors (1 min 7 s). Note: builds `Chess.Llm`, which downloads a Copilot CLI (see L-7). |
| 2 | `dotnet test Chess.sln --no-build` | Chess.Llm 21, Chess.Rating 22, Chess.Engine 136, Chess.Web 329 and Chess.Api 356 passed, 3 skipped (Stockfish, no binary). **Chess.E2E 4 failed:** `playwright install chromium` could not run in the sandbox (see M-1). |
| 3 | `E2E_CHROMIUM_PATH=/opt/pw-browsers/chromium-1243/chrome-linux64/chrome dotnet test tests/Chess.E2E --no-build` | **4 of 4 passed**, 17 s. Overall with a browser: 871 tests, 868 passed, 3 skipped. |
| 4 | `dotnet test tests/Chess.Api.Tests --no-build --filter "FullyQualifiedName~Chess.Api.Tests.Evals"` | 37 passed (the offline AI evals). |
| 5 | `dotnet run --project src/Chess.AppHost --launch-profile fake --no-build` (background) | Running. `curl /alive` → 200; `/health` lists `self`, `database`, `copilot` (Healthy, "Fake gateway"), `stockfish` (Healthy, "absent"); `/api/status` → `{"copilot":"Fake",…}`. Headless Chromium screenshot of `/results` captured. |
| 6 | Bind check: `curl --noproxy '*' http://192.0.2.2:5070/` and `:5080/` (the sandbox's LAN address); `/proc/<pid>/net/tcp` | Both refused. Listening sockets are `127.0.0.1:5070` and `127.0.0.1:5080` only. |
| 7 | Origin probes (`curl -H "Origin: http://evil.example"`): broadcast plan, then `POST …/start` and `POST …/stop`; `POST /_blazor/negotiate`; websocket upgrade to `/_blazor` | Start 200 (state became `running`), stop 200, negotiate 200, upgrade **101 Switching Protocols**. Full results in H-1. |
| 8 | `scripts/broadcast-smoke.sh` | Exit 0. The one-hour fake schedule reached `finished`; standings printed. |
| 9 | `scripts/dev-setup.sh` | Exit 0. Reports .NET OK; Docker, `copilot` missing; Stockfish, gitleaks absent. |
| 10 | `scripts/smoke-copilot.sh` (no argument) and `CI=1 … scripts/smoke-copilot.sh x` | Both exit 2 with the documented message. |
| 11 | CI guard jobs reproduced locally: `dotnet sln Chess.sln list` plus the AppHost registration loop from `ci.yml`; `grep GitHub.Copilot.SDK` over `*.csproj`; kernel line count | Solution and AppHost check exit 0; Copilot SDK referenced only by `Chess.Llm`; kernel 160 lines (limit 400). |
| 12 | gitleaks 8.21.2 (checksum verified against the pinned SHA-256 in `ci.yml`): `gitleaks detect --source . --config .gitleaks.toml --log-opts="HEAD"` | "no leaks found", 50 commits scanned. |
| 13 | `git ls-files`, then `grep` over the list, for token prefixes (`ghp_`, `github_pat_`, `gho_`, `AKIA`, `sk-`, `xox`), private-key headers, and personal paths (`/home/`, `/root/`) | No matches. |
| 14 | `gh api repos/…/actions/runs?branch=main`, `…/jobs` | CI: 6 of 6 jobs succeeded on `cee42c6`. `deploy-pages` failed in *Run actions/configure-pages* (see L-10). Job logs were not downloadable from this sandbox, so the cause is inferred from the workflow's header comment. |
| 15 | `gh api repos/konradcinkusz/llm-multi-agent-chess --jq .description` | Still mentions Streamlit. |
| 16 | Code reading with `grep`, `cat -n` and the exact `path:line` references below | Basis for every checklist row. |

**Not run** (each is in section 7): the Docker/PostgreSQL path (no daemon: `/var/run/docker.sock` is absent); the real Copilot path (no `copilot` login); Stockfish integration (no binary); the mutation runs described in EVALS.md (not repeated); `dotnet ef migrations script`; a wall-clock 24 h run; any test in CI other than what CI itself reports.

**Standards.** Both documents were fetched with `curl` (HTTP 200): `00-REFERENCE-ARCHITECTURE.md` and `PLAYBOOK.md`. The agent proxy refused two NuGet search hosts (`azuresearch-*.nuget.org`) while the AppHost ran. That did not affect the build or tests.

## 2. Strengths

- **A thin, enforced kernel.** `Chess.ServiceDefaults` is 160 lines with no `Chess.*` reference except a logging source name (`src/Chess.ServiceDefaults/Extensions.cs:69`). The CI size guard passes (`.github/workflows/ci.yml:66`).
- **Program.cs is a manifest.** `src/Chess.Api/Program.cs` is 30 lines of `Add…()` and `Map…()` calls, as P9 requires.
- **The Copilot SDK is isolated.** Only `src/Chess.Llm/Chess.Llm.csproj` references `GitHub.Copilot.SDK`. The usage is in one file, `src/Chess.Llm/CopilotLlmGateway.cs`. `tests/Chess.Llm.Tests/PublicSurfaceTests.cs:13` checks the public surface by reflection.
- **Degradation is visible and never fatal.** `src/Chess.Llm/LlmGatewayHealthCheck.cs:14-18` maps a missing login to Degraded, never Unhealthy. The live `/health` (run 5) names each integration and its state.
- **Ratings are pure and reproducible.** `src/Chess.Rating/` uses no clock, I/O or EF; the only randomness is `new Random(options.Seed)` in the bootstrap (`RatingEngine.cs:212`). 22 rating tests pass. The published formula is in `docs/RATING.md`.
- **No fallback move.** `src/Chess.Api/Players/LlmChessPlayer.cs:47` returns `Forfeit: true` when every attempt is rejected. The eval suite (37 tests) grades that path.
- **Runner robustness.** A game that throws is recorded as aborted (`src/Chess.Api/Games/GameRunner.cs:224-227`). The broadcast runner retries with back-off and then skips (`src/Chess.Api/Broadcast/BroadcastRunner.cs:176-189`). `ExecuteAsync` cannot throw out (`BroadcastRunner.cs:42-77`). A restart closes the slot in play and the interrupted game (`BroadcastRunner.cs:255-278`, `src/Chess.Api/Persistence/InterruptedGameSweep.cs`).
- **Leak discipline in the web app.** Every page with a timer, stream or CTS implements `IDisposable` and cancels or disposes it. Checked by grep across `src/Chess.Web/Components`.
- **Migrations, not ensure.** `src/Chess.Api/Persistence/MigrationHostedService.cs:23-29` calls `MigrateAsync` for relational providers and `EnsureCreated` only for InMemory. Retry and command timeout match P4 (`PersistenceExtensions.cs:26`).
- **Real browser journeys.** Four Playwright journeys pass on the fake profile in a real Chromium (run 3). The Results page's section order matches 03 §6.1 (`src/Chess.Web/Components/Pages/Results/ResultsPage.razor:47-162`).
- **Regression pins from CLAUDE.md are tested.** `tests/Chess.Web.Tests/InteractiveRenderModeTests.cs:23-30` pins the render mode on `Routes` and `HeadOutlet`, and the scoped stylesheet link.
- **Secrets stay in AppHost parameters.** `src/Chess.AppHost/AppHost.cs:33` (generated Postgres password, secret), `:41-43` (optional Copilot token, secret). The tracked-file sweep (run 13) and gitleaks (run 12) are both clean.
- **The API binds loopback only and has no CORS.** Verified in run 6 and by `grep -rni cors src tests` (no results). This is ADR-006 as written, and see H-1 for its limit.
- **Docs that were checked hold up.** The broadcast runbook's numbers (24 games per day, 40 requests per side, 1 920 requests) follow from the planner defaults. The smoke script ran to `finished` (run 8). The EVALS refusal paths behave as written (run 10). The 37 eval tests pass (run 4).

## 3. Compliance checklist

### 3.1 Principles P1–P15

Status vocabulary: **Met**, **Met with recorded deviation**, **N/A** (with reason), **Not met**.

| P | Principle | Status | Evidence |
|---|---|---|---|
| P1 | AppHost is the composition root; one command; fixed external ports | **Met** (gap: L-1) | `src/Chess.AppHost/AppHost.cs:10-18` declares `api` and `web`, `WithReference`, `WaitFor`, fixed ports 5080 (`isProxied: false`) and 5070. The `fake` profile starts with no Docker (run 5). `web` has no `WithHttpHealthCheck` (L-1). |
| P2 | Kernel is plumbing only; size ceiling mechanical; architecture test asserts no entity types | **Not met** (M-4) | Content: `src/Chess.ServiceDefaults/` has 160 lines and no domain type. Size: CI `kernel-size` job (`ci.yml:66-79`) passes. Architecture test: **missing**. `tests/Chess.Api.Tests/ServiceDefaultsTests.cs` tests health and the banner only (lines 16-77), with no reflection over the kernel's types. |
| P2a | Every service calls `AddServiceDefaults()` | **Met** | `src/Chess.Api/Program.cs:4`, `src/Chess.Web/Program.cs:8`. |
| P3 | Service per bounded context; database per service | **Met** (ADR-003) | One service (`Chess.Api`) owns Postgres. `Chess.Web` has no EF or Npgsql reference (`grep -rln Npgsql src/Chess.Web` → none). `Chess.Web` references only `Chess.ServiceDefaults`. |
| P4 | Provider-portable; migrated, never ensured; migrations in a hosted service | **Met with recorded deviation** | `MigrationHostedService.cs:23-29`; retry 10 × 30 s and 60 s command timeout at `PersistenceExtensions.cs:26`; two migrations (`InitialCreate`, `AddBroadcast`). Deviation: no silent InMemory fallback when `Database:Provider` is PostgreSQL and no connection string is set. The app throws instead (`PersistenceExtensions.cs:23`). Recorded in 03 §4 (owner requirement: everything in Postgres). |
| P5 | Config from environment; secrets from the platform; scanner in CI | **Met with recorded deviation** | Secrets as AppHost parameters (`AppHost.cs:33, 41-43`). gitleaks job in CI (`ci.yml:42-63`); pre-commit config present. No literal secret (run 13). Deviation: no signing key or JWKS (ADR-006). The design-time connection string `Host=localhost;Database=chess` (`src/Chess.Api/Persistence/ArenaDbContextFactory.cs:15`) has no password, so it is not a secret. |
| P6 | One container per service; multi-stage Dockerfile; runtime major = TFM | **N/A** | No Dockerfile is tracked (`git ls-files` has none). Nothing is deployed (R1, ADR-002). |
| P7 | Fly.io; one `fly.toml` per service | **N/A** | No `fly.toml` (ADR-002). |
| P8 | Optional dependencies degrade; health reports them; banner prints them | **Met** | Copilot: `LlmGatewayHealthCheck.cs:14-18`. Stockfish absent → `NoOpMoveQualityAnalyzer`, healthy "absent". Live `/health` lists all integrations (run 5). Banner pinned by `ServiceDefaultsTests.cs:57-77`. |
| P9 | `Program.cs` is a manifest; wiring in extension methods | **Met** | `src/Chess.Api/Program.cs:1-30`; `src/Chess.Web/Program.cs` is short and uses its own extensions. |
| P10 | Extension points are interfaces registered in DI | **Met** | `ILlmGateway`, `IChessPlayerFactory`, `IMoveQualityAnalyzer`, `IGameRunner`, `IBroadcastController`. |
| P11 | Anti-corruption at the edge; no vendor type crosses it | **Met** (enforcement gap: L-4) | Copilot types only in `src/Chess.Llm/CopilotLlmGateway.cs`; `PublicSurfaceTests.cs:13`. |
| P12 | Tag-driven CI/CD with change detection and ordered deploy | **N/A** | Nothing is deployed (ADR-002). CI runs on every pull request and push to `main` (`ci.yml:3-6`), without path filters. Pages deploys on push to `main` (static site only). |
| P13 | Test at the layer that has the logic; E2E in CI; green E2E is evidence | **Met** (hygiene: M-1) | 871 tests across Engine, Rating, Llm, Api, Web and E2E. E2E is wired into CI (`ci.yml:130`, job `e2e`). Browser journeys run on the fake gateway only (`tests/Chess.E2E/ArenaFixture.cs`, `--ARENA_FAKE=1`). Characterisation came after the engine move (L-8). |
| P14 | Docs live in the repo and record reasoning; a stale README is a finding | **Not met** (M-2) | Decision records exist: ADR-001…011 in `docs/architecture/05-DECISIONS.md`, plus `docs/RATING.md`, `docs/BROADCAST.md`, `docs/EVALS.md`. Accuracy fails: the README and CLAUDE.md contradict the code and CI (M-2). |
| P15 | OTLP traces, metrics and logs; health probes filtered out of traces | **Met** | Logging via OpenTelemetry (`src/Chess.ServiceDefaults/Extensions.cs:57`), metrics and tracing (`:63-68`), OTLP only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (`:74`), probe filter (`:71`). The Copilot span `chat {model}` carries `gen_ai.request.model` and token counts (`src/Chess.Llm/CopilotLlmGateway.cs:179-215`). Not exercised against a collector in this review. |

### 3.2 The §3 compliance checklist (21 items)

| # | Item | Status | Evidence |
|---|---|---|---|
| 1 | Declared in the AppHost with `WithReference`, `WaitFor` and `WithHttpHealthCheck` | **Not met** for `web` (L-1); Met for `api` | `AppHost.cs:12` (`api` has the health check); `:14-18` (`web` has none). |
| 2 | Calls `AddServiceDefaults()` and `MapDefaultEndpoints()` | **Met** | `src/Chess.Api/Program.cs:4, 19`; `src/Chess.Web/Program.cs:8, 25`. |
| 3 | Exposes `/health` and `/alive`; platform check points at `/health` | **Met** (platform N/A) | Run 5: `/alive` 200; `/health` returns JSON with four checks. |
| 4 | Emits OTLP traces, metrics and logs | **Met** (code); not exercised against a collector | `Extensions.cs:57-76`. |
| 5 | Owns its database; no other service connects to it | **Met** | Only `Chess.Api` references EF/Npgsql. |
| 6 | Schema applied by `MigrateAsync` from provider-specific migrations, in a hosted service | **Met** | `MigrationHostedService.cs:23-29`; `src/Chess.Api/Persistence/Migrations/` (2 migrations). |
| 7 | All configuration from environment; no secret in source, config or comment; scanner in CI | **Met** | Run 12 and run 13; `ci.yml:42-63`. |
| 8 | Exactly one service holds a signing key | **N/A** | No authentication (ADR-006). |
| 9 | Kernel holds no entity, DTO, enum, seed, pricing constant or user string; architecture test and CI size check | **Not met** (architecture test missing: M-4) | Size check met (`ci.yml:66-79`, 160 lines). Architecture test absent (`ServiceDefaultsTests.cs`). |
| 10 | Every optional integration has a no-op or fallback | **Met** | Copilot (`Fake`/Degraded), Stockfish (`NoOpMoveQualityAnalyzer`). |
| 11 | Health reports every optional integration; banner prints the same list | **Met** | Run 5; `ServiceDefaultsTests.cs:57-77`. |
| 12 | Multi-stage Dockerfile; runtime major = TFM; `:8080`; non-root | **N/A** | ADR-002; no Dockerfile. |
| 13 | One `fly.toml`; `min_machines_running = 1` for in-request callers | **N/A** | ADR-002; no Fly. |
| 14 | Outbound `HttpClient`s carry the standard resilience handler with explicit timeouts | **Met** (timeouts are the handler's defaults) | `ServiceDefaults/Extensions.cs:30-34` applies `AddStandardResilienceHandler` to every client. `GameEventsClient` sets `Timeout = Infinite` explicitly (`src/Chess.Web/Program.cs:20`). `ArenaApiClient` relies on the handler's defaults (see L-3). |
| 15 | `Program.cs` is a manifest; wiring in `ServiceCollectionExtensions` | **Met** | `src/Chess.Api/Program.cs`. |
| 16 | Extension points are interfaces registered in DI, not base classes | **Met** | See P10. |
| 17 | Test project; logic-bearing layer covered; characterisation written before a move | **Met** for coverage; **not met** for the ordering rule (L-8) | Engine 136 tests; Rating 22; Api 356 (+3 skipped); Web 329; E2E 4. |
| 18 | Built by the tag-driven workflow with path-based change detection | **N/A** | ADR-002. CI runs all jobs on every pull request. |
| 19 | Architectural decisions recorded in `docs/` | **Met** | `docs/architecture/01…06`, `ADR-001…011`, `RATING.md`, `BROADCAST.md`, `EVALS.md`. |

### 3.3 Project invariants (CLAUDE.md)

| Invariant | Status | Evidence |
|---|---|---|
| Copilot SDK types never leave `Chess.Llm` | **Met**, enforced for csproj and public surface only | `PublicSurfaceTests.cs:13`; `ci.yml:81-95`. Usage inside `Chess.Api` is not checked (L-4). |
| `Chess.Rating` is pure | **Met** | `src/Chess.Rating/*.cs`: no I/O, no clock; `RatingEngine.cs:212` (seeded `Random`). |
| No fallback move | **Met** | `LlmChessPlayer.cs:47`; evals (37 pass). |
| Contracts in 03 §3/§5.1 change only by supervisor decision | **Met** (not auditable from history here) | The contract files exist (`src/Chess.Llm/LlmContracts.cs`, `src/Chess.Api/Contracts/ApiContracts.cs`). |
| Ratings are never stored as the leaderboard; `rating_snapshots` never read into a calculation | **Met** | Snapshots are written (`RatingSnapshotWriter.cs:73`). They are read for the cursor (`RatingSnapshotWriter.cs:22`) and for the history chart (`RatingHistory.cs:14`). None feeds the rating. |
| `Chess.ServiceDefaults` ≤ 400 lines | **Met** | 160 lines (run 11). |
| Every csproj in `Chess.sln`; every web project in the AppHost | **Met** | Run 11. |
| Results is first in the navigation; Big Picture first on the page | **Met** | `src/Chess.Web/Components/Layout/NavMenu.razor:2`; `ResultsPage.razor:47-48`. |
| A broadcast game is an ordinary rated game; pacing only for broadcast slots | **Met** | `BroadcastRunner.cs:215` (`Rated = true`); pacer only through `GameRunHooks` (`GameRunner.cs:189`). Concurrency is a separate matter (M-3). |
| Copilot health check is never Unhealthy | **Met** | `LlmGatewayHealthCheck.cs:14-18`. |

### 3.4 Lessons learned (CLAUDE.md) — spot-checked

| Lesson | Status | Evidence |
|---|---|---|
| Render mode on `Routes` and `HeadOutlet` | **Met** | `src/Chess.Web/Components/App.razor:12, 16`; pinned by `InteractiveRenderModeTests.cs:23-30`. |
| `Chess.Web.styles.css` linked | **Met** | `App.razor:11`; pinned by the same test file. |
| `StateHasChanged` after a background refresh | **Met** | Timer pages: `TournamentsPage`, `TournamentDetailPage`, `PlayPage`, `LiveBroadcastPage` (grep). |
| `/api/status` carries the gateway state | **Met** | Run 5: `"copilot":"Fake"`. |
| Significance rule in one place | **Met** | `src/Chess.Web/Components/Pages/Shared/Significance.cs` exists and is used by Results and head-to-head. |
| CI secret scan reads only `HEAD` | **Met** | `ci.yml:64` (`--log-opts="HEAD"`); run 12. |

## 4. Findings

Ranked by severity. Each finding has a failure scenario, the evidence, and the principle it cites. None is blocking.

### High

**H-1. The loopback design does not stop cross-origin requests. A web page can start a paid broadcast day, and the Blazor websocket accepts a foreign origin.**
- *Failure scenario.* The owner runs the app and visits an unrelated page. That page sends a `POST` with no body and no content type to `http://127.0.0.1:5080/api/broadcast/schedules/{id}/start`. Browsers send such a request without a preflight. Without CORS the page cannot read the reply, but the server still acts. If the page also opens `ws://localhost:5070/_blazor` after `POST /_blazor/negotiate`, it reaches a live Blazor circuit of the owner's UI. From there it could start games or tournaments, which cost Copilot premium requests.
- *Evidence (run 7, fake profile, `Origin: http://evil.example` on every request):*
  - Broadcast plan created, then `POST …/start` → **200**; `GET /api/broadcast/now` → `"status":"running"`; `POST …/stop` → **200**.
  - `POST /_blazor/negotiate` → **200** with a connection token.
  - `GET ws://localhost:5070/_blazor?id=<token>` with the foreign origin → **101 Switching Protocols** (the same request with the owner's origin also gives 101).
- *Not demonstrated:* that a page can drive the Blazor circuit to start a game. The circuit protocol was not exercised. Browser rules (Local Network Access, Private Network Access) may block some of these requests in some browsers; no browser was used for this probe.
- *Why it is High, not Blocking.* The app is local and single-user, and the attacker needs the owner to be on the page while the app runs. The impact is real cost and state change, not data disclosure. The API route needs a schedule GUID, which an outside page cannot read without CORS.
- *Principle.* ADR-006 says "CORS is not configured at all (the browser never calls the API)". That stops reads, not writes or websocket handshakes. The FRONTEND-BFF §1 property (the browser talks only to its own origin) is the one it was meant to protect.
- *Proposed fix (F1 in 06-TASKS).* (a) Reject any request whose `Origin` header is present and not `http://localhost:5070`, or whose `Sec-Fetch-Site` is `cross-site`, on `/api` and `/_blazor`. (b) Require a non-simple header or `Content-Type: application/json` on every `POST` and `DELETE`, so that a cross-origin request needs a preflight that the app never answers. Tests: foreign `Origin` → 403 on a `POST` and on `/_blazor/negotiate`; the owner's origin still succeeds.

### Medium

**M-1. `dotnet test Chess.sln` needs a browser and, without one, the network. The unit job depends on it.**
- *Failure scenario.* On a fresh checkout without Chromium, `dotnet test Chess.sln` reports four E2E failures: "`playwright install chromium` failed with exit code 1". On a network-restricted runner the failure is permanent. CI's `build and test` job runs the same command (`.github/workflows/ci.yml:32`), so if the Playwright download is slow or unavailable, the unit job turns red though no unit test changed.
- *Evidence.* Run 2: 4 failed, 0 passed in `Chess.E2E`, all with that message. Run 3: with a preinstalled browser the same four pass in 17 s. The fallback that downloads at test time is `tests/Chess.E2E/ArenaFixture.cs:116-135`.
- *Principle.* CLAUDE.md, working rules: "Tests need no network". P13: E2E belongs to its own layer.
- *Proposed fix.* Run `Chess.E2E` only in the `e2e` job (`--filter "FullyQualifiedName!~Chess.E2E"` in the `build and test` job). Make the fixture fail with the message rather than download.

**M-2. Documents contradict the code and CI (P14 corollary; REPO-BASELINE §8).**
- `README.md:193`: "`tests/Chess.E2E` currently holds a Playwright harness test only… no CI job runs them." **False.** There are four journeys (run 3), and CI runs them in the `e2e` job (`ci.yml:130`).
- `README.md:175`: "`dotnet test Chess.sln` runs 868 tests: 865 pass and 3 are skipped." **Out of date.** The total is 871: 868 pass and 3 skip, with a browser; 4 fail without one (runs 2 and 3).
- `CLAUDE.md:49`: "`Chess.E2E/` Playwright harness (only the driver smoke test so far)." **False.**
- `README.md:109`: "`Arena:MaxConcurrentGames` (default 2) caps how many games run at once." **Not true with broadcast** (see M-3).
- The GitHub repository description still says "through a Streamlit interface" (run 15).
- *Failure scenario.* A new contributor reads the README, concludes that browser coverage is missing, and writes a parallel harness. Or the owner reads the cost section and believes the cap covers broadcast games.
- *Proposed fix.* Correct the four passages in the same PR as their code change (CLAUDE.md working rule). Owner updates the repository description (action A-14).

**M-3. Broadcast games run outside the Copilot concurrency cap.**
- *Failure scenario.* A broadcast day is running (one game at a time) while a tournament with `MaxConcurrency = 2` plays. Three LLM games are in flight at once. The Copilot request rate is then 1.5 times what the README's cost section describes, and premium requests are spent faster than the planner's estimate assumes.
- *Evidence.* `src/Chess.Api/Broadcast/BroadcastRunner.cs:150` calls `games.PlayToEndAsync` directly. The only cap is the worker pool and `Channel` in `GameRunner` (`GameRunner.cs:66-72`). 03 §9.1 says broadcast is "independent of `Arena:MaxConcurrentGames`", so the design is recorded but its cost consequence is not, and `README.md:109` says the opposite.
- *Principle.* ADR-004 (cost), ADR-011 consequences (cost).
- *Proposed fix (decision for the supervisor).* Recommended: put a broadcast slot behind the same `SemaphoreSlim` as `MaxConcurrentGames`, so the cap covers all LLM games. Alternatively record "the cap is per source" in ADR-011 and the README. Either way the README must match.

**M-4. The kernel has no architecture test (P2).**
- *Failure scenario.* A later change adds `using Chess.Api.Players` or a `DbContext` reference to `Chess.ServiceDefaults`. The 160-line guard still passes (limit 400), the health tests still pass, and the kernel silently becomes a domain library. P2 says this is what the architecture test exists to catch: "Stating the limit in prose has already failed twice in this estate."
- *Evidence.* `tests/Chess.Api.Tests/ServiceDefaultsTests.cs` has no reflection, no assembly scan and no reference to an entity or `DbContext`. The only kernel check is the line count (`ci.yml:66-79`).
- *Proposed fix.* Add a test that loads `Chess.ServiceDefaults` and fails if any exported type derives from `DbContext`, is in a `Chess.*` namespace other than `Chess.ServiceDefaults`, or references `Microsoft.EntityFrameworkCore`.

### Low

**L-1. The `web` resource has no health check in the AppHost.** `src/Chess.AppHost/AppHost.cs:14-18`. Checklist item 1 asks for `WithHttpHealthCheck`. Failure scenario: a future dependency on `web` would `WaitFor` a resource with no readiness signal. Fix: `.WithHttpHealthCheck("/health")`.

**L-2. `GameSlot` never disposes its `CancellationTokenSource`.** `src/Chess.Api/Games/GameRunner.cs:249-251`. Each game holds one unreleased CTS until garbage collection. The CTS has no timer, so the cost is small. Fix: make `GameSlot` `IDisposable` and dispose it in the `finally` blocks at `GameRunner.cs:156-171`.

**L-3. A comment and the defaults disagree about the SSE client's resilience handler.** `src/Chess.Web/Program.cs:12-13` says the standard handler is "for the REST calls only: its total timeout would cut the SSE stream". `src/Chess.ServiceDefaults/Extensions.cs:30-34` (`ConfigureHttpClientDefaults`) applies the handler to every client, including `GameEventsClient`. The stream is not cut in practice: the SSE client sets `Timeout` to infinite (`Program.cs:20`) and reads with `ResponseHeadersRead` (`src/Chess.Web/Api/GameEventsClient.cs:18`). I did not observe a stream longer than 30 s in this review, so the handler's per-request timeouts on the stream are taken from the package documentation, not measured. Failure scenario: a later change that reads the body inside `SendAsync` would be cut at 30 s, silently. Fix: correct the comment, or remove the handler from the SSE client.

**L-4. The Copilot SDK check looks only at project files.** `ci.yml:81-95` greps `*.csproj`. `Chess.Api` gets the SDK types transitively through `ProjectReference` → `Chess.Llm`, so a `using GitHub.Copilot;` in `Chess.Api` would compile and pass CI. Today the only usage is `src/Chess.Llm/CopilotLlmGateway.cs`. Fix: add a grep over `*.cs` outside `src/Chess.Llm`.

**L-5. Template leftovers in `src/Chess.Api/Properties/launchSettings.json`.** It has `launchUrl: "weatherforecast"`, `applicationUrl: http://localhost:5189`, an HTTPS profile and an IIS Express profile. Running the API from its own profile opens a 404 on the wrong port; the AppHost ignores the file. Fix: delete it, or set it to 5080.

**L-6. `.claude/settings.json` does not declare architecture-standards.** REPO-BASELINE §7 and T1.8 ask for it; `grep -c architecture-standards .claude/settings.json` → 0. Fix: add the declaration.

**L-7. `dotnet build` downloads a Copilot CLI from github.com.** The first build fetched `copilot-cli 1.0.103` (about 89 MB) from the release page, then checked it against `SHA256SUMS.txt` from the same host. That detects corruption, not a compromised release. Failure scenario: an offline or firewalled build fails at the restore/build step, with no explanation in the repo. Fix: document the requirement in the README prerequisites, and pin the expected hash if the SDK allows it.

**L-8. Characterisation tests came after the engine move.** G21 in 02-GAP-ANALYSIS says "Engine characterisation (perft) **before** moving it". The board ordered the move in T0.1 and the perft tests in T1.5 (PR #22). The engine's 136 tests now pass, so the retrospective risk is covered. This is a process deviation, recorded in section 5.

**L-9. Package budget drift.** `src/Chess.Api/Chess.Api.csproj:9-12` references `Microsoft.EntityFrameworkCore.Design` (the `dotnet ef` tooling; the budget names EF Core only). `Directory.Packages.props:17` pins `Microsoft.EntityFrameworkCore.Relational`, which no project references. Fix: remove the unused pin; record Design as tooling in the budget table.

**L-10. `deploy-pages` is red on `main`.** The run fails at `actions/configure-pages` (run 14). The workflow header says this is the missing one-time setting "Settings → Pages → Source: GitHub Actions". The log could not be read from this sandbox, so the cause is inferred. Until the owner sets the setting, `main` has a red check that is not a code defect. See action A-15.

## 5. Deviations register (as of 2026-10-24)

| # | Deviation | Principle | Since | Reason and record | Status |
|---|---|---|---|---|---|
| D-1 | Blazor Web App (interactive server) instead of Next.js | FRONTEND-BFF §1 (Next.js) | 2026-10-24 | Owner decision; ADR-005. The property kept is "the browser talks only to its own origin", which H-1 shows is not yet enforced. | Accepted; H-1 open |
| D-2 | No authentication; loopback binding; no CORS | P5 (signing key, JWKS); CI standards | 2026-10-24 | Single-user localhost tool; ADR-006. The loopback binding is verified (run 6). Cross-origin protection is missing (H-1). | Accepted; H-1 open |
| D-3 | No deployment: no Dockerfile, no `fly.toml`, no tag-driven deploy | P6, P7, P12 | 2026-10-24 | R1 (local only); ADR-002. Pages publishes a static page on push to `main`, not an app. | Accepted |
| D-4 | One service (`Chess.Api`) for games, players, tournaments and ratings | P3 (service per bounded context) | 2026-10-24 | Data cohesion; ADR-003. P3 itself allows this for shared hot aggregates. | Accepted |
| D-5 | .NET 10 (`net10.0`, SDK `10.0.100` band) | P6 (runtime major = TFM) | 2026-10-24 | ADR-009; no image, so the coupling has nothing to drift against. | Accepted |
| D-6 | PostgreSQL only; InMemory only in the `fake` profile and tests; no silent fallback when no connection string is set | P4 (fallback to InMemory) | 2026-10-24 | Owner requirement: everything persisted in Postgres (03 §4). `PersistenceExtensions.cs:23` throws instead. | Accepted |
| D-7 | SSE instead of SignalR (no hub, no JS client) | None directly; FRONTEND-BFF | 2026-10-24 | ADR-005 and 03 §6. The game loop is server-side, so the browser never sends moves. | Accepted |
| D-8 | Copilot authentication is the logged-in CLI user, with an optional token parameter | P5 (no secret in source) | 2026-10-24 | ADR-004. The token is an AppHost secret parameter (`AppHost.cs:41-43`). | Accepted |
| D-9 | Stockfish is an optional external binary, not a package | Dependency budget | 2026-10-24 | ADR-008. | Accepted |
| D-10 | Dependency budget additions: `Microsoft.EntityFrameworkCore.Design` (tooling); `Aspire.Hosting.Testing` (E2E) | Dependency budget (03 §2) | 2026-10-24 | Design: `dotnet ef`. Aspire.Hosting.Testing: T4.1 supervisor-approved exception. Both recorded here. | Accepted (L-9 for the unused pin) |
| D-11 | Engine tests written after the engine moved (wave 0), not before | G21, P13 characterisation rule | 2026-10-24 | The board ordered T0.1 before T1.5. Retrospective coverage is complete (136 tests). | Accepted; L-8 |
| D-12 | Broadcast runs outside `MaxConcurrentGames` | Cost control (ADR-004) | 2026-10-24 | Recorded in 03 §9.1 ("independent of `Arena:MaxConcurrentGames`"). Consequence not recorded in README or ADR-011 (M-3). | Open (M-3) |
| D-13 | E2E runs inside the unit job as well as its own job | P13, CLAUDE.md "no network in tests" | 2026-10-24 | Side effect of `dotnet test Chess.sln` (M-1). | Open (M-1) |
| D-14 | Live Copilot smoke and 24 h soak are owner-run or accelerated, not in CI | P13 (E2E in CI) | 2026-10-24 | By design: each move costs a premium request (EVALS.md). | Accepted |

## 6. Alignment actions

| # | Action | Owner | Effort |
|---|---|---|---|
| A-1 | F1: reject cross-origin requests on `/api` and `/_blazor`; require a non-simple header on `POST`/`DELETE`; tests (H-1) | Implementer (follow-up task) | S (half a day) |
| A-2 | Keep `Chess.E2E` out of the `build and test` job; make the fixture fail with the message, not download (M-1) | Implementer (CI) | S |
| A-3 | Correct README:175, README:193, README:109 and CLAUDE.md:49; state the real test total (M-2) | Implementer (docs) | S |
| A-4 | Decide the broadcast/Copilot cap: shared gate, or record per-source cap in ADR-011 and README (M-3) | Supervisor (decision), then implementer | S–M |
| A-5 | Add the kernel architecture test (M-4) | Implementer | S |
| A-6 | `.WithHttpHealthCheck("/health")` on `web` (L-1) | Implementer | XS |
| A-7 | Make `GameSlot` disposable and dispose it (L-2) | Implementer | XS |
| A-8 | Correct the SSE comment, or opt the SSE client out of the handler (L-3) | Implementer | XS |
| A-9 | CI grep for `GitHub.Copilot` in `*.cs` outside `src/Chess.Llm` (L-4) | Implementer (CI) | XS |
| A-10 | Delete or align `src/Chess.Api/Properties/launchSettings.json` (L-5) | Implementer | XS |
| A-11 | Add the architecture-standards declaration to `.claude/settings.json` (L-6) | Implementer | XS |
| A-12 | Document the Copilot CLI download in the README; pin its SHA-256 if possible (L-7) | Implementer (docs/build) | S |
| A-13 | Remove the unused `Microsoft.EntityFrameworkCore.Relational` pin; record Design in the budget table (L-9) | Implementer | XS |
| A-14 | Update the GitHub repository description (it still says Streamlit) | Owner (Konrad) | XS |
| A-15 | Settings → Pages → Source: "GitHub Actions", so `deploy-pages` can publish (L-10) | Owner (Konrad) | XS |
| A-16 | Run the live Copilot smoke (`scripts/smoke-copilot.sh`) and the Docker/PostgreSQL path once on the owner's machine (section 7) | Owner (Konrad) | S |
| A-17 | Run the Stockfish integration tests with a binary (`STOCKFISH_PATH`) (section 7) | Owner or implementer | XS |

## 7. Known limitations

Each limitation was checked before it was listed. Corrections are marked.

- **The PostgreSQL + Docker path has never been run by an agent. True.** `docker` CLI is present, but the daemon is not running (`/var/run/docker.sock` absent). The default profile was not exercised.
- **The real GitHub Copilot path has never been run by an agent. True.** No `copilot` login (`dev-setup.sh` reports it missing). Only the fake gateway ran. The live smoke script is owner-run (`docs/EVALS.md`).
- **Only the fake profile was exercised end to end in a real browser. True.** Four journeys pass in headless Chromium on the fake gateway (run 3). Nothing was run against Copilot.
- **Broadcast "Up next" planned times drift when a game runs longer than `AssumedPlies`. True.** `src/Chess.Api/Broadcast/BroadcastRunner.cs:108-110` starts an overdue slot at once and never recomputes `PlannedStart`. The displayed times are the planned ones, so they drift from reality after a long game.
- **The rating anchor `random` shows rating "—"/0 ± 0. Corrected.** On the Results table, `random` shows rating `0` and interval `0 to 0`. The "—" is the Move quality column, which is empty for `random` (`src/Chess.Web/Components/Pages/Results/ResultsPage.razor`, the table body). The broadcast smoke standings also show `random` at rating 0 (run 8).
- **The UI is functional but visually plain. True.** Screenshot of `/results` (run 5 follow-up): a navigation bar, the status strip, a heading and an empty-state message. No styling beyond the defaults.
- **GitHub Pages needs the manual setting Settings → Pages → Source = GitHub Actions. True.** `deploy-pages` fails at `configure-pages` on `main` (run 14, L-10).
- **The repository description still mentions Streamlit. True.** Run 15.
- **E2E journeys run on the fake gateway only. True.** `tests/Chess.E2E/ArenaFixture.cs` starts the AppHost with `--ARENA_FAKE=1`.
- **Added by this review:**
  - `dotnet test Chess.sln` needs a Chromium or the network (M-1).
  - Broadcast is outside the Copilot concurrency cap (M-3).
  - Stockfish has not run against a real engine. Its three integration tests skip without `STOCKFISH_PATH`.
  - The 24 h soak is accelerated (simulated time). No wall-clock 24 h run has been done.
  - The EVALS mutation runs (4 of 37 and 5 of 37 failures) were not repeated here.
  - The Docker/PostgreSQL and Copilot paths are not covered by any CI job.
  - Not checked: the GitHub Pages workflow's content against its source in architecture-standards; `gitleaks` ran from a pinned binary here, but the pre-commit hook was not run.

## Blocked

None. Both standards documents were fetched. The two NuGet search hosts refused by the agent proxy during the AppHost run did not block any step.

## Resolution status (2026-10-24)

Status after the fix tasks on `main`. "Fixed" cites the pull request that changed the code; "documented" means the docs now match the code and the cited pull request is the one that made the change; "open" and "owner" are stated as they stand.

| Finding | Status | Where |
|---|---|---|
| H-1 cross-origin requests (F1 / A-1) | **Fixed** | #65: `OriginGuard` answers 403 on `/api` and `/_blazor` (ADR-006, amended); tests `OriginGuardTests` in Api and Web |
| M-1 `dotnet test Chess.sln` needs a browser (A-2) | **Fixed** | #64: the `build and test` job excludes `Chess.E2E`; `ArenaFixture` fails fast with the instruction; README documents the commands |
| M-2 documents contradict the code (A-3) | **Documented** | This pull request: README test counts and E2E status, CLAUDE.md layout and invariants |
| M-3 broadcast outside the Copilot cap (A-4) | **Documented** (owner decision: per-source cap) | ADR-011 amended; README cost section; CLAUDE.md lessons. The owner chose to document rather than to share a gate |
| M-4 kernel has no architecture test (A-5) | **Fixed** | #64: `tests/Chess.Api.Tests/KernelArchitectureTests.cs` |
| L-1 `web` has no health check (A-6) | **Fixed** | #64: `WithHttpHealthCheck("/health")` on `web` in `src/Chess.AppHost/AppHost.cs` |
| L-2 `GameSlot` never disposes its CTS (A-7) | **Fixed** | #64: `GameSlot` is `IDisposable` (`src/Chess.Api/Games/GameRunner.cs`) |
| L-3 SSE comment disagrees with the defaults (A-8) | **Fixed** | #65: the comment in `src/Chess.Web/Program.cs` now states the handler and the infinite `Timeout` |
| L-4 Copilot SDK check reads only project files (A-9) | **Fixed** | #64: CI step over `*.cs` outside `Chess.Llm` (`.github/workflows/ci.yml`) |
| L-5 template `launchSettings.json` (A-10) | **Fixed** | #64: the file is deleted |
| L-6 `.claude/settings.json` lacks the architecture-standards declaration (A-11) | **Open** | Not changed in this pull request: the edit to `.claude/settings.json` was refused by the session's permission check. The declaration in REPO-BASELINE §7 still needs to be added |
| L-7 Copilot CLI download undocumented (A-12) | **Documented** | This pull request: README prerequisites (download source, SHA-256 check, internet needed once). The hash is not pinned in the repository |
| L-8 characterisation after the engine move | **Accepted** | Deviation D-11 in section 5; the 136 engine tests cover it now |
| L-9 package budget drift (A-13) | **Open (partly)** | `Microsoft.EntityFrameworkCore.Design` is recorded as build tooling in CLAUDE.md. The `Relational` pin stays: `Directory.Packages.props:5` says Npgsql needs it, which this review did not check |
| L-10 `deploy-pages` red (A-15) | **Owner** | Settings → Pages → Source: GitHub Actions. Not verified from this session |
| A-14 repository description | **Owner** | Not verified from this session |
| A-16, A-17 live Copilot smoke, Docker/PostgreSQL path, Stockfish tests | **Owner** | Unchanged. Their known limitations are listed in section 7 |
| D-12 broadcast outside the cap | **Documented** | ADR-011 amendment (M-3) |
| D-13 E2E inside the unit job | **Fixed** | #64 (M-1) |

**Verified for this section** (this task's sandbox run, 2026-10-25): `dotnet build Chess.sln -warnaserror` succeeded with 0 warnings. `dotnet test Chess.sln` with `E2E_CHROMIUM_PATH` set gave 900 passed and 3 skipped. The unit-only filter gave 896 passed and 3 skipped. Without a browser the four journeys fail with the fixture's message.

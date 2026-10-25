# 06 — Atomic tasks

Each task is one implementation session and one pull request. Read
[04-MIGRATION-PLAN](04-MIGRATION-PLAN.md) §3 (rules) before starting any of them, and
[03-TARGET-ARCHITECTURE](03-TARGET-ARCHITECTURE.md) for every contract referenced here.
Contracts in 03 §3 and §5.1 are **frozen** after T0.2.

Legacy reference: the last state of the old code is commit `f0f5845`; port from it with
`git show f0f5845:<path>`.

## Board

| ID | Title | Depends on | Status |
|---|---|---|---|
| T0.1 | Remove Python, move .NET into `src/`, retarget net10 | — | done |
| T0.2 | Solution skeleton, frozen contracts, manifest `Program.cs` | T0.1 | done |
| T1.1 | ServiceDefaults kernel | T0.2 | done |
| T1.2 | Aspire AppHost | T0.2 | done |
| T1.3 | Persistence: new schema, provider switch, migration hosted service | T0.2 | done |
| T1.4 | Copilot SDK gateway | T0.2 | done |
| T1.5 | Engine characterisation tests (perft) | T0.2 | done |
| T1.6 | Rating library (RATING.md) | T0.2 | done |
| T1.7 | Stockfish analyzer | T0.2 | done |
| T1.8 | Repo baseline + CI + secret scanning | T0.2 | done |
| T1.9 | Chess.Web shell: Blazor Web App, API client, SSE client, board | T0.2 | done |
| T1.10 | Engine: PGN writer + tolerant move notation | T0.2 | done |
| T2.1 | Players: LLM player, random player, prompt, parser | T1.10 | done |
| T2.2 | Game runner + event stream | T1.3 | done |
| T2.3 | Games endpoints + SSE | T2.2 | done |
| T2.4 | Tournaments | T2.2 | done |
| T2.5 | Ratings endpoints | T1.3, T1.6 | done |
| T2.6 | Models + status endpoints, health checks | T1.1, T1.4 | done |
| T2.7 | Attempts (prompt log) endpoint | T1.3 | done |
| T2.8 | Post-game move-quality pipeline | T1.7, T2.2 | done |
| T2.9 | Rating snapshots + results breakdown endpoints | T1.3, T1.6 | done |
| T3.1 | UI: live game page | T1.9, T2.3, T2.6 | done |
| T3.0 | UI: SVG chart components | T1.9 | done |
| T3.2 | UI: Results page — Big Picture + charts | T3.0, T2.5, T2.9 | done |
| T3.3 | UI: head-to-head comparison | T1.9, T2.5 | done |
| T3.4 | UI: tournaments | T1.9, T2.4 | done |
| T3.5 | UI: game archive + replay | T1.9, T2.3 | done |
| T3.6 | UI: attempts log | T1.9, T2.7 | done |
| T4.1 | E2E (Playwright) on the AppHost with the fake gateway | T1.2, T3.1, T3.5 | done |
| T4.2 | AI evals: offline scenario suite + live smoke script | T2.1, T2.2 | done |
| T5.1 | Broadcast domain: contracts, planner, persistence | — | done |
| T5.2 | Broadcast runner: paced sequential play, resume, failure policy | T5.1 | done |
| T5.3 | Broadcast API endpoints | T5.1 | done |
| T5.4 | UI: broadcast planner page | T5.3 | done |
| T5.5 | UI: /broadcast OBS view | T5.3 | done |
| T5.6 | 24h soak test + runbook (OBS → YouTube) | T5.2, T5.3, T5.5 | done |
| T4.3 | README, CLAUDE.md, CHANGELOG final | T5.4, T5.5, T5.6 | done |
| T4.5 | GitHub Pages one-pager + deploy workflow | T4.3 | done |
| T4.4 | Final architecture review | T4.1, T4.2, T4.3, T4.5 | done |

Common *Verify* for every .NET task (abbreviated **V-NET** below):

```bash
dotnet build Chess.sln -warnaserror
dotnet test Chess.sln
```

---

## Wave 0 — sequential

### T0.1 — Remove Python, move .NET into `src/`, retarget net10

**Owns:** whole repository (this is the only task allowed to move files freely).
**Do:**
1. `git rm` the Python app: `src/**/*.py`, `src/__init__.py`, `tests/*.py`, `requirements.txt`,
   root `.env.example`. Remove Python-only entries from `.gitignore` and add the standard
   `dotnet` entries (`bin/`, `obj/`, `*.user`, `.vs/`, `TestResults/`).
2. `git mv chess-dotnet/ChessApi src/Chess.Api`, `git mv chess-dotnet/ChessBlazor src/Chess.Web`;
   rename the `.csproj` files to `Chess.Api.csproj` / `Chess.Web.csproj`; set
   `<RootNamespace>` to keep compiling without touching every namespace (namespaces are renamed
   later by the tasks that own the files).
3. Create `src/Chess.Engine/Chess.Engine.csproj` (class library) and `git mv`
   `src/Chess.Api/Chess/ChessBoard.cs` into it; namespace `Chess.Engine`; `Chess.Api` references it.
4. Add `global.json` (SDK 10.0.x, `rollForward: latestFeature`), `Directory.Build.props`
   (`net10.0`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors` false for now,
   `LangVersion latest`), `Directory.Packages.props` with every existing package moved to
   10.x versions, `.editorconfig` (standard .NET rules, 4-space C#, 2-space json/yml/razor),
   `Chess.sln` containing the three projects.
5. Delete `chess-dotnet/docker-compose.yml`, `chess-dotnet/.env.example`, both `Dockerfile`s,
   `nginx.conf`, `chess-dotnet/.gitignore`, and the now empty `chess-dotnet/`. Remove the
   committed password from `appsettings.json` (connection string becomes empty; Aspire supplies it later).
**Done when:** `dotnet build Chess.sln` succeeds on net10.0; no `.py` file and no
`chess-dotnet/` remain; `git log --follow src/Chess.Engine/ChessBoard.cs` shows the old history.
**Verify:** `dotnet build Chess.sln`; `git ls-files | grep -E '\.py$|chess-dotnet' | wc -l` → `0`;
`grep -rn changeme src | wc -l` → `0`.

### T0.2 — Solution skeleton, frozen contracts, manifest `Program.cs`

**Owns:** whole repository.
**Do:**
1. Create projects and add them to `Chess.sln`: `src/Chess.ServiceDefaults` (class library,
   `IsAspireSharedProject`), `src/Chess.AppHost` (Aspire AppHost SDK, latest stable 13.x),
   `src/Chess.Llm`, `src/Chess.Analysis`, `src/Chess.Rating` (class libraries);
   `tests/Chess.Engine.Tests`, `tests/Chess.Rating.Tests`, `tests/Chess.Llm.Tests`,
   `tests/Chess.Api.Tests`, `tests/Chess.Web.Tests`, `tests/Chess.E2E` (xUnit). Project
   references exactly as in 03 §2.
2. Pin **every** package of the dependency budget (03 §2) in `Directory.Packages.props`:
   `GitHub.Copilot.SDK` 1.0.26, EF Core 10 + Npgsql provider 10 + InMemory 10,
   OpenTelemetry (exporter OTLP, extensions hosting, instrumentation AspNetCore/Http/Runtime),
   `Microsoft.Extensions.ServiceDiscovery`, `Microsoft.Extensions.Http.Resilience`,
   `Aspire.Hosting.AppHost`, `Aspire.Hosting.PostgreSQL`, xUnit, `Microsoft.NET.Test.Sdk`,
   `Microsoft.AspNetCore.Mvc.Testing`, `bunit`, `Microsoft.Playwright`.
   Remove `OpenAI`, `Swashbuckle.AspNetCore`, `Microsoft.AspNetCore.SignalR.Client`.
3. Write every contract from 03 §3.1–§3.5 and §5.1 **verbatim** into these files:
   `src/Chess.Llm/LlmContracts.cs`, `src/Chess.Api/Players/PlayerContracts.cs`,
   `src/Chess.Api/Games/GameContracts.cs`, `src/Chess.Rating/RatingContracts.cs` (records +
   `RatingCalculator` with methods throwing `NotImplementedException`),
   `src/Chess.Analysis/AnalysisContracts.cs`, `src/Chess.Api/Contracts/ApiContracts.cs`.
4. Add `src/Chess.Llm/FakeLlmGateway.cs` — the minimal deterministic fake from 03 §3.1
   (seeded RNG over the `Legal moves:` line; `Status = Fake`; `ListModelsAsync` returns
   `fake-a`, `fake-b`). T1.4 owns it afterwards.
5. Delete the old surfaces: `Controllers/`, `Hubs/`, `Services/`, `Agents/`, `Config/`,
   `DTOs/`, `Game/ChessGameSession.cs`, `ChessApi.http`. Keep `Entities/`, `Data/`,
   `Migrations/` (T1.3 replaces them).
6. Replace `src/Chess.Api/Program.cs` with exactly:
   ```csharp
   var builder = WebApplication.CreateBuilder(args);
   builder.AddServiceDefaults();
   builder.AddArenaPersistence();
   builder.AddLlmGateway();
   builder.AddMoveQualityAnalysis();
   builder.AddPlayers();
   builder.AddGameRunner();
   builder.AddTournaments();
   builder.AddRatings();
   builder.AddResults();
   builder.AddCatalog();
   builder.AddAttempts();
   builder.AddMoveQualityPipeline();

   var app = builder.Build();
   app.MapDefaultEndpoints();
   var api = app.MapGroup("/api");
   api.MapCatalogEndpoints();
   api.MapGameEndpoints();
   api.MapTournamentEndpoints();
   api.MapRatingEndpoints();
   api.MapResultEndpoints();
   api.MapAttemptEndpoints();
   app.Run();

   public partial class Program;
   ```
   and create each extension as an empty stub in its own file — `Add…` returns the builder,
   `Map…` returns the route builder:
   `Chess.ServiceDefaults/Extensions.cs` (`AddServiceDefaults`, `MapDefaultEndpoints` — map a
   plain `/health` and `/alive` for now), `Chess.Api/Persistence/PersistenceExtensions.cs`,
   `Chess.Llm/LlmServiceCollectionExtensions.cs` (registers `FakeLlmGateway` as `ILlmGateway`),
   `Chess.Analysis/AnalysisServiceCollectionExtensions.cs`, `Chess.Api/Players/PlayersExtensions.cs`,
   `Chess.Api/Games/GameRunnerExtensions.cs`, `Chess.Api/Games/GameEndpoints.cs`,
   `Chess.Api/Tournaments/TournamentExtensions.cs`, `Chess.Api/Ratings/RatingExtensions.cs`,
   `Chess.Api/Results/ResultsExtensions.cs`,
   `Chess.Api/Catalog/CatalogExtensions.cs`, `Chess.Api/Attempts/AttemptExtensions.cs`,
   `Chess.Api/Quality/QualityPipelineExtensions.cs`.
   `appsettings.json`: Kestrel endpoint `http://127.0.0.1:5080`; sections `Arena`
   (`MaxConcurrentGames: 2`, `MaxAttemptsPerMove: 3`, `MaxPlies: 200`, `MoveTimeoutSeconds: 60`),
   `Llm` (`Provider: "Copilot"`), `Database` (`Provider: "PostgreSQL"`), `Analysis`
   (`StockfishPath: ""`, `Depth: 12`).
7. `src/Chess.AppHost/AppHost.cs`: empty builder that adds `Chess.Api` and `Chess.Web` projects
   (T1.2 completes it).
8. Add `CLAUDE.md` at the root: what the product is (one paragraph), build/test commands,
   the layout of 03 §2, the dependency budget, and these invariants: *Copilot SDK types never
   leave `Chess.Llm`*; *`Chess.Rating` is pure*; *no fallback move — illegal replies forfeit*;
   *contracts in 03 §3/§5.1 change only by supervisor decision*; *ratings are never stored*.
9. One real test per test project that exercises something existing (e.g. Engine: the start
   position has 20 legal moves; Api: `WebApplicationFactory<Program>` with
   `Database:Provider=InMemory` answers `/alive` 200).
**Done when:** V-NET passes; `Program.cs` is exactly the manifest above; every contract file
compiles; no `OpenAI`/`SignalR`/`Swashbuckle` reference remains.
**Verify:** V-NET; `grep -rn "OpenAI\|SignalR\|Swashbuckle" src tests Directory.Packages.props | wc -l` → `0`.

---

## Wave 1 — parallel after T0.2

### T1.1 — ServiceDefaults kernel

**Owns:** `src/Chess.ServiceDefaults/**`, `tests/Chess.Api.Tests/ServiceDefaultsTests.cs`.
**Do:** `AddServiceDefaults()` = OpenTelemetry (logging, metrics: AspNetCore/Http/Runtime;
tracing: AspNetCore/Http + `ActivitySource("Chess.*")` wildcard sources; OTLP exporter only
when `OTEL_EXPORTER_OTLP_ENDPOINT` is set; health requests filtered from traces), default
health checks (`self` tagged `live`), service discovery, `ConfigureHttpClientDefaults` with
`AddStandardResilienceHandler` + `AddServiceDiscovery`, `AddProblemDetails`.
`MapDefaultEndpoints()` = `/health` (all checks, JSON writer listing every check name, status
and description) and `/alive` (`live` tag only). A startup log line
`Integrations: <name>=<status>, …` printed from the registered health checks once the app has
started (`IHostApplicationLifetime.ApplicationStarted`).
Size guard: the project must stay ≤ 400 lines and contain no type from any `Chess.*` project.
**Done when:** `/health` JSON lists each registered check; `/alive` ignores non-`live` checks
(test registers a failing non-live check: `/alive` 200, `/health` 503).
**Verify:** V-NET.

### T1.2 — Aspire AppHost

**Owns:** `src/Chess.AppHost/**`.
**Do:** Postgres container (`AddPostgres("postgres").WithDataVolume("chess-arena-pg")`),
database `chess`; secret parameter `postgres-password` and optional secret parameter
`copilot-github-token`; `Chess.Api` project named `api` with `WithReference(db)`, `WaitFor(db)`,
`WithHttpHealthCheck("/health")`, a fixed HTTP endpoint on port 5080 (`IsProxied = false`),
env `Copilot__GitHubToken` from the parameter only when set; `Chess.Web` named `web` with
`WithReference(api)`, `WaitFor(api)`, fixed port 5070, `WithExternalHttpEndpoints()`.
If the connection string `chess` is configured on the AppHost (user secrets), use
`AddConnectionString("chess")` instead of the container (existing Postgres). An `--fake` launch profile (or `ARENA_FAKE=1` env) that sets `Llm__Provider=Fake` and
`Database__Provider=InMemory` on `api` and omits Postgres — used by E2E and by owners without
Docker.
**Done when:** `dotnet run --project src/Chess.AppHost` starts the three resources locally; the
fake profile starts without Docker.
**Verify:** V-NET; `dotnet run --project src/Chess.AppHost --launch-profile fake` then
`curl -sf http://127.0.0.1:5080/alive` (record the output in the PR; skip only if the sandbox
forbids long-running processes, and say so).

### T1.3 — Persistence: new schema, provider switch, migration hosted service

**Owns:** `src/Chess.Api/Persistence/**`; deletes `src/Chess.Api/{Entities,Data,Migrations}/**`;
`tests/Chess.Api.Tests/Persistence/**`.
**Do:** Entities and `ArenaDbContext` for the five tables of 03 §4 (snake_case names, `jsonb`
for `tournaments.models` with a value converter for InMemory). `AddArenaPersistence()`:
`Database:Provider` = `PostgreSQL` (connection string `ConnectionStrings:chess`, from Aspire;
`EnableRetryOnFailure(10, 30s)`, command timeout 60 s) or `InMemory`; register
`IDbContextFactory<ArenaDbContext>`. `MigrationHostedService` runs `MigrateAsync` after start for
relational providers (`EnsureCreated` only for InMemory), reports a `database` health check
(`Degraded` while migrating, `Healthy` after, `Unhealthy` with the exception message on failure).
Delete the two old migrations and generate a fresh `InitialCreate` with
`dotnet ef migrations add InitialCreate -o Persistence/Migrations`. On start, after migration,
mark games with `started_at != null && finished_at == null` as `result="*"`,
`termination="interrupted"`, `finished_at=now` (03 §7).
**Done when:** round-trip test for each entity on InMemory; the interrupted-game sweep has a
test; `dotnet ef migrations script` produces the five tables.
**Verify:** V-NET; `dotnet tool restore && dotnet ef migrations script -p src/Chess.Api | grep -c "CREATE TABLE"` → `5` (add a `.config/dotnet-tools.json` with `dotnet-ef` inside the owned task if missing — allowed exception).

### T1.4 — Copilot SDK gateway

**Owns:** `src/Chess.Llm/**`, `tests/Chess.Llm.Tests/**`.
**Do:**
1. **Spike first (≤ 30 min):** against `GitHub.Copilot.SDK` 1.0.26 confirm: client start/stop,
   logged-in-user auth, listing models (and whether a premium multiplier is exposed), creating a
   session with a model + system message + no tools + reject-all permission handler, sending one
   prompt and awaiting the final assistant message (`SessionIdleEvent`), token usage if exposed.
   Write the findings as `src/Chess.Llm/README.md` (exact types/members used).
2. `CopilotLlmGateway : ILlmGateway` + `CopilotClientHostedService` exactly per 03 §3.1.
   `AskAsync` enforces `Timeout` with a linked CTS and throws `TimeoutException`; records an
   `Activity` named `chat {model}` from `ActivitySource("Chess.Llm")` with `gen_ai.request.model`,
   `gen_ai.usage.input_tokens`/`output_tokens` when known. Never logs prompts at Information level.
3. `AddLlmGateway()`: `Llm:Provider` = `Copilot` (default) | `Fake`. Also registers a health
   check `copilot` mapping `LlmGatewayState` → Healthy (`Ready`, `Fake`) / Degraded
   (`NotAuthenticated`, `Unavailable`) with `Detail` as description — never Unhealthy (P8).
4. Harden `FakeLlmGateway`: scriptable replies per model (`Queue<string>`), seed option,
   simulated latency option.
**Done when:** no `GitHub.Copilot` type appears in any public signature
(test via reflection over the assembly's exported types); the fake is deterministic for a seed;
the gateway degrades to `Unavailable` (not an exception at startup) when the runtime cannot be
started (test by configuring an invalid CLI path / URI).
**Verify:** V-NET. Live check is the owner's (T4.2 smoke script).

### T1.5 — Engine characterisation tests (perft)

**Owns:** `tests/Chess.Engine.Tests/**` except `PgnTests.cs`, `MoveNotationTests.cs`;
may fix bugs in `src/Chess.Engine/ChessBoard.cs` (only that file).
**Do:** perft node counts (depth 1–3, depth 4 where under 2 s) for the standard positions
"initial", "Kiwipete", positions 3–6 of the Chess Programming Wiki perft page; FEN round-trip
for 20 FENs; SAN generation incl. disambiguation, castling, promotion, check/mate suffixes;
status detection for checkmate, stalemate, insufficient material, fifty-move, threefold.
Fix every failure in `ChessBoard.cs` with a regression test; list each fixed bug in the PR.
**Done when:** all perft counts match the published numbers.
**Verify:** V-NET.

### T1.6 — Rating library

**Owns:** `src/Chess.Rating/**` (except signatures in `RatingContracts.cs`), `tests/Chess.Rating.Tests/**`.
**Do:** implement `docs/RATING.md` §3–§6 exactly: penalised Bradley–Terry fit with White
advantage (Newton–Raphson; stop at max |Δ| < 1e-6 or 10 000 iterations), anchoring, legality,
move quality, quality, RQC, Elo-equivalent, arena score, seeded bootstrap, provisional flag,
head-to-head record. Pure: no I/O, no `DateTime.Now`, no static mutable state; `Random` only
via `new Random(options.Seed)`.
**Done when:** test vectors V1–V9 of RATING.md §7 pass, plus: empty input → empty report;
a model with games only as White still gets a finite rating; 200 models × 5 000 games computes
in < 2 s without bootstrap.
**Verify:** V-NET.

### T1.7 — Stockfish analyzer

**Owns:** `src/Chess.Analysis/**` (except `AnalysisContracts.cs` signatures), `tests/Chess.Api.Tests/Analysis/**`.
**Do:** `StockfishAnalyzer` speaking UCI to the process at `Analysis:StockfishPath`
(or `stockfish` resolved on `PATH`): `uci`/`isready`, `position fen … moves …` (convert SAN to
UCI via `Chess.Engine`), `go depth {Depth}`, parse `info … score cp|mate` and `bestmove`;
centipawn loss per RATING.md §4 (cap 1000, mate ±1000); one process per analyzer, serialised
with a `SemaphoreSlim`, restarted if it dies. `NoOpMoveQualityAnalyzer` when no binary.
`AddMoveQualityAnalysis()` picks one and registers health check `stockfish`
(`Healthy` "available" / `Healthy` "absent — move quality disabled").
**Done when:** UCI parsing is unit-tested against recorded transcripts (fixtures as text files);
an integration test runs only when `STOCKFISH_PATH` is set (`[SkippableFact]` style via a
custom `FactAttribute` that sets `Skip` — no new package).
**Verify:** V-NET.

### T1.8 — Repo baseline + CI + secret scanning

**Owns:** `.github/**`, `.gitleaks.toml`, `.pre-commit-config.yaml`, `scripts/**`, `.claude/**`,
`.gitattributes`, `CODEOWNERS` (inside `.github/`).
**Do:** `.github/workflows/ci.yml` on `pull_request` and `push` to `main`: setup .NET 10, cache
NuGet, V-NET, upload test results; job `secrets`: gitleaks on the full history; job
`kernel-size`: fail if `src/Chess.ServiceDefaults` exceeds 400 lines of `.cs`; job `contracts`:
fail if any project references `GitHub.Copilot.SDK` other than `Chess.Llm` (grep the csproj files);
job `solution`: fail if any `*.csproj` under `src/` or `tests/` is missing from `dotnet sln Chess.sln list`,
or if a `Microsoft.NET.Sdk.Web` project under `src/` is not referenced from `src/Chess.AppHost/AppHost.cs`.
Dependabot (nuget, github-actions, weekly). `CODEOWNERS` (`* @konradcinkusz`).
PR template mirroring 04 §3 rule 7 (acceptance checklist, verification output, `## Blocked`).
Real `.gitattributes` (`* text=auto`, `*.sh eol=lf`). `scripts/dev-setup.sh` (checks .NET 10,
Docker, `copilot` CLI login status; prints what is missing; never installs silently) and
`scripts/setup-hooks.sh` (pre-commit gitleaks; refuses when gitleaks is absent).
`.claude/settings.json` declaring architecture-standards per REPO-BASELINE §7.
**Done when:** CI is green on the PR itself; a test commit containing a fake key pattern is
caught by the gitleaks job (show the failing run, then drop the commit).
**Verify:** `actionlint` if available; CI run link in the PR.

### T1.9 — Chess.Web shell

**Owns:** `src/Chess.Web/**`, `tests/Chess.Web.Tests/**`.
**Do:**
1. Convert the WASM project into a Blazor Web App (`Microsoft.NET.Sdk.Web`, interactive server
   render mode, `AddRazorComponents().AddInteractiveServerComponents()`), calling
   `AddServiceDefaults()` / `MapDefaultEndpoints()`; listens on `http://127.0.0.1:5070`.
   Delete `wwwroot/index.html`, `sample-data/`, `Counter`, `Weather`, `Home`, the old
   `Services/`, `Models/`, and the old pages (they are ported in wave 3 from `f0f5845`).
2. `MainLayout` + `NavMenu` with links to every route of 03 §6, and one placeholder page per
   route, each in its own folder: `Components/Pages/{Results,Play,HeadToHead,Tournaments,Games,Admin}/` and an empty
   `Components/Charts/` (wave 3 tasks own those folders). `Results` is the first navigation item. A status strip in the layout showing `GET /api/status`.
3. `Api/ApiContracts.cs` — copies of 03 §5.1 records (and of `ModelRating`,
   `PairwiseCoefficient` as local records). `Api/ArenaApiClient.cs` — typed client with one
   method per row of 03 §5 (base address `http://api`, service discovery). `Api/GameEventsClient.cs`
   — SSE reader per 03 §5.1 returning `IAsyncEnumerable<GameEventMessage>(string Type, JsonElement Data)`.
4. `Components/Shared/ChessBoardSvg.razor` — pure component: `Fen`, optional `LastMove`
   (from/to squares), `Flipped`; renders an 8×8 SVG with Unicode pieces, coordinates, last-move
   highlight. No JS.
**Done when:** bUnit tests: board renders 32 pieces for the start FEN and highlights a given
square; SSE parser handles multi-line `data:`, comments and `event:` names (fixture text);
DTO round-trip: serialise each `Chess.Api.Contracts` record with the API's JSON options and
deserialise into the web copy without loss (the test project may reference `Chess.Api` for
this — the web project may not).
**Verify:** V-NET.

### T1.10 — Engine: PGN writer + tolerant move notation

**Owns:** `src/Chess.Engine/Pgn.cs`, `src/Chess.Engine/MoveNotation.cs`,
`tests/Chess.Engine.Tests/PgnTests.cs`, `tests/Chess.Engine.Tests/MoveNotationTests.cs`.
Must **not** edit `ChessBoard.cs` (use its public API).
**Do:** `Pgn.Write(tags, startFen, sanMoves, result)` (Seven Tag Roster + `FEN`/`SetUp` when not
the start position, 80-column wrapping). `MoveNotation.TryResolve(ChessBoard board, string text,
out string san)`: accepts exact SAN, SAN without `+`/`#`/`!?`, castling written `0-0`/`O-O-O`,
UCI (`e2e4`, `e7e8q`), and a leading move number (`12. Nf3`, `12... Nf3`); trims quotes,
backticks and trailing punctuation; returns false for ambiguous or illegal text.
`MoveNotation.ToUci(board, san)` for the analyzer.
**Done when:** ≥ 25 table-driven cases for `TryResolve` incl. 6 negative ones; PGN of a known
game matches a fixture byte for byte.
**Verify:** V-NET.

---

## Wave 2 — backend features

### T2.1 — Players

**Owns:** `src/Chess.Api/Players/**` (except `PlayerContracts.cs`), `tests/Chess.Api.Tests/Players/**`.
**Do:** `RatedPromptBuilder` (pure; one fixed template: side to move, FEN, last 10 moves in SAN,
legal moves list, "answer with one move in SAN only"); system prompt constant (`"You are playing
a game of chess…"`). `LlmChessPlayer`: holds one `ILlmConversation` per game side; on each move
asks; resolves the reply with `MoveNotation.TryResolve`; on failure sends a corrective follow-up
in the same conversation (`"'{reply}' is not a legal move. Legal moves: … Answer with one move."`)
until `Arena:MaxAttemptsPerMove`; timeouts and exceptions count as attempts; returns
`PlayerMove(Forfeit: true)` when exhausted. Every attempt is reported in `Attempts`.
`RandomChessPlayer` (seedable). `ChessPlayerFactory` (`llm` → gateway conversation with
`Arena:MoveTimeoutSeconds`; `random`); throws `LlmUnavailableException` when the gateway is not
`Ready`/`Fake`. `AddPlayers()` registers them.
**Done when:** with `FakeLlmGateway` scripted `["garbage", "e4"]` the move is `e4` with 2 attempts;
scripted 3 illegal replies → forfeit with 3 attempts; prompt builder snapshot test.
**Verify:** V-NET.

### T2.2 — Game runner + event stream

**Owns:** `src/Chess.Api/Games/**` except `GameContracts.cs` and `GameEndpoints.cs`;
`tests/Chess.Api.Tests/Games/**`.
**Do:** `InMemoryGameEventStream` (per-game `Channel` fan-out, completes subscribers after
`GameFinished`). `GameRunner : BackgroundService, IGameRunner` — bounded `Channel<Guid>`,
`Arena:MaxConcurrentGames` workers; per game: load, create both players, loop: publish
`MoveStarted` → `ChooseMoveAsync` → persist `move_attempts` → on forfeit finish
`forfeit-white|black` → apply move, persist `moves` row + game `current_fen`/`ply_count`
(one `SaveChanges` per ply), publish `MovePlayed`; termination from the engine status; at
`Arena:MaxPlies` adjudicate per RATING.md §2 using `IMoveQualityAnalyzer.EvaluateAsync`, else
material. On finish write `result`, `termination`, `pgn` (`Pgn.Write`), `finished_at`; publish
`GameFinished`; dispose players. `TryAbort` cancels the game's CTS → `aborted`, `*`.
A failing game never stops the worker (log + finish as `aborted`).
**Done when:** with two `random` players and InMemory DB a game reaches a terminal state and
persisted rows match events; forfeit path; abort path; concurrency cap respected (test with
slow fake players); max-plies adjudication with a stub analyzer.
**Verify:** V-NET.

### T2.3 — Games endpoints + SSE

**Owns:** `src/Chess.Api/Games/GameEndpoints.cs`, `tests/Chess.Api.Tests/Endpoints/GameEndpointsTests.cs`.
**Do:** the `/api/games…` rows of 03 §5 with the DTOs of §5.1. `POST` validates kinds/models
against `ILlmGateway.ListModelsAsync` (`random` always valid) → 400 problem; gateway not ready
for an `llm` side → 503 problem with "run `copilot` and log in"; inserts the game, enqueues it,
201 + `Location`. SSE per §5.1 (`text/event-stream`, no buffering, heartbeat, closes after
`GameFinished`). PGN as `application/x-chess-pgn` with a filename. `DELETE` → 204 / 404.
**Done when:** `WebApplicationFactory` tests (InMemory + Fake gateway) for each route incl.
validation errors; SSE test reads events until `GameFinished` for a random-vs-random game.
**Verify:** V-NET.

### T2.4 — Tournaments

**Owns:** `src/Chess.Api/Tournaments/**`, `tests/Chess.Api.Tests/Tournaments/**`.
**Do:** `POST /api/tournaments` creates a round robin: every unordered pair of `Models`,
`GamesPerPair` games, colours alternating (must be even; 400 otherwise), all `rated = true`;
creates the games up front with `tournament_id` and enqueues them respecting the tournament's
`MaxConcurrency` (≤ `Arena:MaxConcurrentGames`). `GET` list/detail with progress
(`GamesFinished/GamesTotal`); status → `finished` when all games are terminal; `DELETE` aborts
the queued/running games and sets `cancelled`. Response includes
`EstimatedPremiumRequests = pairs × games × 80 × multiplier` per model when known.
**Done when:** 3 models × 2 games → 6 games, colour-balanced; cancel path; progress updates.
**Verify:** V-NET.

### T2.5 — Ratings endpoints

**Owns:** `src/Chess.Api/Ratings/**`, `tests/Chess.Api.Tests/Ratings/**`.
**Do:** read finished, rated games (`result != "*"`) → `GameOutcome`s (model id per side;
`random` for random players); per model move stats from `moves`/`move_attempts`
(first-attempt-legal = moves with `attempts == 1`; ACPL from `centipawn_loss` when every move
of the model has one, else null); call `RatingCalculator`; expose `/api/ratings`,
`/api/ratings/pairwise?a&b` (404 for unknown model), `/api/ratings/matrix`. Optional filters
`?tournamentId=`. No caching.
**Done when:** seeded DB fixture → leaderboard equals a direct `RatingCalculator` call on the
same outcomes; unrated and interrupted games are excluded (tests).
**Verify:** V-NET.

### T2.6 — Models + status endpoints, health

**Owns:** `src/Chess.Api/Catalog/**`, `tests/Chess.Api.Tests/Catalog/**`.
**Do:** `GET /api/models` → gateway models + a synthetic `random` entry; cached 60 s in
memory; when the gateway is not ready returns only `random` plus a `Warning` header.
`GET /api/status` → `StatusDto` from the health check results (`copilot`, `database`,
`stockfish`).
**Done when:** tests with the fake gateway in `Fake` and a stub in `NotAuthenticated` state.
**Verify:** V-NET.

### T2.7 — Attempts endpoint

**Owns:** `src/Chess.Api/Attempts/**`, `tests/Chess.Api.Tests/Attempts/**`.
**Do:** `GET /api/attempts?gameId&model&legal&page&pageSize` → `PageDto<AttemptDto>`, newest
first, `pageSize` ≤ 200.
**Done when:** filter and paging tests.
**Verify:** V-NET.

### T2.8 — Post-game move-quality pipeline

**Owns:** `src/Chess.Api/Quality/**`, `tests/Chess.Api.Tests/Quality/**`.
**Do:** a `BackgroundService` that polls every 30 s for finished games whose moves lack
`centipawn_loss` (no change to `IGameEventStream`); run `IMoveQualityAnalyzer.AnalyzeAsync`; write
`centipawn_loss`, `best_san` per move. Skipped entirely when the analyzer is unavailable.
Background, one game at a time.
**Done when:** with a stub analyzer, a finished game gets every move annotated; no-op analyzer
leaves rows untouched.
**Verify:** V-NET.

### T2.9 — Rating snapshots + results breakdown

**Owns:** `src/Chess.Api/Results/**` (incl. the `AddResults` / `MapResultEndpoints` stubs
from T0.2), `tests/Chess.Api.Tests/Results/**`.
**Do:** `RatingSnapshotService : BackgroundService` — every 15 s, finds finished rated games
newer than the newest `rating_snapshots.after_game_id`; for each, in finish order, computes
the report over all rated games up to and including it (`BootstrapSamples` 200) and appends
one row per model (`formula_version = "1"`). Idempotent across restarts. Endpoints
`GET /api/ratings/history` and `GET /api/results/breakdown` per 03 §5 / §5.1.
**Done when:** 3 finished games → 3 snapshot batches; restart does not duplicate; breakdown
counts W/D/L/forfeits correctly from a fixture.
**Verify:** V-NET.

---

## Wave 3 — Blazor pages (each task owns one folder under `src/Chess.Web/Components/Pages/`)

Port visual pieces from `f0f5845:chess-dotnet/ChessBlazor/` where they fit. Each page gets
bUnit tests with a stubbed `ArenaApiClient` (via an interface extracted in the page's own
folder if needed — do not edit `Api/`; if the client lacks something, `## Blocked`).

### T3.1 — Live game page (`Pages/Play/`, route `/`)
Model pickers for White/Black (from `/api/models`, multiplier shown), `rated` toggle, start;
board (`ChessBoardSvg`) updated from the SSE stream, move list, attempt/forfeit notices
(`AttemptRejected` shown inline), thinking indicator with model name, abort, PGN link.
Gateway not ready → banner with login instructions, only `random` selectable.
**Done when:** bUnit: events update board and move list; ply de-duplication per 03 §5.1.

### T3.0 — SVG chart components (`Components/Charts/`)
Pure Razor components, no JS, each with bUnit tests on the generated SVG: `RankedBarChart`
(horizontal, sorted, whiskers, highlighted item, hatched flag, direct labels), `LineChart`
(x numeric, multiple series, end-of-line labels, legend), `Heatmap` (diverging scale with a
neutral midpoint value, cell labels, muted cells), `StackedBar100` (horizontal, 2 px gaps),
`BarChart` (single series), `ScatterPlot` (labelled points). Shared `ChartPalette` with the
light/dark colours of 03 §6.1 as CSS custom properties, `ChartTheme.css`, an SVG `<title>` per
mark for hover tooltips, and a `ChartTable` fallback. Follow every chart rule in 03 §6.1.

### T3.2 — Results page (`Pages/Results/`, `/results`)
Exactly the seven sections of 03 §6.1, in that order, using T3.0 components and
`/api/ratings`, `/api/ratings/pairwise` (leader vs #2), `/api/ratings/matrix`,
`/api/ratings/history`, `/api/results/breakdown`. The Big Picture headline and chart must be
visible without scrolling at 1366×768. Empty state (no rated games): explain and link to
`/tournaments`. Tournament filter in one row above the Big Picture.
**Done when:** bUnit: section order; headline names the top model; "not yet significantly
ahead" when the RQC interval contains 0.5; empty state.

### T3.3 — Head-to-head (`Pages/HeadToHead/`, `/head-to-head`)
Two pickers → RQC with interval, verbal reading per RATING.md §5 (incl. *not significant*),
expected score, quality share, Elo-equivalent, direct record and the list of their direct games.

### T3.4 — Tournaments (`Pages/Tournaments/`, `/tournaments`, `/tournaments/{id}`)
Create form (multi-select models, games per pair, concurrency) with the premium-request
estimate shown **before** confirming; list with progress bars; detail with its games and a
leaderboard filtered to `tournamentId`; cancel.

### T3.5 — Archive + replay (`Pages/Games/`, `/games`, `/games/{id}`)
Paged, filterable list; replay with first/prev/next/last stepping over `FenAfter`, keyboard
arrows, per-move details (attempts, latency, CPL, best move), PGN download.

### T3.6 — Attempts log (`Pages/Admin/`, `/admin/attempts`)
Port of the old Prompts/Conversations pages onto `/api/attempts`: filters by game, model,
legal; expandable prompt/reply.

**Verify (all of wave 3):** V-NET.

---

## Wave 4 — quality and closure

### T4.1 — E2E
**Owns:** `tests/Chess.E2E/**`, the `e2e` job appended to `.github/workflows/ci.yml` (only that job).
Start the AppHost with the fake profile via `Aspire.Hosting.Testing`
(`DistributedApplicationTestingBuilder`) — allowed new package, pin it in the same PR
(supervisor-approved exception). Journeys: start a random-vs-fake game on `/` and see it finish;
open it in `/games/{id}` and step to the last move; `/results` shows the Big Picture headline
and chart first, with both players in it. Web-first
assertions only, `data-testid` locators (add them in pages only if missing — `## Blocked` instead
of editing pages).
**Verify:** `dotnet test tests/Chess.E2E` (Playwright Chromium is preinstalled in cloud sessions).

### T4.2 — AI evals
**Owns:** `tests/Chess.Api.Tests/Evals/**`, `scripts/smoke-copilot.sh`, `docs/EVALS.md`.
Offline scenario suite (AI-EVALS): 30 positions as data (FEN + set of acceptable replies,
including adversarial replies: prose around the move, wrong case, UCI, illegal, empty) run
through `LlmChessPlayer` with the scripted fake; asserts resolution/forfeit outcomes. Prove the
suite can fail (one deliberately broken parser run, documented). `smoke-copilot.sh` (owner-run):
starts the API with the real gateway, plays one rated `random` vs `<model>` game capped at 20
plies, prints result, attempts and latency.

### T4.3 — Docs final
**Owns:** `README.md`, `CLAUDE.md`, `CHANGELOG.md`, `CONTRIBUTING.md`, `docs/**` except
`docs/architecture/0*-*.md`.
README: what it is, prerequisites (.NET 10, Docker, Copilot CLI logged in, optional Stockfish),
`dotnet run --project src/Chess.AppHost`, the fake profile, how ratings work (link RATING.md),
the Results page, **Broadcast mode (planning a 24 h channel, `/broadcast` as an OBS Browser Source, link to docs/BROADCAST.md)**, cost warning, troubleshooting by `/health` state, project layout, link to
`docs/architecture/`. Accurate against the code — every command run.
Follow the architecture-standards guides (attach or fetch
`konradcinkusz/architecture-standards`): **README-BADGES** (`docs/guides/README-BADGES.md` —
header metadata row and footer block, every badge a titled link, owner/repo substituted from
`git remote`, never badge what the repo does not have: CI badge for `ci.yml`, license, .NET 10)
and **REPO-BASELINE** §8 (documentation staleness: no claim the code does not back). Remove the
GitHub repository description's Streamlit wording from the README and state the new
description at the top of the PR so the supervisor can update the repo settings.

### T4.5 — GitHub Pages one-pager
**Owns:** `docs/index.html`, `docs/assets/**`, `.github/workflows/deploy-pages.yml`.
Follow architecture-standards' **generate-github-page** procedure exactly
(`plugins/architecture-core/skills/generate-github-page/SKILL.md` in
`konradcinkusz/architecture-standards`, with its bundled
`assets/ONE-PAGER-TEMPLATE.html`): derive owner/repo/Pages URL from `git remote` and echo the
table in the PR; copy the template's CSS verbatim and fill only the `{{TOKENS}}` /
`<!-- CUSTOMIZE -->` parts from the final README (T4.3); stats only for numbers verifiable in
the repo (e.g. test count, tasks, endpoints) — otherwise delete the row; one usage shape
(`.code` quickstart: `git clone …` + `dotnet run --project src/Chess.AppHost`); documents list
linking `docs/RATING.md` and `docs/architecture/`; real license line; no logo unless one exists.
Copy `.github/workflows/deploy-pages.yml` from architecture-standards **verbatim**. Run the
skill's §8 verification (no `{{`/`CUSTOMIZE` left, links resolve). State in the PR the one
manual step — Settings → Pages → Source → "GitHub Actions" — for the owner.

---

## Wave 5 — Broadcast mode (03 §9, ADR-011)

Common context for T5.*: read 03 §9 and ADR-011 first. Everything is stored in Postgres; tests use
InMemory + `FakeLlmGateway` + random players. A broadcast game is an ordinary rated game created
through the same game runner path; pacing is applied **only** to games that belong to a broadcast slot.

### T5.1 — Broadcast domain: contracts, planner, persistence
**Owns:** `src/Chess.Api/Broadcast/**` except runner/endpoint files created by T5.2/T5.3
(i.e. you own `BroadcastContracts.cs`, `BroadcastPlanner.cs`, `BroadcastEntities.cs`, `BroadcastExtensions.cs`),
`src/Chess.Api/Persistence/**` (new entities, DbSets, the `AddBroadcast` migration), `tests/Chess.Api.Tests/Broadcast/Planner*`, `tests/Chess.Api.Tests/Persistence/**`, and `Program.cs` additions
`builder.AddBroadcast(); api.MapBroadcastEndpoints();` (stubs for T5.2/T5.3 in their own files: `BroadcastRunnerExtensions.cs`, `BroadcastEndpoints.cs`).
**Do:** the records of 03 §9.2 verbatim; EF entities/tables of 03 §9.3 + migration; the pure
`BroadcastPlanner.Plan(PlanRequest, IReadOnlyDictionary<string,double?> premiumMultipliers)`:
all unordered pairs repeated round-robin until the window is filled, colours alternating per repeat,
no model plays two slots in a row unless only two models exist, deterministic for `Seed`, slot duration =
`AssumedPlies × (AssumedMoveLatencyMs + MoveDelayMs) + GameGapSeconds`, last slot only if it ends ≤ `endsAt`,
returns slots + premium estimate (`slots-per-model × AssumedPlies/2 × multiplier`).
**Done when:** planner tests: 24 h / 3 models / defaults fills the window within one game length;
deterministic per seed, different per seed; never two identical consecutive pairings; colour balance within 1;
`Random` kind allowed as a model; fewer than 2 models → validation error. Migration applies (script contains both tables).
**Verify:** V-NET.

### T5.2 — Broadcast runner
**Owns:** `src/Chess.Api/Broadcast/BroadcastRunner*.cs`, `BroadcastRunnerExtensions.cs`,
`src/Chess.Api/Games/GameRunner*.cs` and `GameRun.cs` **only** to add an optional per-game pacing delay and a
way to run one game to completion awaitably (no change to existing behaviour or contracts),
`tests/Chess.Api.Tests/Broadcast/Runner*`.
**Do:** `BroadcastRunner : BackgroundService`: when a schedule is `Running`, take the next `pending` slot at/after
its planned start (wait until then), create the game (rated, via the same persistence as `POST /api/games`), run it
with `MoveDelayMs` between moves, wait `GameGapSeconds`, mark the slot, publish a `NowNext` change; pause/resume/stop
honoured between moves; on startup resume a `Running` schedule (interrupted game → slot `skipped`); failure policy of
03 §9.1 (backoff 30 s→10 min, 3 attempts then `skipped`, `Notice` text while backing off); never throws out of
`ExecuteAsync`. Expose `IBroadcastController` (start/pause/resume/stop/now) for T5.3. All delays through an injectable
`TimeProvider` so tests run instantly.
**Done when:** with a fake time provider: slots run in order and only one game at a time; per-move delay observed;
pause holds the next move; restart resumes at the next slot and marks the interrupted one skipped; a gateway that always
fails → 3 attempts, `skipped`, next slot runs; stop cancels the running game as `aborted`.
**Verify:** V-NET.

### T5.3 — Broadcast API endpoints
**Owns:** `src/Chess.Api/Broadcast/BroadcastEndpoints.cs`, `tests/Chess.Api.Tests/Broadcast/Endpoints*`.
**Do:** all endpoints of 03 §9.2 (validation → problem details; plan returns Draft with slots + estimate; slot edits only
for Draft/Scheduled; start/pause/resume/stop via `IBroadcastController`; `GET /now` builds `NowNextDto`).
**Done when:** WebApplicationFactory tests for each route incl. validation, illegal state transitions (409), and `/now` shapes
in Idle/Running/Paused/Notice.
**Verify:** V-NET.

### T5.4 — UI: broadcast planner (`/broadcast/plan`)
**Owns:** `src/Chess.Web/Components/Pages/Broadcast/Plan*`, `src/Chess.Web/Api/BroadcastApiClient.cs` + `BroadcastContracts.cs` (copies of 03 §9.2 records),
`tests/Chess.Web.Tests/Broadcast/Plan*`, and one nav link added to `NavMenu.razor` (the single allowed edit outside the folder).
**Do:** per 03 §9.4: form, preview timeline (pure SVG/CSS, no JS), premium-request estimate before saving, slot
edit/reorder/delete, Save draft / Start, schedules list with Pause/Resume/Stop. `data-testid`s on every control.
**Done when:** bUnit: preview renders slots from a stub; estimate visible before save; start disabled until saved; controls call the right client methods.
**Verify:** V-NET.

### T5.5 — UI: `/broadcast` OBS view
**Owns:** `src/Chess.Web/Components/Pages/Broadcast/Live*`, `src/Chess.Web/Components/Layout/BroadcastLayout.razor(.css)`,
`tests/Chess.Web.Tests/Broadcast/Live*`.
**Do:** per 03 §9.4 — chrome-less layout (`@layout BroadcastLayout`), 1920×1080 fixed-aspect scaling, big board
(reuse `ChessBoardSvg`), header with game N of M, move tail, thinking indicator, mini leaderboard from `/api/ratings`
(refresh after each game), Up next, clock, intermission/technical-break cards from `/api/broadcast/now`, SSE
auto-reconnect with back-off, `?theme=` / `?top=` options, minimum 28 px text. No scrollbars, no cursor. Reads `/api/broadcast/now` every 5 s.
**Done when:** bUnit: shows current slot players and board from stub; intermission card when `Current` is null; technical-break card when `Notice` set; leaderboard limited by `?top`.
**Verify:** V-NET.

### T5.6 — 24-hour soak test + runbook
**Owns:** `tests/Chess.Api.Tests/Broadcast/Soak*`, `docs/BROADCAST.md`, `scripts/broadcast-smoke.sh`.
**Do:** an accelerated soak test: fake `TimeProvider` advancing 24 simulated hours with random + fake players and induced
gateway failures and one simulated restart — asserts every slot ends `finished|skipped`, no overlap, ratings still computable,
schedule `Finished`, no unhandled exceptions. `docs/BROADCAST.md`: how to plan and start a day, cost estimate guidance
(premium multipliers, requests per day), OBS setup step by step (Browser Source `http://localhost:5070/broadcast`, 1920×1080,
"control audio via OBS" off, custom CSS none), YouTube Live settings pointer, keeping the machine awake (disable sleep), what
happens on restart / Copilot outage, how to stop. `scripts/broadcast-smoke.sh` (shellcheck-clean): starts the fake profile, plans a
1-hour schedule at 50 ms moves, starts it and prints `/api/broadcast/now` until finished.
**Done when:** soak test green and fast (< 60 s real time); runbook commands verified.
**Verify:** V-NET (+ shellcheck if installable).

### T4.4 — Final architecture review
**Owns:** `docs/architecture/ARCHITECTURE_REVIEW.md`, board statuses in this file,
ADR statuses in 05.
REVIEW mode of the playbook: walk the compliance checklist item by item against the merged
code, findings ranked by severity with failure scenarios, deviations register (dated), and an
alignment-actions table. Any blocking finding becomes a new task here.

---

## Follow-ups from the final review

Source: [ARCHITECTURE_REVIEW.md](ARCHITECTURE_REVIEW.md) §4. Only blocking and high findings become tasks here; the medium and
low items are in its §6 alignment table. None is blocking. These rows are not on the board above, so they are not counted in it.

| ID | Title | Depends on | Status |
|---|---|---|---|
| F1 | Reject cross-origin requests to the loopback app (review H-1) | — | done (F5, #65) |

### F1 — Reject cross-origin requests to the loopback app

**Owns:** `src/Chess.Api/` (a request-validation middleware and its registration in `Program.cs`), `src/Chess.Web/Program.cs`,
`tests/Chess.Api.Tests/Security/**`, `tests/Chess.Web.Tests/Security/**`.
**Why:** the review showed that a page on another origin can start a broadcast day (`POST …/schedules/{id}/start` → 200) and
that the Blazor websocket accepts a foreign `Origin` (`/_blazor/negotiate` → 200, upgrade → 101). ADR-006 assumes the loopback
binding and the absence of CORS keep the browser out. They keep out reads, not writes or the websocket.
**Do:**
1. Middleware on `/api` and `/_blazor` that answers 403 when the `Origin` header is present and is not `http://localhost:5070`,
   or when `Sec-Fetch-Site` is `cross-site`.
2. Require a non-simple request on every `POST` and `DELETE` (`Content-Type: application/json` or a custom header), so a
   cross-origin write needs a preflight, which the app never answers.
3. Keep the `fake` profile and the E2E journeys working; they use the owner's origin.
**Done when:** a test sends `POST /api/broadcast/schedules/{id}/start` and `POST /_blazor/negotiate` with
`Origin: http://evil.example` and gets 403; the same requests with `Origin: http://localhost:5070` succeed; the E2E journeys pass.
**Verify:** V-NET; `E2E_CHROMIUM_PATH=<chromium> dotnet test tests/Chess.E2E`.

## UI polish (owner request 2026-10-25)

The owner asked for a better-looking UI. The design is frozen in [07-UI-STYLE](07-UI-STYLE.md): tokens, shared classes, the
app shell and the direction for each area. U1 and U2 run in parallel; U3, U4 and U5 start when U1 is merged (they use its
classes). These rows are not on the board above.

| ID | Title | Depends on | Status |
|---|---|---|---|
| U1 | Design tokens, base styles, shared classes, app shell | — | done (#69) |
| U2 | Chart polish | — | done (#68) |
| U3 | Results and head-to-head pages | U1 | in progress |
| U4 | Live game, games list and replay | U1 | in progress |
| U5 | Tournaments, broadcast planner, attempts log | U1 | in progress |
| U6 | Charts fill their card on wide screens | U2 | todo |
| U7 | Stockfish status reads `available` or `absent` | U1 | todo |

Common to every U task: follow 07-UI-STYLE §1 (no dependencies, tokens only, both themes, phone width, accessibility, test
hooks unchanged) and prove the work as in §7. **Verify:** `dotnet build Chess.sln -warnaserror`;
`E2E_CHROMIUM_PATH=<chromium> dotnet test Chess.sln`; the §7 screenshots and phone-width check of the task's pages.

### U1 — Design tokens, base styles, shared classes, app shell

**Owns:** `src/Chess.Web/wwwroot/app.css`, `src/Chess.Web/Components/Layout/MainLayout.razor`, `NavMenu.razor` (and new
`MainLayout.razor.css` / `NavMenu.razor.css` if wanted), `src/Chess.Web/Components/Shared/StatusStrip.razor` (and a new
`StatusStrip.razor.css`), `tests/Chess.Web.Tests/Components/NavigationTests.cs`, `StatusStripTests.cs`, a new
`tests/Chess.Web.Tests/Components/StyleContractTests.cs`.
**Do:** every token of 07 §2 in light and dark; the base element styles of §3 scoped under `.app-shell`; every shared class of
§4; the shell of §5 (sticky header with brand mark and pill nav, status strip with dots, centred main).
**Done when:** `StyleContractTests` reads `app.css` and fails when any §2 token is missing from the light block or the dark
block, or when any §4 class has no rule; bUnit: the status strip gives Copilot `Ready` a success dot, `NotAuthenticated` a
warning dot and `Unavailable` a danger dot, and still shows the detail text; Results is still the first nav item; the
`/broadcast` page renders as before (screenshot compared by eye, noted in the PR); every page at 390 px has no horizontal
page scroll caused by the shell.

### U2 — Chart polish

**Owns:** `src/Chess.Web/Components/Charts/**`, `tests/Chess.Web.Tests/Charts/**`.
**Do:** 07 §6 Charts. The frame is card-like with inner padding. Heatmap cells ≤ 72 px and labels at chart text size. Bar
thickness ≤ 48 px with a 4 px rounded data end. A degenerate axis domain (all x or all y equal, as with the fake gateway's
zero latency) shows one tick and the points still plot. The stacked 100 % bar uses the outcome colours of 07 §2 and its
legend follows the stacking order (wins, draws, losses, forfeit losses). Chart text stays at least 11 px rendered at 390 px.
The series palette and `ChartStylesheetTests` stay as they are.
**Done when:** bUnit/unit tests: a scatter whose x values are all 0 renders exactly one x tick label; the heatmap cell size
never exceeds 72 for 2 and for 12 models; the bar chart bar thickness never exceeds 48; the stacked bar legend order equals
its segment order. Existing chart tests still pass.

### U3 — Results and head-to-head pages

**Owns:** `src/Chess.Web/Components/Pages/Results/**`, `src/Chess.Web/Components/Pages/HeadToHead/**`,
`src/Chess.Web/Components/Pages/Shared/**` (only if markup there must change), `tests/Chess.Web.Tests/Results/**`,
`tests/Chess.Web.Tests/HeadToHead/**`.
**Do:** 07 §6 Results. Big Picture hero card, each section a card, leaderboard in `.table-wrap` with `.num` and badges, filter
row styled as a `.field`. Head-to-head: the two pickers in a `.field-row`, the RQC result as a hero number with its verbal
reading and interval, the direct record and games in a card.
**Done when:** bUnit: the leader's name is in the hero element; provisional models carry a `.badge`; the leaderboard table is
inside `.table-wrap`; section order and the existing significance tests unchanged; E2E `ResultsJourney` passes. The Big
Picture headline and chart are visible without scrolling at 1366×768 (screenshot).

### U4 — Live game, games list and replay

**Owns:** `src/Chess.Web/Components/Pages/Play/**`, `src/Chess.Web/Components/Pages/Games/**`,
`src/Chess.Web/Components/Shared/ChessBoardSvg.razor`, `tests/Chess.Web.Tests/Play/**`, `tests/Chess.Web.Tests/Games/**`,
`tests/Chess.Web.Tests/Components/ChessBoardSvgTests.cs`.
**Do:** 07 §6 Live game. Players panel and board in cards, primary start button, numbered two-column move list, result badge,
board colours from tokens-compatible values (board squares may keep fixed wood colours, documented in the PR). Games list:
filters in a `.field-row`, table in `.table-wrap`, result as a badge (win for White, win for Black, draw), dates compact.
Replay: stepping buttons as a button group, move details in a card.
**Done when:** bUnit: the start button has `.btn-primary`; the result renders as a `.badge`; the games table is inside
`.table-wrap`; existing Play/Games tests pass; E2E live-game and replay journeys pass.

### U5 — Tournaments, broadcast planner, attempts log

**Owns:** `src/Chess.Web/Components/Pages/Tournaments/**`, `src/Chess.Web/Components/Pages/Broadcast/PlanPage.razor`,
`PlanPage.razor.css`, `PlanTimeline.razor`, `PlanTimeline.razor.css`, `PlanTick.razor`, `src/Chess.Web/Components/Pages/Admin/**`,
`tests/Chess.Web.Tests/Tournaments/**`, `tests/Chess.Web.Tests/Admin/**`, `tests/Chess.Web.Tests/Broadcast/Plan*.cs`.
Not `LiveBroadcastPage` (the OBS view is out of scope).
**Do:** 07 §6 Tournaments. Forms in cards with `.field-row`s and model `.chips`; estimates as `.callout-info`; primary and
danger buttons; lists in `.table-wrap`; progress with `.progress`; attempts filters in a `.field-row` and prompt/reply
`details` readable in monospace.
**Done when:** bUnit: model pickers render as `.chips`; the create/plan buttons have `.btn-primary` and cancel/stop have
`.btn-danger`; tables are inside `.table-wrap`; existing tests pass; E2E tournament journey passes.

### U6 — Charts fill their card on wide screens

**Owns:** `src/Chess.Web/Components/Charts/**`, `tests/Chess.Web.Tests/Charts/**`.
**Why:** after U2 every chart is drawn at its natural viewBox width, so at 1366 px the charts use a third to a half of their
card and leave a wide empty band on the right (supervisor screenshots after U1 + U2). The heatmap also leaves a tall empty
band above its rotated column labels.
**Do:** on a wide screen a chart grows to the width of its card, up to 1.5× its natural size; it is never drawn smaller than
its natural size (at phone width it still scrolls inside its frame, as U2 decided, so its text stays ≥ 11 px). A way that
needs no JavaScript: `width: 100%` on the SVG with `min-width` = the natural width and `max-width` = 1.5 × the natural width
as an inline style computed by the component. Trim the heatmap's top margin to what its rotated labels need.
**Done when:** unit tests: every chart component's SVG carries a `min-width` equal to its viewBox width and a `max-width` of
1.5 × that width; the heatmap's top margin is at most the measured label length plus 8 px for 3 and 12 models. At 1366 px
(screenshot) each chart on `/results` spans at least 80 % of its card or reaches its 1.5× cap; at 390 px the page still has no
horizontal scroll.

### U7 — Stockfish status reads `available` or `absent`

**Owns:** `src/Chess.Api/Catalog/StatusReporter.cs`, `tests/Chess.Api.Tests/Catalog/**`,
`src/Chess.Web/Components/Shared/StatusStrip.razor`, `tests/Chess.Web.Tests/Components/StatusStripTests.cs`.
**Why:** 03 §8 says `/api/status` reports `stockfish` as `available | absent`. `StatusReporter` passes the health verdict
through, and `MoveQualityHealthCheck` is Healthy in both cases (absent is optional, not broken), so the status strip shows a
green "Stockfish: healthy" on a machine without Stockfish (found by U1).
**Do:** `StatusReporter` maps the `stockfish` entry to `available` or `absent` from its description (`available` /
`absent — move quality disabled`) when the check is Healthy, passes any other verdict through as today (`degraded`,
`unhealthy`), and keeps `unknown` when the check is not registered. `Database` is unchanged. The status
strip shows `available` with a success dot and `absent` with a neutral dot and the tooltip "move quality disabled".
**Done when:** API tests: an analyzer that is not available gives `Stockfish = "absent"`, an available one `"available"`, a
missing check `"unknown"`; bUnit: `absent` renders a neutral dot, `available` a success dot. No contract shape changes
(`StatusDto` keeps its four strings).

## Live game transparency and cost (owner request 2026-10-25, 03 §10, ADR-012)

The owner played real games and could not tell who was to move or what happened on each move, had no view of the
models' reasoning or of the messages exchanged with the engine, got no token or dollar figures, and saw "multiplier
unknown" for every Copilot model (cause: AI-credit billing, 03 §10). The contracts are already on `main` (03 §10.1).
G1 and G2 run in parallel; G3 starts when U4 and G2 are merged; G4 after G3; G5 when U5 and G2 are merged; G6 last.

| ID | Title | Depends on | Status |
|---|---|---|---|
| G1 | Gateway: tokens, reasoning, AI credits and model prices from the SDK | — | todo |
| G2 | API: store and stream every attempt with usage; rejection kinds; usage endpoint; model prices | — | todo |
| G3 | Live game page: player bars, turn, move table, engine log, game summary, model prices | U4, G2 | todo |
| G4 | Replay page: the same move table, engine log and summary for stored games | G3 | todo |
| G5 | Estimates in AI credits and USD on the tournament form and the broadcast planner | U5, G2 | todo |
| G6 | Docs: README, RATING.md §8, BROADCAST.md §2, CLAUDE.md, smoke script output | G1–G5 | todo |

Common **Verify** for G tasks: `dotnet build Chess.sln -warnaserror`; `E2E_CHROMIUM_PATH=<chromium> dotnet test Chess.sln`;
for UI tasks the 07-UI-STYLE §7 screenshots.

### G1 — Gateway: tokens, reasoning, AI credits and model prices from the SDK

**Owns:** `src/Chess.Llm/**`, `tests/Chess.Llm.Tests/**`, `scripts/smoke-copilot.sh`.
**Do:** in `CopilotLlmGateway`, fill `LlmModelInfo.Pricing` from `ModelInfo.Billing.TokenPrices` (keep the multiplier when
present); per reply, sum `InputTokens`, `OutputTokens`, `ReasoningTokens`, `CacheReadTokens` and `CopilotUsage.TotalNanoAiu`
over the reply's `AssistantUsageEvent`s (credits = nano AIU / 1e9; null when no event reported one, and when
`AiCreditsStatus` is `unavailable`), and take the reasoning from the final `AssistantMessageData.ReasoningText`, else the
concatenated `AssistantReasoningEvent` contents of the reply. Find out from the SDK docs and the RPC surface what unit
`models.listTokenPriceUnits` reports and record it, with the 1 AIU = 1 credit assumption, in `src/Chess.Llm/README.md`;
log each model's raw billing at Debug on startup. `FakeLlmGateway`: deterministic usage (input ≈ prompt length / 4, output
≈ reply length / 4, a few reasoning tokens), a short deterministic reasoning text that names the chosen move, prices for
`fake-a` and `fake-b` (e.g. 1.0 / 4.0 credits per 1 000 000 tokens) and `AiCredits` computed from them, so the whole cost
path shows non-zero values on the fake profile. `scripts/smoke-copilot.sh`: also print, per attempt, the tokens, credits
and whether reasoning was present, and the game's `/usage` (after G2; if G2 is not merged yet, print what exists and leave
a TODO line for `/usage` in the PR).
**Done when:** unit tests: the usage aggregation (several usage events in one reply, missing fields, unavailable credits),
the reasoning selection (message text first, events second, none), the price mapping, and the fake's determinism and
non-zero usage; `PublicSurfaceTests` still pass.

### G2 — API: store and stream every attempt with usage; rejection kinds; usage endpoint; model prices

**Owns:** `src/Chess.Api/**` except `Tournaments/**`, `Broadcast/**` and `Catalog/StatusReporter.cs`;
`tests/Chess.Api.Tests/**` except `Tournaments/**`, `Broadcast/**` and `Catalog/StatusReporter*`.
**Do:**
1. `move_attempts` gets `reasoning`, `input_tokens`, `output_tokens`, `reasoning_tokens`, `cached_input_tokens`,
   `ai_credits`, `rejection_kind` (one EF migration). The player and the runner store them for every attempt, legal or not.
2. Publish `AttemptRecorded` after each attempt is stored (before `MovePlayed` or `AttemptRejected`).
3. Rejection kinds (03 §10.3) decided in the player: `unparseable`, `illegal`, `ambiguous` (add the check in the player, not
   in `Chess.Engine`: count the legal moves whose SAN without disambiguation equals the candidate), `timeout`, `error`;
   the `AttemptRejected.Reason` text names the kind and the candidate. Default `Players:MoveTimeoutSeconds` = 180.
4. `GET /api/games/{id}/usage` → `GameUsageDto` per 03 §10.2, with `CostOptions` (`Arena:Cost:UsdPerAiCredit`, 0.01) and a
   pure `GameCostCalculator` (estimate from the model's prices when no cost was reported).
5. `GET /api/models`: `ModelDto.Pricing` in USD per 1M tokens; hide the Auto model unless `Arena:ShowAutoModel` (03 §10.2).
6. `GET /api/attempts`: the new `AttemptDto` fields.
**Done when:** tests for each item: an attempt row keeps all usage fields; the SSE stream of a fake game carries an
`AttemptRecorded` for every attempt with the prompt and reply; each rejection kind (prose → `unparseable`, a legal-looking
SAN that is not legal → `illegal`, `Rc7` with two rooks able to reach c7 → `ambiguous`, a timing-out conversation →
`timeout`); the calculator for every cost basis (free, reported, estimated, partial, unknown) and the USD rate; `/usage` for
a finished fake game is non-zero and its totals equal the sum of the attempts; the models endpoint maps prices and hides
`auto`. The migration applies on the InMemory provider and `dotnet ef migrations` shows it.

### G3 — Live game page: player bars, turn, move table, engine log, game summary, model prices

**Owns:** `src/Chess.Web/Components/Pages/Play/**`, a new `src/Chess.Web/Components/Shared/Game/**` (reusable
`PlayerBar`, `MoveTable`, `EngineLog`, `GameUsageSummary`), `src/Chess.Web/Components/Shared/ChessBoardSvg.razor`
(last-move and check highlights), `src/Chess.Web/Api/ArenaApiClient.cs` (usage call), `tests/Chess.Web.Tests/Play/**`,
`tests/Chess.Web.Tests/Components/**` for the shared components, `tests/Chess.E2E/LiveGameJourney.cs`.
**Do:** 03 §10.4 on the live page, styled with 07-UI-STYLE classes. Read `AttemptRecorded` from the stream; load
`/api/attempts?gameId=` and `/api/games/{id}/usage` when a page opens on a running or finished game and after
`GameFinished`. The model pickers show the price (`$3.00 / $15.00 per 1M tokens`), not "multiplier unknown".
**Done when:** bUnit: the side to move and "attempt n of 3" follow `MoveStarted`/`AttemptRecorded`; the move table numbers
moves in pairs and marks retries; the log shows prompt, reply and reasoning, and the "did not expose its reasoning" text;
each rejection kind renders its badge; the summary renders every cost basis and the rate; the picker shows prices and
"price unknown". The E2E live-game journey also checks the turn indicator, one log entry and the summary on the fake
profile. Screenshots of a running and a finished fake game, light, dark and phone.

### G4 — Replay page: the same move table, engine log and summary for stored games

**Owns:** `src/Chess.Web/Components/Pages/Games/**`, `tests/Chess.Web.Tests/Games/**`, `tests/Chess.E2E/ReplayJourney.cs`.
**Do:** `/games/{id}` uses G3's `MoveTable`, `EngineLog` (entries of the selected ply highlighted as you step) and
`GameUsageSummary`; the games list gets a cost column (estimated USD, "—" when unknown).
**Done when:** bUnit for the three parts on a stored game and for the cost column; E2E replay journey checks the log and
the summary.

### G5 — Estimates in AI credits and USD on the tournament form and the broadcast planner

**Owns:** `src/Chess.Api/Tournaments/**`, `src/Chess.Api/Broadcast/**` (estimate code only), `src/Chess.Web/Components/Pages/Tournaments/**`,
`src/Chess.Web/Components/Pages/Broadcast/Plan*`, the matching tests, and additive fields on the tournament and broadcast
estimate DTOs (API and web copies, with round-trip tests) — authorised by the supervisor for this task only.
**Do:** estimate credits per model = expected moves × (average input and output tokens per move of that model in stored
games, else `Arena:Cost:AssumedInputTokensPerMove` 700 and `AssumedOutputTokensPerMove` 300) × the model's prices; show
credits and USD per model and in total, with the basis ("from N past moves" or "assumed"); keep the premium-request figure
only for models that still report a multiplier. The model checkboxes show prices instead of "multiplier unknown".
**Done when:** tests for the estimator (history, assumption, unknown price, multiplier model) and for both forms showing
USD and the basis; screenshots of both forms with a priced fake model.

### G6 — Docs

**Owns:** `README.md`, `docs/RATING.md` (§8 wording only), `docs/BROADCAST.md` (§2), `CLAUDE.md`, `src/Chess.Llm/README.md`
(if G1 left gaps).
**Do:** describe the live page, the engine log, the cost model (AI credits, USD rate, basis, the unverified assumptions),
the 180 s timeout, `/api/status` Stockfish `available | absent` (U7), `Arena:ShowAutoModel`; replace premium-request
wording where it is now wrong; a lessons-learned entry for the multiplier. Every command shown is one that was run.

## Public results site and viewer pace (owner decision 2026-10-25, 03 §10.5 and §11, ADR-013)

| ID | Title | Depends on | Status |
|---|---|---|---|
| S1 | Static export: results, head-to-head, games list, tournaments, about, JSON | — | todo |
| S2 | Static export: one page per game with boards, move table, engine log, usage, PGN | S1, G3, G4 | todo |
| S3 | Publishing to `gh-pages`, `/admin/site`, auto-publish, workflow and README | — | todo |
| P1 | Viewer pace on the live page | G3 | todo |
| R1 | Conservative ranking order (lower bound of the arena score) | — | todo |

### S1 — Static export: results, head-to-head, games list, tournaments, about, JSON

**Owns:** `src/Chess.Web/Site/**` except `Site/Publishing/**`, `src/Chess.Web/Components/Site/**`, `src/Chess.Web/Program.cs`
(one registration line), `tests/Chess.Web.Tests/Site/**`.
**Do:** implement `ISiteExporter` (03 §11) with `HtmlRenderer`: static components for every path in the table except
`games/<id>/` (S2), reusing the chart components and `ResultsModel`, never editing the interactive pages; copy the assets;
write `data/*.json` and `.nojekyll`; relative links only; snapshot time, rated-game count, "Watch live" and support links in
a shared static layout. `SiteOptions` binds `Arena:Site`.
**Done when:** tests export a fake data set (canned `ArenaApiClient` responses) into a temp folder and check: every page
exists; the index starts with the Big Picture; no link is absolute or points outside the export (a link checker over all
`href`/`src`); no `<script>` and no `blazor` reference in any page; JSON parses; the support and YouTube links render only
when configured. A real export from the fake profile opened with Playwright from `file://` and from a local static server
under a sub-path (`/llm-multi-agent-chess/`), screenshots light, dark and phone.

### S2 — Static export: one page per game

**Owns:** `src/Chess.Web/Components/Site/Game/**`, `src/Chess.Web/Site/Game/**`, `tests/Chess.Web.Tests/Site/Game/**`.
**Do:** `games/<id>/index.html` per finished game with G3's shared `MoveTable`, `EngineLog` and `GameUsageSummary`, a board per
ply reachable by `#ply-<n>` anchors (CSS `:target`, no script; first/prev/next/last as links), and `game.pgn`; the engine log
only for the newest `FullLogGames` games.
**Done when:** tests for the anchors and links of a short game, the log cut-off, the PGN; screenshots of a stored fake game.

### S3 — Publishing to `gh-pages`, `/admin/site`, auto-publish, workflow and README

**Owns:** `src/Chess.Web/Site/Publishing/**`, `src/Chess.Web/Components/Pages/Admin/Site*` (new page `/admin/site`),
`src/Chess.Web/Components/Layout/NavMenu.razor` (one item after Attempts), `tests/Chess.Web.Tests/Site/Publishing/**`,
`.github/workflows/deploy-pages.yml`, `README.md` (a "Public results site" section only).
**Do:** `GitSitePublisher` per 03 §11 (own clone in `WorkDir`, orphan commit, force-push to `Branch`, `.nojekyll`, a clear
`Detail` on every failure: no git, no remote, push refused); the remote defaults to the `origin` of the repository the app
runs from. A hosted service for auto-publish. The `/admin/site` page: export, publish, last result, the site URL. The
workflow only verifies the one-pager assembles (no deploy). README: the one-time Pages setting and the options.
**Done when:** tests against a local bare repository as the remote: first publish creates `gh-pages` with one commit; a second
publish still leaves one commit and replaces the content; the owner's working tree and `main` are untouched; failures are
reported, never thrown; auto-publish runs only when a game finished since the last publish (fake clock).

### P1 — Viewer pace on the live page

**Owns:** `src/Chess.Web/Components/Pages/Play/**`, `tests/Chess.Web.Tests/Play/**`.
**Do:** 03 §10.5 with a pure, unit-tested pacer (queue + `TimeProvider`).
**Done when:** unit tests: moves are released no faster than the pause; "Live" releases at once; attempts and log entries of a
ply come with or before its move; "behind" count and "Catch up"; a page opened mid-game shows the current position at once.
bUnit for the selector; the E2E live journey still passes (it may select "Live").

### R1 — Conservative ranking order (lower bound of the arena score)

**Why:** the owner asked that the number of games played weigh in the ranking. The fit already shrinks ratings of models
with few games (prior), marks them provisional and refuses significance below 20 games each (RATING.md §3, §6), but the order
among established models is by the point arena score. The owner chose (2026-10-25) to add a conservative order as an option.
**Owns:** `src/Chess.Rating/**`, `tests/Chess.Rating.Tests/**`, `docs/RATING.md` (§6 and §7), `src/Chess.Api/Ratings/**`,
`tests/Chess.Api.Tests/Ratings/**`, `src/Chess.Web/Components/Pages/Results/**`, `src/Chess.Web/Components/Charts/RankedBarChart.razor`,
`tests/Chess.Web.Tests/Results/**`, `tests/Chess.Web.Tests/Charts/RankedBarChart*`; and, authorised for R1 only, two additive
fields on `ModelRating` (03 §3.4) and its web copy: `double? ArenaScoreLow = null, double? ArenaScoreHigh = null`, with the
round-trip test.
**Do:** in the bootstrap, compute the arena score of every model on every sample and report its 2.5 / 97.5 percentiles as
`ArenaScoreLow` / `ArenaScoreHigh` (RATING.md §6). Add a pure ordering option: `score` (today's order, the default) and
`conservative` (non-provisional first, then by `ArenaScoreLow` descending, then arena score, then rating, then name).
`GET /api/ratings?order=score|conservative` (anything else → 400 problem). The Results page gets an "Order" control (Arena
score / Conservative) next to the tournament filter; the Big Picture headline and the chart follow the chosen order and the
headline says which order is used. The ranked bar chart's whiskers use the real `ArenaScoreLow`/`High` when present (they
approximate it from the pairwise intervals today). RATING.md §6 documents the conservative order and why (fewer games →
wider interval → lower bound); §7 gets test vectors.
**Done when:** Rating tests: the interval contains the point score; a model with fewer games has a wider interval; with
equal point scores the model with more games ranks first in the conservative order; same seed → identical intervals (V6
style); `score` order unchanged from today (existing vectors pass untouched). API test for the parameter and the 400. bUnit:
the control switches the order and the headline names it. E2E `ResultsJourney` still passes.

## Benchmark (owner request 2026-10-25, 03 §12, ADR-014)

| ID | Title | Depends on | Status |
|---|---|---|---|
| B1 | Gateway: reasoning effort, effort lists, custom models via the SDK provider config | — | todo |
| B2 | API: variants (`model@effort`), benchmark runs, report (JSON + Markdown) | G2, G5, B1 contracts | todo |
| B3 | UI: `/benchmark` (create, progress, report, downloads) | B2 | todo |
| B4 | `scripts/benchmark.sh` + `.ps1`, `docs/BENCHMARK.md`, README section | B2 | todo |
| M1 | Model map (bubble chart of two chosen coefficients) on the Results page | G2, R1, F8 | todo |

### B1 — Gateway: reasoning effort, effort lists, custom models

**Owns:** `src/Chess.Llm/**`, `tests/Chess.Llm.Tests/**`.
**Do:** 03 §12.1: pass `LlmConversationOptions.ReasoningEffort` to `SessionConfig.ReasoningEffort`; fill
`LlmModelInfo.ReasoningEfforts` / `DefaultReasoningEffort` from `ModelInfo`; `Arena:CustomModels` (options class in
Chess.Llm, bound by its registration) listed with `Custom = true` and started with `SessionConfig.Provider`
(`ProviderConfig.Type`, `BaseUrl`, `ApiKey`, `WireApi`, model name); id validation (no `@`, no clash with a Copilot id);
FakeLlmGateway: `fake-a` and `fake-b` support `low|medium|high`, and a configured custom model is served by the fake under
the fake profile so the whole path runs offline. The API key is never logged.
**Done when:** unit tests: the session config built for a variant carries the effort; for a custom model it carries the
provider fields and no Copilot model id; invalid ids rejected at startup with a clear message; the key does not appear in
`ToString()` or logs; the fake lists efforts and serves a custom model. `PublicSurfaceTests` pass.

### B2 — API: variants, benchmark runs, report

**Owns:** `src/Chess.Api/Benchmarks/**` (new), `src/Chess.Api/Tournaments/**`, `src/Chess.Api/Players/ChessPlayerFactory.cs`,
`src/Chess.Api/Catalog/ModelCatalog.cs`, `src/Chess.Api/Persistence/**` (one migration: tournament kind and candidate),
`src/Chess.Api/Program.cs` (one registration line), `tests/Chess.Api.Tests/Benchmarks/**`, `tests/Chess.Api.Tests/Tournaments/**`.
**Do:** 03 §12.1 (parse `model@effort` in the player factory; validate the effort against the model's list; ModelDto efforts)
and §12.2 (gauntlet schedule with alternating colours, the endpoints, the report with per-opponent bootstrap intervals, the
Markdown rendering, the fingerprint; app version from the assembly informational version).
**Done when:** tests: the schedule (counts, colours, opponents default to all enabled except candidate and Auto); a fake
benchmark of 2 opponents × 4 games finishes and its report's totals equal its games; the Elo difference for a 75 % score is
~+191 with an interval that contains it; Markdown contains the fingerprint and the table; an unknown effort → 400; cancel.

### B3 — UI: `/benchmark`

**Owns:** `src/Chess.Web/Components/Pages/Benchmark/**` (new), `src/Chess.Web/Api/ArenaApiClient.cs` (benchmark calls),
`src/Chess.Web/Components/Layout/NavMenu.razor` (one item), `tests/Chess.Web.Tests/Benchmark/**`, a new E2E journey
`tests/Chess.E2E/BenchmarkJourney.cs`.
**Do:** 03 §12.3 UI, styled by 07-UI-STYLE; the effort picker appears only for models that list efforts; the estimate
before start (G5's cost inputs); report page with a ranked bar chart of score per opponent (Charts components) and downloads.
**Done when:** bUnit for the form (effort list, opponents, estimate), progress and report; the E2E journey runs a tiny fake
benchmark (2 opponents × 2 games) to its report.

### B4 — Headless scripts and docs

**Owns:** `scripts/benchmark.sh`, `scripts/benchmark.ps1`, `docs/BENCHMARK.md`, `README.md` (a "Benchmark your own model"
section only).
**Do:** 03 §12.3; shellcheck-clean bash, the PowerShell twin; both run end to end against the fake profile.
**Done when:** a recorded run of both scripts against the fake profile (output in the PR); `shellcheck -S warning` clean;
every command in the docs was run.

### M1 — Model map on the Results page

**Owns:** `src/Chess.Web/Components/Charts/ModelMap*` (new component and its pure geometry/model classes),
`tests/Chess.Web.Tests/Charts/ModelMap*`, `src/Chess.Api/Results/**`, `tests/Chess.Api.Tests/Results/**`, the two additive
`ModelResultsDto` fields of 03 §13 (API and web copy, round-trip test); after R1 and F8 are merged:
`src/Chess.Web/Components/Pages/Results/**`, `tests/Chess.Web.Tests/Results/**`, `tests/Chess.E2E/ResultsJourney.cs` (the
section order now has `model-map` second).
**Depends on:** G2 (cost data), R1 and F8 (same page and journey).
**Do:** 03 §13. Build and test the component with canned data first; integrate into the Results page when R1 and F8 are on
`main`.
**Done when:** unit tests: axis choice and scales (log when range > 10×), bubble area ∝ games, the median guides and quadrant
names follow each axis's better direction, a degenerate axis (all equal) still plots, labels never leave the plot; API test
for the two new fields; bUnit: the axis pickers re-render the map, section order with `model-map` second; E2E
`ResultsJourney` updated and passing; screenshots light, dark and phone.

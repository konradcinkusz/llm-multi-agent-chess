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
| T0.1 | Remove Python, move .NET into `src/`, retarget net10 | — | todo |
| T0.2 | Solution skeleton, frozen contracts, manifest `Program.cs` | T0.1 | todo |
| T1.1 | ServiceDefaults kernel | T0.2 | todo |
| T1.2 | Aspire AppHost | T0.2 | todo |
| T1.3 | Persistence: new schema, provider switch, migration hosted service | T0.2 | todo |
| T1.4 | Copilot SDK gateway | T0.2 | todo |
| T1.5 | Engine characterisation tests (perft) | T0.2 | todo |
| T1.6 | Rating library (RATING.md) | T0.2 | todo |
| T1.7 | Stockfish analyzer | T0.2 | todo |
| T1.8 | Repo baseline + CI + secret scanning | T0.2 | todo |
| T1.9 | Chess.Web shell: Blazor Web App, API client, SSE client, board | T0.2 | todo |
| T1.10 | Engine: PGN writer + tolerant move notation | T0.2 | todo |
| T2.1 | Players: LLM player, random player, prompt, parser | T1.10 | todo |
| T2.2 | Game runner + event stream | T1.3 | todo |
| T2.3 | Games endpoints + SSE | T2.2 | todo |
| T2.4 | Tournaments | T2.2 | todo |
| T2.5 | Ratings endpoints | T1.3, T1.6 | todo |
| T2.6 | Models + status endpoints, health checks | T1.1, T1.4 | todo |
| T2.7 | Attempts (prompt log) endpoint | T1.3 | todo |
| T2.8 | Post-game move-quality pipeline | T1.7, T2.2 | todo |
| T2.9 | Rating snapshots + results breakdown endpoints | T1.3, T1.6 | todo |
| T3.1 | UI: live game page | T1.9, T2.3, T2.6 | todo |
| T3.0 | UI: SVG chart components | T1.9 | todo |
| T3.2 | UI: Results page — Big Picture + charts | T3.0, T2.5, T2.9 | todo |
| T3.3 | UI: head-to-head comparison | T1.9, T2.5 | todo |
| T3.4 | UI: tournaments | T1.9, T2.4 | todo |
| T3.5 | UI: game archive + replay | T1.9, T2.3 | todo |
| T3.6 | UI: attempts log | T1.9, T2.7 | todo |
| T4.1 | E2E (Playwright) on the AppHost with the fake gateway | T1.2, T3.1, T3.5 | todo |
| T4.2 | AI evals: offline scenario suite + live smoke script | T2.1, T2.2 | todo |
| T4.3 | README, CLAUDE.md, CHANGELOG final | T3.0 – T3.6 | todo |
| T4.4 | Final architecture review | T4.1, T4.2, T4.3 | todo |

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
fail if any project references `GitHub.Copilot.SDK` other than `Chess.Llm` (grep the csproj files).
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
cost warning, troubleshooting by `/health` state. Accurate against the code — every command run.

### T4.4 — Final architecture review
**Owns:** `docs/architecture/ARCHITECTURE_REVIEW.md`, board statuses in this file,
ADR statuses in 05.
REVIEW mode of the playbook: walk the compliance checklist item by item against the merged
code, findings ranked by severity with failure scenarios, deviations register (dated), and an
alignment-actions table. Any blocking finding becomes a new task here.

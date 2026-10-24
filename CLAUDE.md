# CLAUDE.md

## What the product is

LLM Chess Arena is a local tool that makes LLMs available through the owner's GitHub Copilot
subscription, has them play chess against each other, and ranks them with a published,
deterministic formula ([docs/RATING.md](docs/RATING.md)). It runs on localhost only; nothing is
deployed. Everything is persisted in Postgres. User-facing setup and usage are in
[README.md](README.md).

## Build and test

```bash
dotnet build Chess.sln -warnaserror
dotnet test Chess.sln
```

The SDK is pinned in `global.json` (10.0.x). Package versions live only in
`Directory.Packages.props`. Install the SDK when missing:
`sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0`.

Run the app without Docker or a Copilot login with the fake profile:
`dotnet run --project src/Chess.AppHost --launch-profile fake` (UI http://localhost:5070,
API http://127.0.0.1:5080). The default profile (`dotnet run --project src/Chess.AppHost`) needs Docker.

## Layout

Architecture reference: [docs/architecture/03-TARGET-ARCHITECTURE.md](docs/architecture/03-TARGET-ARCHITECTURE.md) §2.

```
global.json  Directory.Build.props  Directory.Packages.props  .editorconfig  Chess.sln
src/
  Chess.AppHost/          Aspire composition root: PostgreSQL (or ConnectionStrings:chess), Chess.Api, Chess.Web;
                          the `fake` launch profile sets ARENA_FAKE=1 (Llm=Fake, Database=InMemory, no Postgres)
  Chess.ServiceDefaults/  kernel: OpenTelemetry, /health and /alive, problem details, service discovery (<= 400 lines, no domain)
  Chess.Engine/           pure chess rules: FEN, SAN, legal moves, status, PGN writer, tolerant MoveNotation
  Chess.Llm/              ILlmGateway, CopilotLlmGateway, FakeLlmGateway. The ONLY project that references GitHub.Copilot.SDK
  Chess.Analysis/         IMoveQualityAnalyzer, StockfishAnalyzer (UCI), NoOpMoveQualityAnalyzer
  Chess.Rating/           pure rating maths (RATING.md): Bradley-Terry fit, RQC, bootstrap. No I/O, clock or EF
  Chess.Api/              the one service: Players, Games (runner, SSE), Tournaments, Ratings, Results (snapshots),
                          Quality (post-game move quality), Attempts, Catalog (models, status), Broadcast (planner,
                          runner, endpoints), Persistence (EF Core, migrations, startup sweep), Contracts (DTOs)
  Chess.Web/              Blazor Web App, interactive server rendering. Api/ holds its own DTO copies, the typed
                          ArenaApiClient and the SSE GameEventsClient. Components/: Layout, Charts (pure SVG),
                          Shared (ChessBoardSvg, StatusStrip), Pages (Play, Results, HeadToHead, Tournaments,
                          Games, Admin, Broadcast)
tests/
  Chess.Engine.Tests/  Chess.Rating.Tests/  Chess.Llm.Tests/  Chess.Api.Tests/  Chess.Web.Tests/
  Chess.E2E/              Playwright harness (only the driver smoke test so far)
docs/  RATING.md, BROADCAST.md, EVALS.md (linked from the README); architecture/ (owned by the supervisor)
scripts/                  dev-setup.sh, setup-hooks.sh, smoke-copilot.sh (owner's live run), broadcast-smoke.sh (fake profile)
```

Project references are the allowed edges: `Chess.Api` → ServiceDefaults, Engine, Llm, Analysis,
Rating; `Chess.Analysis` → Engine; `Chess.Web` → ServiceDefaults; `Chess.AppHost` → Api, Web.
Llm, Rating and Engine reference no projects. Any other edge is a review finding.

## Dependency budget

| Project | Allowed packages |
|---|---|
| Chess.Engine, Chess.Rating | none |
| Chess.Llm | `GitHub.Copilot.SDK` |
| Chess.Analysis | none (Stockfish is an external binary) |
| Chess.Api | EF Core, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.InMemory`. `Microsoft.EntityFrameworkCore.Design` is build tooling only (`dotnet ef`), not a runtime dependency |
| Chess.ServiceDefaults | OpenTelemetry.*, `Microsoft.Extensions.ServiceDiscovery`, `Microsoft.Extensions.Http.Resilience` |
| Chess.Web | none beyond the ASP.NET Core shared framework. No JS dependencies, no component library, no chess library |
| Chess.AppHost | `Aspire.Hosting.AppHost`, `Aspire.Hosting.PostgreSQL` |
| tests | xUnit, `Microsoft.AspNetCore.Mvc.Testing`, `bunit`, `Microsoft.Playwright` |

## Invariants

These hold on `main`. The CI jobs named in brackets enforce the ones they name.

- Copilot SDK types never leave `Chess.Llm`. [CI: only `Chess.Llm.csproj` may reference `GitHub.Copilot.SDK`; `PublicSurfaceTests` checks the public API by reflection]
- `Chess.Rating` is pure: no I/O, no clock, no EF.
- No fallback move: an illegal reply is recorded and, after `Arena:MaxAttemptsPerMove` attempts, the side forfeits.
- Contracts in 03 §3 and §5.1 change only by supervisor decision.
- Ratings are never stored as the leaderboard; `rating_snapshots` is history only and is never read back into a calculation.
- `Chess.ServiceDefaults` stays at or below 400 lines of C#. [CI: kernel size guard]
- Every `.csproj` under `src/` and `tests/` is in `Chess.sln`, and every web project (`Microsoft.NET.Sdk.Web`) is registered in `src/Chess.AppHost/AppHost.cs`. [CI: solution]
- Results is the first item of the navigation and its Big Picture comes first on the page (03 §6.1).
- A broadcast game is an ordinary rated game; pacing applies only to games that belong to a broadcast slot (03 §9).
- The Copilot health check is never Unhealthy: a missing login is Degraded and the rest of the app keeps running (P8).

## Working rules

- Tasks are listed in [docs/architecture/06-TASKS.md](docs/architecture/06-TASKS.md); the rules for implementing them are in [04-MIGRATION-PLAN.md](docs/architecture/04-MIGRATION-PLAN.md) §3.
- Tests need no network: use `FakeLlmGateway`, the InMemory provider and fixtures.
- No secrets, tokens or personal paths in any file. Copilot and Postgres secrets are AppHost secret parameters.
- Every command in the docs must be one that was run. When a doc says what a command prints or returns, check it against the code or a run.
- When a change touches behaviour the README describes, update the README in the same pull request (REPO-BASELINE §8: no claim the code does not back).

## Lessons learned

Fixes merged to `main`, with their pull request numbers in brackets. Keep them in mind before touching the same code.

- **Render mode on the root components.** Without `@rendermode="InteractiveServer"` on `Routes` and `HeadOutlet` in
  `App.razor`, every page was static SSR and buttons, forms and the live game stream did nothing (#47). Keep the render mode
  on both. A test pins the root component.
- **Link the scoped styles bundle.** Each page's `.razor.css` compiles into `Chess.Web.styles.css`, which must be linked in
  `App.razor` with `@Assets["Chess.Web.styles.css"]`. Without the link every page rendered unstyled (#56). A test pins the link.
- **Call `StateHasChanged` after a background refresh.** A `PeriodicTimer` reload runs on a timer thread, outside any UI event.
  Without `StateHasChanged` (or `InvokeAsync(StateHasChanged)`) the tournament pages kept the status they had at first render
  (#57). The broadcast page does the same for its polling.
- **`/api/status` carries the gateway state, not the health verdict.** `Copilot` in `StatusDto` is `Ready | Fake | NotAuthenticated |
  Unavailable`, which the Play page keys off. Returning the health verdict (`healthy`/`degraded`) made LLM players unselectable
  even with a working login (#51).
- **Never claim significance from a tiny sample.** One rated game collapses every bootstrap resample to a point, so an interval
  "excluded 0.5" on floating-point noise. One rule decides significance, in `Components/Pages/Shared/Significance.cs`, and both
  the Results verdict and the head-to-head reading use it (#53).
- **The CI secret scan reads only commits reachable from `HEAD`.** `gitleaks` runs with `--log-opts="HEAD"`. With
  `fetch-depth: 0` it would scan every branch, so one unmerged branch could fail unrelated pull requests (see the comment in `ci.yml`).

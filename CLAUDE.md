# CLAUDE.md

## What the product is

LLM Chess Arena is a local tool that makes LLMs available through the owner's GitHub Copilot
subscription, has them play chess against each other, and ranks them with a published,
deterministic formula ([docs/RATING.md](docs/RATING.md)). It runs on localhost only; nothing is
deployed. Everything is persisted in Postgres.

## Build and test

```bash
dotnet build Chess.sln -warnaserror
dotnet test Chess.sln
```

The SDK is pinned in `global.json` (10.0.x). Package versions live only in
`Directory.Packages.props`. Install the SDK when missing:
`sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0`.

## Layout

Architecture reference: [docs/architecture/03-TARGET-ARCHITECTURE.md](docs/architecture/03-TARGET-ARCHITECTURE.md) §2.

```
global.json  Directory.Build.props  Directory.Packages.props  .editorconfig  Chess.sln
src/
  Chess.AppHost/          Aspire composition root: Postgres, Chess.Api, Chess.Web
  Chess.ServiceDefaults/  kernel: OTel, health (/health, /alive), problem details — no domain
  Chess.Engine/           pure chess rules: FEN, SAN, legal moves, status, PGN
  Chess.Llm/              ILlmGateway + CopilotLlmGateway + FakeLlmGateway — the only project that references GitHub.Copilot.SDK
  Chess.Analysis/         IMoveQualityAnalyzer + StockfishAnalyzer + NoOpAnalyzer
  Chess.Rating/           pure rating maths (RATING.md) — no I/O, no clock, no EF
  Chess.Api/              the one service: players, game runner, tournaments, persistence, endpoints
  Chess.Web/              Blazor Web App (interactive server): the UI; talks to Chess.Api over HTTP only
tests/
  Chess.Engine.Tests/  Chess.Rating.Tests/  Chess.Llm.Tests/  Chess.Api.Tests/  Chess.Web.Tests/
  Chess.E2E/              Playwright for .NET, against the AppHost with the fake gateway
docs/  architecture/ RATING.md
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
| Chess.Api | EF Core, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.InMemory` |
| Chess.ServiceDefaults | OpenTelemetry.*, `Microsoft.Extensions.ServiceDiscovery`, `Microsoft.Extensions.Http.Resilience` |
| Chess.Web | none beyond the ASP.NET Core shared framework. No JS dependencies, no component library, no chess library |
| Chess.AppHost | `Aspire.Hosting.AppHost`, `Aspire.Hosting.PostgreSQL` |
| tests | xUnit, `Microsoft.AspNetCore.Mvc.Testing`, `bunit`, `Microsoft.Playwright` |

## Invariants

- Copilot SDK types never leave `Chess.Llm`.
- `Chess.Rating` is pure: no I/O, no clock, no EF.
- No fallback move: an illegal reply is recorded and, after the attempt limit, the side forfeits.
- Contracts in 03 §3 and §5.1 change only by supervisor decision.
- Ratings are never stored as the leaderboard; `rating_snapshots` is history only and is never read back into a calculation.

## Working rules

- Tasks are listed in [docs/architecture/06-TASKS.md](docs/architecture/06-TASKS.md); the rules for implementing them are in [04-MIGRATION-PLAN.md](docs/architecture/04-MIGRATION-PLAN.md) §3.
- Tests need no network: use `FakeLlmGateway`, the InMemory provider and fixtures.
- No secrets, tokens or personal paths in any file.

# 01 — Current state

Snapshot of `konradcinkusz/llm-multi-agent-chess` at `main` = `f0f5845` (2026-10-24), measured
against `architecture-standards/docs/architecture/00-REFERENCE-ARCHITECTURE.md`.

**Mode chosen: MODERNIZE** (see [PLAYBOOK](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/PLAYBOOK.md)).
The repo predates the architecture (no AppHost, no shared kernel, no tests on the .NET side),
and it builds: `dotnet build ChessApi` and `dotnet build ChessBlazor` both succeed with SDK
8.0.132 (0 errors; 1 warning in ChessBlazor) in the planning session. Packages are on nuget.org,
the TFM (`net8.0`) is in support until 2026-11-10, and there are no private feeds.

## 1. What is in the repository

There are **two independent implementations** of the same product, and neither is wired to
the other.

| Path | What it is | State |
|---|---|---|
| `src/`, `tests/`, `requirements.txt` | Original Python app: Streamlit UI, `python-chess`, OpenAI `openai==1.0.0`, 11 pytest tests | Legacy. The README still documents only this one. |
| `chess-dotnet/ChessApi` | ASP.NET Core 8 API: MVC controllers, SignalR hub, EF Core 8 + Npgsql, OpenAI .NET SDK 2.1, hand-written chess engine (`Chess/ChessBoard.cs`, 692 lines) | The port. Most recent work (#1, #2). |
| `chess-dotnet/ChessBlazor` | Blazor WebAssembly SPA served by nginx, SignalR client, admin pages for prompts/conversations | Also contains template leftovers: `Counter.razor`, `Weather.razor`, `Home.razor`, `sample-data/weather.json`. |
| `chess-dotnet/docker-compose.yml` | Postgres 16 + api + nginx frontend on port 80 | The only way to run the .NET side. |

## 2. Backend (`ChessApi`)

- **Composition:** `Program.cs` (47 lines) wires everything inline: options, `AddDbContextFactory`,
  singletons, controllers, SignalR, Swagger, an allow-any-origin CORS policy *with credentials*,
  and a synchronous `MigrateAsync()` **before** `app.Run()`.
- **Domain:** `ChessGameSession` holds the live game in memory (`ConcurrentDictionary` inside the
  singleton `GameService`). The database only receives copies. On restart every in-flight game
  is lost and stays `"Ongoing"` in the database forever.
- **Players:** `IChessAgent` with `LlmChessAgent` (OpenAI `ChatClient`, one stateless system
  prompt per attempt, `MaxOutputTokenCount = 15`) and `RandomChessAgent`. Models are a hard-coded
  list of OpenAI ids in `appsettings.json` (`gpt-4o-mini`, `gpt-4o`, `gpt-3.5-turbo`, `gpt-4`).
- **Ranking-relevant behaviour:** after `MaxRetries` illegal answers the LLM agent **silently
  plays the first legal move** (`LlmChessAgent.cs:325`). A model that cannot play chess at all
  still produces complete games, indistinguishable in the data from a model that played well.
- **Auto-play:** `GameHub.StartAutoPlay` starts a `Task.Run` loop that keeps calling the hub's
  own `MakeMoveAsync` after the hub invocation has returned. Hub instances are transient and
  disposed after each invocation, so `Clients`/`Context` are used after disposal; the loop is
  also tied to one browser connection. Expected to fail intermittently — not reproduced, because
  nothing exercises it in a test.
- **Persistence:** EF Core, 3 entities (`Game`, `MoveRecord`, `PromptLog`), 2 migrations.
  Prompt logs are written fire-and-forget (`_ = Task.Run(...)`), so errors vanish.
  `ListGames` sorts by `Guid` (random order) and reads only memory.
- **Status as string:** game status is the display text (`"Checkmate! White wins!"`); the admin
  stats query compares against literals. The winner is not stored as data.
- **Config/secrets:** `appsettings.json` commits a connection string with password `changeme`;
  the OpenAI key is read from `AppConfig__OpenAiApiKey`. No secret scanner anywhere.
- **Observability:** none. `/health` returns a constant `ok`; no `/alive`, no OTel.

## 3. Frontend (`ChessBlazor`)

Blazor WASM talking to the API through nginx (`/api/`, `/gamehub`). Pages: game (`Index.razor`:
agent config, board SVG fetched from the API, controls, move list, analysis), admin dashboard,
prompts, conversations. No tests.

## 4. Engineering baseline

| Item | Present? |
|---|---|
| CI workflows (`.github/`) | No |
| Secret scanning (pre-commit / CI) | No |
| `.editorconfig`, `Directory.Build.props`, `Directory.Packages.props`, `global.json` | No |
| `.sln` | No |
| .NET tests | **None** (the 692-line engine has zero tests) |
| `CODEOWNERS`, Dependabot, PR template | No |
| `.claude/settings.json` declaring the standards | No |
| `docs/` | No (this directory is the first) |
| README accurate | No — documents the Python app only |

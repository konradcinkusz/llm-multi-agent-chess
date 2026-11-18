# Changelog

All notable changes to this project are documented in this file. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Everything merged to `main` since 0.1.0, and the work of the session branch that follows it.

### Added

- **README.** New *What you get*, *How it works*, *Configuration* and *API* sections; MIT license year corrected to 2026.
- **Live game transparency.** The gateway reports tokens, reasoning and AI credits from the SDK, and the runner stores and
  streams every attempt with its usage and a rejection kind. The live page shows player bars, whose turn it is, a move table, the
  engine log, a game summary and model prices. The replay page shows the same for stored games.
- **Cost in AI credits.** Copilot bills per token, so the tournament form and the broadcast planner estimate in AI credits with a
  dollar figure at `Arena:Cost:UsdPerAiCredit`. Wording changed from premium requests to AI credits everywhere.
- **Reasoning effort and custom models.** A player id can carry an effort (`model@effort`); the gateway lists the efforts a model
  supports and prices a variant by its base model. `Arena:CustomModels` adds models through the SDK provider configuration, and the
  AppHost passes them on.
- **Prompt styles.** `classic` (the earlier prompt, unchanged), `board` and `think`, selected as `~style` on the player id, so each
  configuration is ranked on its own. Pickers on Play, Tournaments, Benchmark and Broadcast; a style filter and a "Prompt styles
  compared" section on Results.
- **Benchmark runs.** Variants (`model@effort`) play a benchmark run with a JSON and Markdown report, a `/benchmark` page,
  `scripts/benchmark.sh` and `scripts/benchmark.ps1` (they refuse to run under CI), and `docs/BENCHMARK.md`.
- **Public static site.** The results, head-to-head, games list, tournaments, an about page, one page per game and the JSON
  data are exported as static pages without script, and can be published to the `gh-pages` branch from an admin page or
  automatically.
- **Results archive.** Every rated game can be exported as one JSON file in `results/` (`docs/RESULTS-ARCHIVE.md`); CI builds the
  public site from those files. `CONTRIBUTING.md` describes how to contribute results.
- **Results page.** A model map: a bubble chart of two chosen coefficients.
- **Live page.** A Pace control: the view runs a chosen number of seconds behind the game (default `Arena:Viewer:MinMovePauseSeconds`);
  the game itself is not slowed.
- **Viewer-paid games.** Gateway providers for Anthropic, OpenAI and DeepInfra API keys, a request core (menu, price quote,
  ledger, queue, intake), a YouTube Super Chat intake, and a broadcast runner that plays the queue with a per-game budget guard
  (`docs/REQUESTS.md`). With `Arena:Requests:RequireApiKeyModels` on, a paid game never runs on the Copilot subscription.
- **Broadcast planning.** Games are planned one by one with per-game model options, cost estimates and queue rules; the live page
  has navigation, indicators, a layout that fits one screen in landscape and portrait, and an intermission state between
  scheduled games.
- **Public site from Actions.** The `publish-site` workflow builds the public site from the results archive (`results/`) and
  deploys it to GitHub Pages (Settings, Pages, Source = GitHub Actions). The workflow checks that the site has no script and fits
  the Pages limit, and writes the rating history from the archive. Undecided games (a cancelled tournament's queued games, a game
  cut off by a restart) are not on the site. The in-app *Publish* to a branch stays as an advanced option for your own database.
  `deploy-pages` now only checks the one-pager.
- **Results archive checks.** A game file is checked more strictly (standard start position, model-id characters, bounds on
  tokens, latency and date, and one spelling per model), and the CI archive test replays every file.
- **Planner.** The broadcast plan starts in one call (`POST /api/broadcast/schedules`): it validates the games, saves the schedule
  and starts it, and a refusal saves nothing. Drafts can be deleted; Stop asks for a second click; the list refreshes while a day runs.
- **Live broadcast page.** A wider 16:9 layout with larger type, `?theme=light` and `?view=mobile`, an intermission card with a
  countdown before each scheduled game, and a replay only when no game is scheduled next. The page keeps running through a slow or
  failed read of the API.
- **Navigation and pages.** The strip lists Results, Live game, Head-to-head, Tournaments, Benchmark, Live broadcast, Broadcast
  plan and Games, with Attempts and Site under **More**. Unknown addresses answer 404 inside the layout. A status strip shows
  the Copilot, database and Stockfish states. Filters, the chosen game and the pager are kept in the address, and the replay page
  has a slider. Pages show plain words when the API is away, keep their last data and ask again.
- **Custom model prices.** A custom model with a price pair is priced in the estimates and the game summary.
- **PostgreSQL tests.** `tests/Chess.Api.Tests/Postgres` runs the real app against a PostgreSQL server (`ARENA_TEST_POSTGRES`), and
  the CI job `postgres` also checks that the model matches the migrations (`dotnet ef migrations has-pending-model-changes`).

### Changed

- A move whose every attempt was a gateway error aborts the game, unrated, instead of forfeiting the side, in every game. A
  tournament or benchmark then starts no new game for 15 s, doubling to 2 minutes (`Arena:TechnicalCooldown`,
  `Arena:MaxTechnicalCooldown`; ADR-017, `docs/RATING.md` §2).
- The Copilot runtime starts in the background, so the API answers while it starts; opening a conversation has a timeout
  (`Copilot:SessionStartTimeout`) and a conversation's session is deleted when it ends.
- The Results and rating endpoints are faster: the report and the pairwise matrix share one bootstrap, the last computation is
  kept in memory until a game finishes, move statistics are summed in the database, and the rating history sends at most 200
  points per model.
- Unsafe requests (POST, DELETE) are not retried after a failure, so one click cannot create a tournament, benchmark or plan
  several times.
- The broadcast clock is one rule: the first game starts when you press Start, each next one a game gap after the previous one
  left *running*, and the planned times follow what happened. A restart plays the next game at once.
- A request or a game left over by a restart is closed as interrupted, never played unpaced, and a viewer request whose model
  catalog is not ready yet is tried again after 5 s.
- The Stockfish analysis has a handshake and a search timeout (`Analysis:HandshakeTimeoutSeconds`, `SearchTimeoutSeconds`,
  `RetryAfterSeconds`); a binary that does not answer is reported as Degraded and the arena carries on without it.
- The random player shows as free in the cost estimates, and a model billed by token is counted in AI credits instead of "unknown
  multiplier". A rating that rounds to zero shows no sign.
- The API and the web app answer 403 to a foreign `Host` header (DNS rebinding), as they do to a foreign `Origin`.
- The tournament and benchmark forms start on concurrency 1 and say what is missing only after you touch them; both API calls
  check the models, the game counts and the Copilot state, and answer 503 while Copilot is not ready.
- Repetition: a position counts as equal only when the en passant capture is legal; `LoadFen` rejects an invalid position; the PGN
  has a Termination tag.
- CI: job timeouts, a run on a pull request is cancelled by a newer push, no Copilot CLI download, and the Stockfish tests run.

- The leaderboard order is conservative: by the lower bound of the arena score interval (`docs/RATING.md`).
- The live game page keeps the board in place: the player bars and the status line have fixed heights, and the live move list
  shows the newest move on top.
- The broadcast page cleans up its stream connections, and the HTTP clients for ratings and results are more resilient.
- Dependencies: GitHub.Copilot.SDK 1.0.28, Microsoft.EntityFrameworkCore and InMemory 10.0.14, Microsoft.AspNetCore.Mvc.Testing
  10.0.14, bunit 2.13.3 and the `dotnet-ef` tool 10.0.14. CI resolves the .NET SDK from `global.json` again.

### Removed

- The viewer-paid request panels on the broadcast pages, and the public site's menu and price page. The request intake, the queue,
  the ledger and `/admin/requests` remain; the owner is deferring the decision on what viewers see.
- The mini leaderboard of the broadcast page and its `?top=` option.

### Fixed

- `/api/ratings` and `/api/ratings/matrix` answered 500 on PostgreSQL, so the Results page did not load: the query ordered a
  projected record, which Npgsql cannot translate. It now orders before projecting.
- Aborting a game that had already finished reported success; it now reports false.
- The broadcast keeps its intermission while a replay is still looking for a game.
- The broadcast stage froze when one read of the API timed out; every read is now cut after 20 s and retried on the next poll.
- A viewer-paid game could run on the Copilot subscription when its menu id was also a Copilot model id: the API now stops at
  start-up with a message that names the id, and a request that names such a model is refused and credited. A cancel and a
  restart recovery no longer race with the runner.
- The site export failed on a game without a PGN (an undecided game); such games are left out.
- `scripts/smoke-copilot.sh` waited for the wrong ready state, `scripts/benchmark.ps1` had no CI guard, and
  `scripts/broadcast-smoke.sh` needed GNU `date`.

## [0.1.0] - 2026-10-24

The first release of the .NET arena. It replaces the earlier Python and Streamlit application, which is no longer in the
repository.

### Added

- **Arena app.** A local .NET 10 solution: an Aspire AppHost that starts PostgreSQL in a container, the API on
  `http://127.0.0.1:5080`, and the Blazor web app on `http://localhost:5070`. A `fake` launch profile runs without Docker or a
  Copilot login.
- **Chess engine.** FEN, SAN, legal move generation, game status, PGN writer and tolerant move notation, verified by perft
  counts for standard positions.
- **LLM gateway.** `CopilotLlmGateway` on `GitHub.Copilot.SDK` (the only project that references it), a deterministic
  `FakeLlmGateway`, and a `copilot` health check that reports `Ready`, `NotAuthenticated` or `Unavailable` as Degraded, never
  Unhealthy.
- **Players and game runner.** LLM and random players. A move is accepted only when it names exactly one legal move. A rejected
  reply gets one corrective follow-up, and after `MaxAttemptsPerMove` attempts the side forfeits; there is no fallback move.
  Games run on a bounded background runner with live events over Server-Sent Events.
- **Rating.** The published formula in `docs/RATING.md`: a penalised Bradley–Terry fit with a White advantage, the relative
  coefficient RQC, arena score, a seeded bootstrap for 95 % intervals, and provisional flags below 20 rated games.
- **Move quality.** Stockfish analysis over UCI, run by a post-game pipeline. Without Stockfish move quality is disabled and the
  ranking uses legality only.
- **Tournaments.** Round robins with colour balance, progress, cancellation and a premium-request estimate.
- **Results page.** The Big Picture first (headline and ranked bar chart with intervals), then rating over time, the
  head-to-head matrix, results breakdown, move quality, strength against cost, and a full leaderboard table. Built from SVG chart
  components, with a table fallback.
- **Other pages.** Live game, head-to-head comparison, tournaments, game archive with replay and PGN download, and an attempts log.
- **Persistence.** Everything is stored in PostgreSQL (games, moves, every LLM attempt, tournaments, rating snapshots, broadcast
  schedules). The schema is applied by a startup migration, and games left unfinished by a restart are marked `interrupted`.
- **Broadcast mode.** A 24-hour channel plan: a pure planner with a premium-request estimate, a paced sequential runner that
  resumes after a restart and backs off on Copilot failures, API endpoints, a planning page, and the `/broadcast` page for OBS.
  `docs/BROADCAST.md` is the runbook.
- **AI evals.** An offline suite of 30 scripted positions run through the move parser and player, which can fail, and
  `scripts/smoke-copilot.sh` for the owner's live run.
- **Health.** `/health` and `/alive` on every service, and `/api/status` with the Copilot, database and Stockfish states.
- **Repository baseline.** GitHub Actions CI (build with warnings as errors, tests, gitleaks over the commit history, a
  ServiceDefaults size guard, a Copilot-SDK-only check, and a solution and AppHost registration check), Dependabot for NuGet and
  Actions, CODEOWNERS, a pull request template, and `scripts/dev-setup.sh` and `scripts/setup-hooks.sh`.

### Changed

- The Results and head-to-head pages use one significance rule: a result is significant only when its interval excludes 0.5 by a
  real margin, the interval is not degenerate, and neither model is provisional.

### Removed

- The Python and Streamlit application, its requirements file and its OpenAI dependency.

### Fixed

- The web app renders interactively: `Routes` and `HeadOutlet` declare the interactive server render mode.
- The scoped component styles bundle is linked, so pages render with their styles.
- Tournament pages re-render after their background refresh.
- `/api/status` reports the gateway state (`Ready`, `Fake`, `NotAuthenticated`, `Unavailable`) that the live game page depends on.

# Changelog

All notable changes to this project are documented in this file. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

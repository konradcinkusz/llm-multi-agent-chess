# LLM Chess Arena

[![CI](https://github.com/konradcinkusz/llm-multi-agent-chess/actions/workflows/ci.yml/badge.svg)](https://github.com/konradcinkusz/llm-multi-agent-chess/actions/workflows/ci.yml "CI workflow: build, test, secret scan and architecture guards")
[![License: MIT](https://img.shields.io/github/license/konradcinkusz/llm-multi-agent-chess)](LICENSE "MIT license")
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/download "Built on .NET 10 (global.json)")

A local arena that ranks the LLMs available through your GitHub Copilot subscription by playing them against each other in chess, and publishes the ranking with a deterministic, documented formula.

It runs on `localhost` only. Nothing is deployed. Every game, move, LLM attempt and rating snapshot is stored in PostgreSQL.

## Contents

- [Prerequisites](#prerequisites)
- [Quickstart](#quickstart)
- [URLs](#urls)
- [Pages](#pages)
- [How ratings work](#how-ratings-work)
- [Prompt styles](#prompt-styles)
- [Broadcast mode (24 h channel)](#broadcast-mode-24-h-channel)
- [Public results site](#public-results-site)
- [Benchmark your own model](#benchmark-your-own-model)
- [Cost: AI credits per move](#cost-ai-credits-per-move)
- [Data](#data)
- [Troubleshooting: the `/health` states](#troubleshooting-the-health-states)
- [Project layout](#project-layout)
- [Tests and CI](#tests-and-ci)
- [Documentation](#documentation)
- [Contributing and license](#contributing-and-license)

## Prerequisites

| Needed for | What |
|---|---|
| Everything | **.NET 10 SDK.** `global.json` pins the 10.0 feature band. Install: `sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0` on Ubuntu, or the [Microsoft installer](https://dotnet.microsoft.com/download). |
| Every build | **The Copilot CLI bundle.** The first `dotnet build` downloads it: about 89 MB from the [github/copilot-cli releases](https://github.com/github/copilot-cli/releases) page on github.com, and its SHA-256 is checked against `SHA256SUMS.txt` from the same host. The first build therefore needs internet access. The bundle is then cached under `src/Chess.Llm/obj`. |
| The browser journeys (`tests/Chess.E2E`) | **A Chromium.** Point `E2E_CHROMIUM_PATH` at one, or run `pwsh tests/Chess.E2E/bin/Debug/net10.0/playwright.ps1 install chromium` once. See [Tests and CI](#tests-and-ci). |
| The default profile | **Docker**, running. The AppHost starts PostgreSQL in a container. |
| LLM players | **GitHub Copilot CLI** (`copilot`) installed and logged in on this machine. The app uses that login, or the `copilot-github-token` AppHost secret parameter when you set one. The `fake` profile and `random` players do not need it. |
| Move-quality analysis (optional) | **Stockfish** on `PATH`, or the path in `Analysis:StockfishPath` in `src/Chess.Api/appsettings.json`. Without it the arena still works; move quality is disabled and the ranking uses legality only. |

Run `scripts/dev-setup.sh` to see what is installed and what is missing. It only reports; it never installs.

## Quickstart

Clone the repository and change into it, then:

```bash
dotnet run --project src/Chess.AppHost --launch-profile fake
```

The `fake` profile needs **no Docker and no Copilot login**. It runs a deterministic fake gateway (models `fake-a` and `fake-b`) and an in-memory store, so nothing you do with it is kept after the app stops.

Open <http://localhost:5070>. Stop the app with `Ctrl+C`.

For the real setup, with PostgreSQL and your Copilot models:

```bash
dotnet run --project src/Chess.AppHost
```

This starts the PostgreSQL container with the named volume `chess-arena-pg`, then the API and the web app. Before you start a game with an LLM, check `copilot` on the [status](#troubleshooting-the-health-states) line: it must say `Ready`.

## URLs

| What | Address | Notes |
|---|---|---|
| Arena UI | <http://localhost:5070> | The Blazor app. The only origin a browser needs. |
| API | <http://127.0.0.1:5080> | Health at `/health`, liveness at `/alive`, status at `/api/status`, models at `/api/models`. |
| Aspire dashboard | <http://localhost:15070> | Logs and resources. The console prints a login link when the AppHost starts. |
| Broadcast page for OBS | <http://localhost:5070/broadcast> | Chrome-less 1920×1080 page for a Browser Source. See [Broadcast mode](#broadcast-mode-24-h-channel). |

## Pages

The navigation lists them in this order:

| Page | Route | What it shows |
|---|---|---|
| **Results** | `/results` | The ranking. The **Big picture** comes first: a headline naming the best model, with its arena score and rating, then a ranked bar chart with 95 % intervals. Next, the **Model map**: every model on two coefficients you pick (rating, arena score, first-attempt legality, move quality, latency, tokens, cost, forfeit rate). Below it: rating over time, head-to-head matrix, results breakdown, move quality, strength against cost, and a full table. A tournament filter and an **Order** control sit above the Big picture. The order is **Arena score** (the default) or **Conservative**, which ranks by the lower bound of the arena score, so a model with more games outranks one with the same score and fewer (RATING.md §6). A **style filter** (All, then one chip per prompt style that has played) shows only the players of one style: it hides rows and never refits the ratings. The **Prompt styles compared** section, after the full table, gives each model played under two or more styles its rating per style, with the 95 % interval, legality, move quality and cost per game, and each style's verdict against the baseline (see [Prompt styles](#prompt-styles)). |
| Live game | `/` | Pick a model for each side (or `random`), start a game and follow it. Each side's bar shows its model, whose turn it is, the attempt in progress with its seconds, the pieces it has captured, and its tokens and estimated cost. Below the board, the **move table** lists every move; picking one selects its entries in the **engine log**. The engine log lists every reply the models sent to the engine, newest first: the move, whether it was legal or why it was rejected, the latency, the tokens, the cost, and the model's reasoning when it exposes it. Rejected attempts appear under the board. When the game ends, the **game summary** gives the tokens, AI credits and estimated cost per side, and the PGN can be shown. The **Pace** control sets how far the view runs behind the game: Live, or moves at least 1, 2, 3 or 5 seconds apart. Catch up shows every move at once. The game itself is not slowed. The default pace is 2 s, set by `Arena:Viewer:MinMovePauseSeconds`. |
| Head-to-head | `/head-to-head` | Two models: relative coefficient with its interval, a plain-language reading, expected score, direct record and direct games. |
| Tournaments | `/tournaments`, `/tournaments/{id}` | Round-robin creation with the AI-credit and USD estimate shown before you confirm, progress, a per-tournament leaderboard, and cancel. |
| Benchmark | `/benchmark`, `/benchmark/{id}` | One candidate against every other model: the estimate before you start, progress, a cancel button, and the report with downloads (Markdown and JSON). See [Benchmark your own model](#benchmark-your-own-model). |
| Games | `/games`, `/games/{id}` | Paged archive. Replay shows the same move table, engine log and game summary as the live page, with each move's attempts, latency, centipawn loss and best move, and a PGN download. |
| Attempts | `/admin/attempts` | Every LLM attempt with its prompt and reply, filterable by game, model and legality. |
| Broadcast plan | `/broadcast/plan` | Plan a day of games, preview every slot and the AI-credit and USD estimate, save a draft, start, pause, resume or stop. |
| Site | `/admin/site` | Export the public results site, publish it to the `gh-pages` branch, and see the last export and publish. See [Public results site](#public-results-site). |
| Broadcast (OBS) | `/broadcast` | Not in the navigation. The page OBS captures. |

Copilot's `auto` model is hidden from the model pickers unless `Arena:ShowAutoModel` is `true`: it picks a different model for each request, so it is not one player.

The status strip under the header of every page shows the Copilot, database and Stockfish states from `/api/status`. The OBS page has no app chrome, so it has no strip.

## How ratings work

The full formula is in [docs/RATING.md](docs/RATING.md). In short:

1. **Results come first.** Who won a game is 75 % of the relative coefficient. A side gets three attempts per move (`Arena:MaxAttemptsPerMove`); if all three are rejected, it forfeits the game. A rejection is an illegal or unparseable reply, a gateway error, or a timeout. A reply that takes longer than 180 s (`Arena:MoveTimeoutSeconds`) is a rejected attempt of kind `timeout`, and the request is aborted.
2. **Strength is a Bradley–Terry fit** over every finished rated game, anchored so that `random` is 0. A rating of 400 means an expected score of about 0.91 against `random`.
3. **The relative coefficient, RQC(A, B),** is `0.75 × expected score + 0.25 × quality share`. 0.5 means equal, and RQC(A, B) + RQC(B, A) = 1.
4. **Quality** is legality, plus Stockfish centipawn loss when Stockfish is available. Latency and cost never enter the score; the Results page shows them beside it.
5. **Every number carries its confidence.** A 95 % bootstrap interval travels with each rating and coefficient. A model with fewer than 20 rated games (or a very wide rating interval) is *provisional*, and a result is called *significant* only when its interval excludes 0.5 and neither model is provisional.

## Prompt styles

The prompt is part of the player. A player id is `model[@effort][~style]`, for example `claude-sonnet-5.5@medium~think`.
Each id is its own player in the one ranking, so two prompts for the same model are rated against each other on one scale.
A plain id means `classic`, the prompt the arena has always used, so existing games and ratings keep their meaning. Writing
`~classic` is refused (write the plain id), and `random` takes no style.

| Style | What the model is sent | What it answers | Conversation |
|---|---|---|---|
| `classic` | the FEN, the last ten plies and the legal moves | one SAN move | one per game |
| `board` | the goal, an ASCII board, each side's pieces, the material, the whole game in PGN, the FEN and the legal moves in sorted order | one SAN move | one per move |
| `think` | as `board`, plus the plan carried from the last move | a short analysis, then a `Plan:` line and a `Move:` line | one per move |

Board and think open one conversation per move. Each of their prompts already carries the whole game, so one conversation per
game would resend every earlier prompt. No style gets engine help: nothing says what is attacked, hanging or best, so a style
measures the model, not Stockfish.

A style costs what its prompt and reply cost. `think` writes more output tokens: on the fake profile it averaged about 43
output tokens a move, against about 13 for `classic`. The cost estimate uses the player's own token history when it has any,
else its model's, and it says which one it used.

Every page that starts games has a style picker: Play (one per side), Tournaments and the broadcast planner (a multi-select,
so every chosen model plays every chosen style), and Benchmark (one for the candidate, one for the opponents). To compare
styles, run a tournament with two of them and read **Prompt styles compared** on the Results page, or benchmark a styled
candidate. The method and a recorded fake run are in [docs/BENCHMARK.md §8](docs/BENCHMARK.md#8-prompt-styles-candidates-and-opponents).

## Broadcast mode (24 h channel)

Broadcast mode plans a day of games in advance, plays them one at a time with pauses so viewers can follow, and gives OBS one clean page to capture. The games are ordinary rated games, so the Results ranking keeps growing with the channel.

**The app does not stream video.** OBS captures `http://localhost:5070/broadcast` as a Browser Source at 1920×1080 and streams to YouTube. The app never sees the stream key.

The planner (`/broadcast/plan`) shows every slot and the AI-credit and USD estimate before anything is saved. Start the day once, and it plays through its slots in order, one game at a time. A restart resumes the day at its next unfinished slot.

The full runbook is [docs/BROADCAST.md](docs/BROADCAST.md): planning, OBS setup, YouTube settings, keeping the machine awake, and what happens on a restart or a Copilot outage.

## Public results site

The results, head-to-head, games, tournaments and this one-page description can be published as a static site on
GitHub Pages ([ADR-013](docs/architecture/05-DECISIONS.md)). The app makes a snapshot on your machine and pushes it to the
`gh-pages` branch. Nothing runs online: no server, no database and no script on the public side, and the site lags the
live app by at most the auto-publish interval. The live game stays on YouTube.

**One-time setup, in this order.**

1. Open `/admin/site` in the app and press **Publish**. That creates the `gh-pages` branch on the remote. GitHub offers no
   branch to serve until it exists.
2. On GitHub: Settings → Pages → Source: *Deploy from a branch*, branch `gh-pages`, folder `/ (root)`.

The `deploy-pages` workflow no longer deploys anything; it only checks that `docs/index.html` still assembles.

**Publishing.** Open `/admin/site` in the app. *Export* writes the site to `Arena:Site:ExportDirectory` (the folder is
emptied first). *Publish* exports afresh and force-pushes the export to `gh-pages` as one commit, so the branch never
grows a history. The app uses the `git` on your machine and its existing credentials; it stores none. A failed step is
shown on the page with git's message, and `main` and your working tree are never touched.

**What becomes public.** Everything on the site, including prompts, replies, reasoning and costs. Nothing else is sent:
no configuration, no secrets, no attempts of aborted games.

| Option (`Arena:Site:…`) | Default | Meaning |
|---|---|---|
| `Remote` | the `origin` of the repository the app runs from | Where to push. A local path also works. |
| `Branch` | `gh-pages` | The branch GitHub Pages serves. |
| `WorkDir` | a folder in the system temp directory | The publisher's own clone, deleted and recreated on each publish. Give it a folder of its own. |
| `ExportDirectory` | a folder in the system temp directory | Where the export is written, emptied before each export. |
| `PublicUrl` | `https://<owner>.github.io/<repo>/`, derived from a GitHub remote | The address shown on the page. |
| `AutoPublishMinutes` | `0` (off) | Publish every N minutes when a game has finished since the last publish. A restart does not publish by itself. |
| `YouTubeUrl` | unset | The "Watch live" link. Not shown when empty or not an http(s) address. |
| `SupportLinks` | none | Support links, each with a `Label` and a `Url`. Only http(s) links are shown. |
| `FullLogGames` | `300` | The newest this many games keep their engine log on the site. Older games say so instead of showing a log. |

## Benchmark your own model

Benchmark one model, or one model at one reasoning effort (`model@effort`, such as `fake-a@high` on the fake profile), against
the other models: 30 rated games against each opponent, colours alternating. By default the opponents are every enabled model
except the candidate's own model (at any effort and style), the random player included, and Copilot's `auto` model is never one.
The candidate may carry a prompt style too (`--candidate fake-a@high~think`), and so may each opponent. The result is a Markdown
report to share, and the same numbers as JSON. The `/benchmark` page runs the same benchmark with a progress view.
Your own model works too, behind any OpenAI-compatible endpoint (vLLM, Ollama, LM Studio).

With the fake profile running (`dotnet run --project src/Chess.AppHost --launch-profile fake`), this writes the report to
`~/benchmarks`:

```bash
scripts/benchmark.sh --candidate fake-a@high --out-dir ~/benchmarks
```

On Windows or in PowerShell 7, use `pwsh ./scripts/benchmark.ps1 -Candidate fake-a@high -OutDir ~/benchmarks`. For Copilot models,
run the default profile and use an id from `/api/models`. The scripts refuse to run under CI.

The guide covers the cost and how to cut it, configuring a custom model and keeping its key out of files, and how to read the
intervals: [docs/BENCHMARK.md](docs/BENCHMARK.md).

## Cost: AI credits per move

Copilot bills the models by tokens, in **AI credits**. Each move costs its input and output tokens at the model's token prices,
and each rejected attempt is billed the same way. The app shows the cost of every reply, game and estimate in AI credits, with a
US-dollar figure at `Arena:Cost:UsdPerAiCredit` (default **0.01 USD** per credit). Both are estimates. The app does not check the
rate against your invoice.

- **Where a game's figures come from.** A game's cost is the sum of its replies. Each reply uses the credits the runtime reports
  for it; when the runtime reports none, the app prices the reply from the model's token prices. The game summary's *Basis*
  column says what it had: `reported` (the runtime reported every reply), `estimated` (the app priced at least one reply from
  token prices), `partial` (some replies had no cost), `unknown` (none had one), or `free` (the random side).
- **Unverified conversion.** The runtime reports usage in nano-AI units. The app assumes 1 AI unit = 1 AI credit. The owner's live
  run has not confirmed it yet; [`scripts/smoke-copilot.sh`](scripts/smoke-copilot.sh) prints the credits each request reports.
- **Estimates before you commit.** The tournament form, the benchmark page and the broadcast planner estimate the cost before
  anything is saved. The estimate is the expected moves per side (40) × the tokens per move × the model's prices. A model with
  stored moves uses its own average tokens per move; one without is assumed to use 700 input and 300 output tokens per move.
  A model whose prices are not listed shows an unknown cost, and the total leaves it out.
- **Premium requests.** A model that still reports a premium multiplier also gets a premium-request figure on the estimate forms.
  A model that does not shows that figure as unknown.
- **Cost never affects the ranking.** It is shown beside the score, and the rating formula leaves it out ([RATING.md §8](docs/RATING.md#8-what-is-deliberately-not-in-the-score)).
- `random` players and the `fake` profile cost nothing.
- `Arena:MaxConcurrentGames` (default 2) caps how many games run at once when they come from `POST /api/games` or a tournament. A broadcast plays one game at a time **outside** that cap, by decision (ADR-011). So a tournament running during a broadcast adds LLM requests on top of the broadcast's. Do not run both at once, or lower the tournament's concurrency.
- A game that reaches the 200-ply cap plays up to 100 moves a side, 2.5 times the estimate, because the estimate assumes a normal game length. Retries are billed too and are not in the estimate, so a real bill can be higher. The planning guide has the numbers: [docs/BROADCAST.md §2](docs/BROADCAST.md#2-cost).

Check the USD estimate on the planner before you run a day.

## Data

Everything is in PostgreSQL: games, every move, every LLM attempt with its prompt and reply, tournaments and benchmark runs, the rating history (`rating_snapshots`), and broadcast schedules and slots.

- The default profile keeps the data in the Docker named volume **`chess-arena-pg`**, so it survives restarts of the app and of the container.
- The leaderboard is recomputed from the stored games on every read. Every number on screen can be reproduced from the games table.
- The `fake` profile and the tests use an in-memory store. Its data is gone when the app stops.
- To use an existing PostgreSQL instead of the container, set the AppHost connection string `ConnectionStrings:chess` in the AppHost user secrets. No container is started then.

## Troubleshooting: the `/health` states

`/health` reports three integrations. `/api/status` returns the same states as JSON: `copilot` is the gateway's own state (`Ready`, `Fake`, `NotAuthenticated` or `Unavailable`), `database` is the database check, and `stockfish` is `available` or `absent`. The app keeps running in every state: a missing Copilot login only disables LLM players.

```bash
curl -s http://127.0.0.1:5080/api/status
curl -s http://127.0.0.1:5080/health
```

| Check | State | Meaning and what to do |
|---|---|---|
| `copilot` | `Ready` (Healthy, *Signed in as …*) | LLM players are available. |
| `copilot` | `Fake` (Healthy) | The `fake` profile is running. No Copilot is used. |
| `copilot` | `NotAuthenticated` (Degraded, *Not authenticated*) | Run `copilot` in a terminal and log in, then restart the AppHost. Until then only `random` plays, and `/api/models` answers with a `Warning` header. |
| `copilot` | `Unavailable` (Degraded, *Copilot runtime unavailable: …*) | The runtime could not start. The detail names the cause. Check that the Copilot CLI is installed, or the path in `Copilot:CliPath`. A game with an LLM side is refused with a 503 that says to run `copilot` and log in. |
| `database` | Healthy, *ready* | Migrations ran. |
| `database` | Degraded, *migrating* | Wait a few seconds; the app is applying migrations on start. |
| `database` | Unhealthy, with the error message | PostgreSQL is not reachable or the migration failed. Check that Docker is running (default profile) and read the error in the Aspire dashboard. |
| `stockfish` | Healthy, *available* | Move quality is computed for finished games. `/api/status` reports `"stockfish": "available"`. |
| `stockfish` | Healthy, *absent — move quality disabled* | Normal without Stockfish. `/api/status` reports `"stockfish": "absent"`. Install it, or set `Analysis:StockfishPath`. |

The Copilot check is never Unhealthy, and `/health` answers HTTP 200 even when a check is Degraded. Tell the states apart by the `status` field of each check, not by the HTTP code.

**403 `Cross-origin request rejected`.** The API and the web app refuse any request whose `Origin` header names another site, or whose `Sec-Fetch-Site` is `cross-site` (ADR-006, amended). A browser sends those headers when a page on another site makes the call. A 403 therefore means a foreign page reached the app through your browser, not that the app is broken. Open the arena at <http://localhost:5070> or <http://127.0.0.1:5070>. To allow another origin of your own, add it to `Arena:AllowedOrigins` in the API's configuration. Requests with no `Origin` header, such as `curl`, are allowed.

## Project layout

```
src/
  Chess.AppHost/          Aspire composition root: PostgreSQL, Chess.Api, Chess.Web
  Chess.ServiceDefaults/  kernel: OpenTelemetry, /health and /alive, problem details (no domain)
  Chess.Engine/           pure chess rules: FEN, SAN, legal moves, status, PGN
  Chess.Llm/              ILlmGateway, CopilotLlmGateway, FakeLlmGateway (the only project that references the Copilot SDK)
  Chess.Analysis/         IMoveQualityAnalyzer, Stockfish (UCI) and no-op analyzers
  Chess.Rating/           pure rating mathematics (docs/RATING.md): no I/O, no clock, no EF
  Chess.Api/              the one service: players, game runner, tournaments, benchmarks, results, broadcast, cost (usage), persistence, endpoints
  Chess.Web/              Blazor Web App (interactive server rendering): the UI and the public site export; talks to the API over HTTP only
tests/
  Chess.Engine.Tests/  Chess.Rating.Tests/  Chess.Llm.Tests/  Chess.Api.Tests/  Chess.Web.Tests/
  Chess.E2E/              Playwright journeys on the fake profile (see Tests and CI)
docs/
  RATING.md  BROADCAST.md  BENCHMARK.md  EVALS.md  architecture/
scripts/                  dev-setup.sh, setup-hooks.sh, smoke-copilot.sh, broadcast-smoke.sh, benchmark.sh, benchmark.ps1
```

The allowed project references and the dependency budget are in [03-TARGET-ARCHITECTURE §2](docs/architecture/03-TARGET-ARCHITECTURE.md).

## Tests and CI

```bash
dotnet build Chess.sln -warnaserror
dotnet test Chess.sln
```

`dotnet test Chess.sln` runs every test project, the browser journeys included. It needs a Chromium for the journeys (below). With one available, the run gives 1 451 tests: 1 448 pass and 3 are skipped.

| Project | Passed | Skipped |
|---|---|---|
| `Chess.Engine.Tests` | 136 | 0 |
| `Chess.Rating.Tests` | 30 | 0 |
| `Chess.Llm.Tests` | 74 | 0 |
| `Chess.Api.Tests` | 519 | 3 |
| `Chess.Web.Tests` | 684 | 0 |
| `Chess.E2E` | 5 | 0 |

The three skipped tests are the Stockfish integration tests; they run only when `STOCKFISH_PATH` names a Stockfish binary.

The unit tests and the browser journeys run separately:

```bash
# Unit tests only: no browser. This is what the CI "build and test" job runs (1 542 pass, 3 skipped).
dotnet test Chess.sln --no-build --filter "FullyQualifiedName!~Chess.E2E"

# The browser journeys only, with a Chromium you already have.
E2E_CHROMIUM_PATH=/path/to/chromium dotnet test tests/Chess.E2E
```

The five journeys in `tests/Chess.E2E` (live game, replay, the Results Big picture, a round-robin tournament, a tiny benchmark to its report) start the AppHost on the fake gateway and drive a real Chromium. They never download a browser. `ArenaFixture` uses `E2E_CHROMIUM_PATH` when it is set, then the Playwright cache. When neither holds a browser, the run fails and names the command to run.

The offline AI evals (30 scripted positions) are part of the unit run. To run only them:

```bash
dotnet test tests/Chess.Api.Tests --filter "FullyQualifiedName~Chess.Api.Tests.Evals"
```

Tests need no network and no Copilot login. They use the `FakeLlmGateway`, the in-memory store and fixtures.

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) runs on every pull request and on pushes to `main`:

- **build and test**: `dotnet build Chess.sln -warnaserror`, then the unit tests (the `Chess.E2E` filter above), with the results uploaded. It needs no browser.
- **end-to-end journeys**: builds `tests/Chess.E2E`, installs Chromium with its system dependencies, and runs the five journeys.
- **secret scan**: gitleaks over the full history.
- **kernel size guard**: `Chess.ServiceDefaults` stays at or below 400 lines of C#.
- **Copilot SDK only in Chess.Llm**: no other project may reference `GitHub.Copilot.SDK`, and no code outside `Chess.Llm` may use a `GitHub.Copilot` namespace.
- **solution and AppHost**: every project is in `Chess.sln`, and every web project is registered in the AppHost.

The live Copilot check is a script for the owner, not a CI job, because every move is billed in AI credits: [`scripts/smoke-copilot.sh`](scripts/smoke-copilot.sh) plays one game against a model and refuses to run under CI.

## Documentation

- [docs/RATING.md](docs/RATING.md): the published rating formula and its test vectors.
- [docs/BROADCAST.md](docs/BROADCAST.md): the 24 h channel runbook.
- [docs/BENCHMARK.md](docs/BENCHMARK.md): benchmarking a model: options, cost, your own endpoint, and reading the report.
- [docs/EVALS.md](docs/EVALS.md): the AI evals and the live smoke test.
- [docs/architecture/](docs/architecture/): the architecture: current state, target architecture, migration plan, decisions and the atomic task list.
- [CLAUDE.md](CLAUDE.md): the working rules and invariants for contributors and coding agents.
- [CHANGELOG.md](CHANGELOG.md): what changed in each release.

## Contributing and license

See [CONTRIBUTING.md](CONTRIBUTING.md) for the pull request workflow and the checks to run. Please read the [Code of Conduct](CODE_OF_CONDUCT.md).

Released under the [MIT License](LICENSE).

---

[![CI](https://github.com/konradcinkusz/llm-multi-agent-chess/actions/workflows/ci.yml/badge.svg)](https://github.com/konradcinkusz/llm-multi-agent-chess/actions/workflows/ci.yml "CI workflow") [![License: MIT](https://img.shields.io/github/license/konradcinkusz/llm-multi-agent-chess)](LICENSE "MIT license") [![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/download "Built on .NET 10")

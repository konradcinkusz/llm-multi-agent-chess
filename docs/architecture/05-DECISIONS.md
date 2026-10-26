# 05 — Decisions

Each record: context, decision, consequences, and the principle it applies or deviates from.
Every record is **Accepted** as of 2026-10-24 (T4.4 final review, see [ARCHITECTURE_REVIEW.md](ARCHITECTURE_REVIEW.md)).

---

## ADR-001 — Remove the Python implementation

**Status:** Accepted (2026-10-24).

**Context.** The repo carries two implementations of one product (Streamlit/Python and
.NET). The README documents only the Python one; recent work happened only in .NET.
**Decision.** Delete `src/*.py`, `tests/*.py`, `requirements.txt` and the root `.env.example`
in wave 0. The .NET engine is characterised by its own tests (T1.5) instead.
**Consequences.** One stack; `src/` becomes free for the .NET solution. The history keeps the
Python code if it is ever wanted. — P14 (a stale README is a finding).

## ADR-002 — Local only: Aspire is the runtime, nothing is deployed

**Status:** Accepted (2026-10-24).

**Context.** R1: the owner wants the app on their own machine, no deployment, no provisioned
agents (no Azure AI Foundry, no Fly.io).
**Decision.** The Aspire AppHost is the way to run the product, not just a dev convenience.
Postgres runs as an Aspire-managed container with a named data volume. No per-service
Dockerfile, no nginx, no `fly.toml`, no tag-driven deploy workflow. CI runs build, tests,
lint and the secret scan on every pull request.
**Deviation, recorded:** P6 (container per service), P7 (Fly.io), P12 (tag-driven deploy) are
**N/A** while R1 holds. P1's "the AppHost is not the production topology" is satisfied
trivially — there is no production topology. Reversal cost if deployment is ever wanted:
add Dockerfiles + `fly.toml` per the Fly guide; nothing in the code assumes localhost except
the candidate ladder's last rung.
**Consequences.** Docker is a prerequisite (for Postgres). `Database:Provider=InMemory` exists
for tests and for a no-Docker demo.

## ADR-003 — One service

**Status:** Accepted (2026-10-24).

**Context.** P3 asks for a service per bounded context.
**Decision.** Games, players, tournaments and ratings are one bounded context (`Chess.Api`):
ratings are a pure function of finished games, tournaments are schedules of games, and the
game runner holds the live game. Splitting them would ship the same aggregate over HTTP for no
gain — the same reasoning as CopilotScope's collector. Pure logic is still separated into
libraries (`Chess.Engine`, `Chess.Rating`, `Chess.Analysis`) and the vendor edge into `Chess.Llm`.
**Consequences.** One database, one owner — P3 holds.

## ADR-004 — GitHub Copilot SDK is the only LLM gateway

**Status:** Accepted (2026-10-24).

**Context.** R2: every model, session and request goes through the Copilot SDK on the owner's
subscription.
**Decision.**
- `Chess.Llm` is the only project referencing `GitHub.Copilot.SDK` (1.0.x, pinned centrally).
  The OpenAI package is removed. Models are listed from the SDK at runtime; there is no
  hard-coded model list.
- One SDK session per side per game: the system prompt is set once, the model keeps the game's
  context, and the corrective follow-up after an illegal reply is a natural turn in that
  conversation.
- No tools, reject-all permission handler (03 §3.1).
- Authentication: the logged-in Copilot CLI user by default; `Copilot:GitHubToken` as an
  optional Aspire secret parameter. No token in source or logs (P5).
- `FakeLlmGateway` for CI, tests and E2E, selected by `Llm:Provider=Fake`.
**Consequences.**
- **Cost.** Each move is at least one Copilot request; premium models consume premium requests
  at their multiplier. An 80-ply game between two premium models is ~80 premium requests ×
  multiplier. The models page shows the multiplier when the SDK reports it, the tournament
  form shows an estimate (`pairs × games × ~80 × multiplier`) before it starts, and
  `Arena:MaxConcurrentGames` defaults to 2.
- **Availability.** The SDK spawns the Copilot runtime; not being logged in is a degraded state
  reported by `/health`, not a crash (P8).
- The SDK's API changed between 0.1.x and 1.0.x; T1.4 starts with a 30-minute spike that
  confirms the exact calls against the pinned version and records them in `Chess.Llm/README.md`.

## ADR-005 — Keep Blazor, as a Blazor Web App with interactive server rendering

**Status:** Accepted (2026-10-24).

**Context.** The standard frontend is a Next.js product surface whose server side is the only
thing the browser talks to (FRONTEND-BFF §1). The current Blazor WASM app calls the API and a
SignalR hub through nginx. On 2026-10-24 the owner decided to **stay with Blazor**; the
standard itself will be revisited in `architecture-standards` later.
**Decision.**
- Convert `ChessBlazor` (WASM) into `src/Chess.Web`, a Blazor Web App with the interactive
  server render mode, run by the AppHost. The browser talks only to `Chess.Web`; `Chess.Web`
  calls `Chess.Api` with a server-side typed `HttpClient` through service discovery.
- Live updates: SSE from `GET /api/games/{id}/events`, consumed server-side by `Chess.Web`.
  The SignalR hub on the API and the browser-side SignalR client are removed; the game loop is
  server-side (ADR-007), so the browser never needs to send moves over a socket.
- No JS dependencies (same budget as CopilotScope's dashboard).
**Deviation, recorded:** FRONTEND-BFF's Next.js requirement. The property it protects — no token
or backend address in the browser, no CORS — is kept, because interactive server rendering is a
BFF by construction. Revisit when the standard changes.
**Consequences.** No Node toolchain. Old Blazor pages are ported, not rewritten; between wave 0
and wave 3 the app has no working UI (the old pages call endpoints that wave 0 removes). The
last fully working legacy state is commit `f0f5845`.

## ADR-006 — No authentication; bind to loopback

**Status:** Accepted (2026-10-24).

**Context.** P5's JWT/JWKS rule assumes a multi-user, networked estate. This is a single-user
tool on the owner's machine, and adopting `authservice` would add a second service and a login
for one person.
**Decision.** No identity provider. The API listens on `127.0.0.1:5080` only and the web app on
`localhost:5070`; CORS is not configured at all (the browser never calls the API). The admin
attempt log is therefore reachable only from the machine itself.
**Deviation, recorded:** P5 signing-key item N/A while R1 holds. Revisit together with ADR-002.

**Amended 2026-10-24: origin guard.** Loopback binding does not stop a web page on another site from reaching the
app through the owner's browser. A browser sends a cross-site `POST` without a preflight, and a websocket handshake is
not subject to CORS, so such a page could start a broadcast day (which spends Copilot premium requests) or open a Blazor
circuit (ARCHITECTURE_REVIEW H-1). The guard (`Chess.ServiceDefaults/OriginGuard.cs`, applied by both apps) answers 403
to any request whose `Origin` is not `localhost`, `127.0.0.1` or `[::1]` on the web or API port (plus
`Arena:AllowedOrigins`), and to any request with `Sec-Fetch-Site: cross-site`. Requests without an `Origin`, such as curl,
the web app's server-side calls, health probes and Playwright's request context, are allowed. The guard does not
authenticate the owner. It does not stop code already running on the owner's machine, another local user or process
that can reach the loopback ports, or a non-browser client that sends neither header. CORS is still not configured: the
guard refuses foreign origins, it does not grant any.

## ADR-007 — Ranking: server-side games, forfeits, Bradley–Terry recomputed on read

**Status:** Accepted (2026-10-24).

**Context.** The ranking is the product (R3). Today an LLM that answers garbage still finishes
games (first-legal-move fallback), games live in a browser connection, and nothing is rated.
**Decision.**
- Games are run by a hosted `GameRunner`; browsers observe.
- No fallback move: illegal replies are recorded and, after `MaxAttemptsPerMove`, forfeit.
- Strength is a Bradley–Terry fit (order-independent, with first-move advantage), not
  incremental Elo; the relative coefficient RQC blends expected score (0.75) with move quality
  (0.25). Full definition and test vectors: [`docs/RATING.md`](../RATING.md).
- The current leaderboard is computed on read from the games table by a pure function. Its
  history is persisted (`rating_snapshots`, one row per model after each finished rated game,
  with `formula_version`) so the rating-over-time chart survives restarts — added 2026-10-24
  when the owner required that everything is stored in Postgres. Snapshots are never fed back
  into a calculation.
**Consequences.** Changing the formula needs no migration; every number is reproducible. At
thousands of games the fit is still milliseconds; if it ever is not, cache by
`(max game id, options hash)`.

## ADR-008 — Stockfish is an optional external binary

**Status:** Accepted (2026-10-24).

**Context.** Move quality (ACPL) needs an engine; a package dependency would break the
dependency budget.
**Decision.** `Analysis:StockfishPath` (or `stockfish` on `PATH`) enables `StockfishAnalyzer`
over UCI; otherwise `NoOpMoveQualityAnalyzer` and `Q = L` (RATING.md §4). Reported in
`/health` (P8).

## ADR-009 — Retarget to net10.0 with central package management

**Status:** Accepted (2026-10-24).

**Context.** net8.0 leaves support on 2026-11-10; Aspire's current releases target net10.
**Decision.** Every project `net10.0`, versions in `Directory.Packages.props`, SDK pinned in
`global.json`. No Dockerfiles exist after T0.2, so the TFM/base-image coupling (P6) has nothing
to drift against.

## ADR-010 — Contracts are frozen in wave 0 so later waves can run in parallel

**Status:** Accepted (2026-10-24).

**Context.** The work is executed by several parallel agent sessions that each open one PR.
Parallel sessions that each invent a shared interface produce merge conflicts and three
incompatible shapes.
**Decision.** Wave 0 creates every project, every interface and record in
03-TARGET-ARCHITECTURE §3, and every `Add…()` registration call in `Program.cs` as a stub in
its own file. Every later task owns a disjoint set of files (06-TASKS, "Owns"). A task that
needs to change a frozen contract stops and reports to the supervisor instead.

## ADR-011 — Broadcast mode: a scheduled, sequential, paced 24-hour channel

**Status:** Accepted (2026-10-24).

**Context.** The owner will run the arena as a 24/7 YouTube channel. Today the app can start single
games and round-robin tournaments, but cannot (a) be planned ahead for a wall-clock window, (b) pace
games so humans can follow them, or (c) present one clean page for OBS.
**Decision.** Add a *schedule* aggregate (03 §9): a pure planner builds 24 h of slots from the chosen
models; a dedicated broadcast runner plays them one at a time with a per-move delay and a gap between
games; a chrome-less `/broadcast` page is the OBS Browser Source. Broadcast games are normal rated
games. The app does not stream video (dependency budget); OBS does.
**Consequences.** Cost: a 24 h day at 4 s/move is roughly 20–30 games of ~80 plies — ~2 000 Copilot
requests × the model multiplier; the planner shows the estimate before saving, and premium models
should be chosen knowingly. Reliability: backoff + skip policy so one failure never ends the stream.
Restart: a running schedule resumes; the interrupted game is excluded from ratings. Copilot rate
limits are the dominant operational risk and are handled by the failure policy, not by retries inside a move.

**Amended 2026-10-24: broadcast games are outside `Arena:MaxConcurrentGames` by decision; the cap is per source; operational rule: do not run a tournament during a broadcast.**

## ADR-012 — Cost in AI credits and US dollars; reasoning is stored

**Status:** Accepted (2026-10-25).

**Context.** Copilot bills chat by AI credits (0.01 USD each, token-metered per model) since June 2026, so the model
multiplier is null for the token-billed models and every model showed "multiplier unknown". The owner wants the tokens
and the dollar cost of each game, the models' reasoning and the messages exchanged with the engine.
**Decision.** The gateway captures, per reply, tokens (input, output, reasoning, cached), the reported AI-credit cost and
the readable reasoning, and lists each model's token prices (03 §10). Every attempt stores them. The API computes cost per
side as in 03 §10.2, with `Arena:Cost:UsdPerAiCredit` (default 0.01) and a cost basis that says how much of it was
reported. Prices replace the multiplier in the UI. Copilot's Auto model is hidden by default.
**Consequences.** Reasoning text is stored in `move_attempts` (it can be long; it is the owner's own data on the owner's
machine, P5 unaffected). The 1 AI unit = 1 AI credit assumption and the price unit are unverified until the owner's live
smoke run; both are isolated in one place each so a correction is one line. The multiplier-based estimates in the
tournament form and the broadcast planner are replaced by AI-credit estimates (G5).

## ADR-013 — The public results site is a static export on the `gh-pages` branch

**Status:** Accepted (2026-10-25).

**Context.** The owner wants a public page with every result and chart. Deploying the web app read-only (Fly.io, a hosted
Postgres, a read-only mode, a SELECT-only role, caching of the on-read rating) was analysed and costs about 40 USD a month
plus a new attack surface; the live game stream would not reach it anyway, because it lives in the local process.
**Decision.** The local app exports a static snapshot (03 §11) and force-pushes it as one orphan commit to `gh-pages`;
GitHub Pages serves the branch. ADR-002 holds: nothing runs anywhere but the owner's machine. The live view stays on
YouTube; the site lags by at most the auto-publish interval.
**Consequences.** No server, no database online, no cost. Everything the site shows is public by design. Pages' source
moves from GitHub Actions to the `gh-pages` branch (one owner setting). The site is plain HTML and SVG, so it has no search
or filtering; the JSON files are there for anyone who wants to analyse the data.

## ADR-014 — Benchmark runs, player variants and custom models

**Status:** Accepted (2026-10-25).

**Context.** The owner wants anyone who clones the repository to benchmark one model configuration, including their own
newly trained model, against the other models, with a shareable report.
**Decision.** (1) A player id may carry a reasoning-effort suffix (`model@effort`), and each variant is ranked as its own
player. (2) Custom models are served through the Copilot SDK's bring-your-own-key provider configuration
(`SessionConfig.Provider`, OpenAI-compatible or Azure), so ADR-004 holds. (3) A benchmark is a gauntlet stored as a
tournament of kind `benchmark`; its games are ordinary rated games. (4) Reports carry a fingerprint (app version, formula
version, prompt version) so two reports can be compared honestly.
**Consequences.** Cost scales with opponents × games: 30 games against 12 opponents is 360 games (about 14 000 candidate
moves and as many opponent moves); the estimate is shown before start and the docs recommend a reference panel. A custom
model's API key is a secret parameter. Early stopping (SPRT, as chess-engine testing uses) is a possible follow-up, not part
of this decision.

## ADR-015 — The prompt style is part of the player

**Status:** Accepted (2026-10-25).

**Context.** Games often end with every piece traded and kings and pawns left, with no sign of a plan. The rated prompt
forbids any thinking, names no goal, shows the position as a FEN only, and carries nothing between moves. The owner asked
for better prompts as a run configuration, each ranked on its own.
**Decision.** (1) A player id may carry a prompt style, `model[@effort][~style]`. The style is part of the player's
identity, like the effort (ADR-014), so each configuration is its own player in the one global ranking. (2) There are
three styles: `classic` (today's text, byte-identical, the default and the plain id), `board` (goal, ASCII board, piece
lists, material, full PGN, sorted legal moves) and `think` (as `board`, plus an analysis, a `Plan:` line carried to the
next move, and a `Move:` line). (3) `board` and `think` open one conversation per move, because the prompt carries the
whole game. (4) No style gets engine help. (5) RATING.md §1 reads "the rated prompt template of the player's style".
**Consequences.** Existing games and ratings keep their meaning, because they are `classic`. A `think` move costs more
output tokens; the estimate uses the player's own history once it exists. One conversation per move means one session
start per move; if that is slow on Copilot, it shows in the latency columns and the smoke run. Whether a style helps is
measured, not assumed: the "Prompt styles compared" section and a benchmark of the same model under two styles.

## ADR-016 — Viewer-paid games: YouTube Super Chat, API keys, tiered prices

**Status:** Accepted (2026-10-26).

**Context.** The owner streams 24 hours a day and wants viewers to pay for the games they choose, so that the stream
does not spend the owner's money. Games played for others on the owner's personal Copilot subscription may breach its
terms. The app runs on localhost only.

**Decision.**

1. Payment is a YouTube Super Chat. Google is the seller, so there is no payment provider, no card data and no VAT
   handling in the app (merchant-of-record rule of the payments standard).
2. Paid games run only on the owner's API keys at Anthropic (provider type `anthropic`), OpenAI and DeepInfra (both
   provider type `openai`), through the SDK's bring-your-own-key configuration. ADR-004 still holds: the Copilot SDK
   stays the only gateway.
3. Prices come in tiers derived from the cost estimator, so a cheap matchup costs the minimum. Each game is capped at
   the net amount received.
4. Payments are read by outbound polling of the YouTube Live Chat API, not by webhooks, so nothing is exposed to the
   internet. The payments standard's webhook rules still apply to the polled events: each is recorded once by its
   provider id, amounts are validated on the server, and the provider vocabulary stops at the boundary.
5. A payment is never lost: it is played or kept as credit.
6. Request games are ordinary rated games, played one at a time by the broadcast runner.

**Consequences.** YouTube keeps about 30 % of a Super Chat, which the tiers include. Exchange rates are configuration,
not live data. Super Chat cannot refund, so failures become credit. The YouTube Data API quota bounds the poll rate. A
request game that hits its budget is adjudicated. Before going live, the owner checks the YouTube Super Chat policy and
the tax treatment of the income.

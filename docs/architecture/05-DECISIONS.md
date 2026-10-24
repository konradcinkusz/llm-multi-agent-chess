# 05 — Decisions

Each record: context, decision, consequences, and the principle it applies or deviates from.
Status is **Proposed** until the owner accepts the plan; the supervisor flips them to
**Accepted** when the plan PR merges.

---

## ADR-001 — Remove the Python implementation

**Context.** The repo carries two implementations of one product (Streamlit/Python and
.NET). The README documents only the Python one; recent work happened only in .NET.
**Decision.** Delete `src/*.py`, `tests/*.py`, `requirements.txt` and the root `.env.example`
in wave 0. The .NET engine is characterised by its own tests (T1.5) instead.
**Consequences.** One stack; `src/` becomes free for the .NET solution. The history keeps the
Python code if it is ever wanted. — P14 (a stale README is a finding).

## ADR-002 — Local only: Aspire is the runtime, nothing is deployed

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

**Context.** P3 asks for a service per bounded context.
**Decision.** Games, players, tournaments and ratings are one bounded context (`Chess.Api`):
ratings are a pure function of finished games, tournaments are schedules of games, and the
game runner holds the live game. Splitting them would ship the same aggregate over HTTP for no
gain — the same reasoning as CopilotScope's collector. Pure logic is still separated into
libraries (`Chess.Engine`, `Chess.Rating`, `Chess.Analysis`) and the vendor edge into `Chess.Llm`.
**Consequences.** One database, one owner — P3 holds.

## ADR-004 — GitHub Copilot SDK is the only LLM gateway

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

**Context.** P5's JWT/JWKS rule assumes a multi-user, networked estate. This is a single-user
tool on the owner's machine, and adopting `authservice` would add a second service and a login
for one person.
**Decision.** No identity provider. The API listens on `127.0.0.1:5080` only and the web app on
`localhost:5070`; CORS is not configured at all (the browser never calls the API). The admin
attempt log is therefore reachable only from the machine itself.
**Deviation, recorded:** P5 signing-key item N/A while R1 holds. Revisit together with ADR-002.

## ADR-007 — Ranking: server-side games, forfeits, Bradley–Terry recomputed on read

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

**Context.** Move quality (ACPL) needs an engine; a package dependency would break the
dependency budget.
**Decision.** `Analysis:StockfishPath` (or `stockfish` on `PATH`) enables `StockfishAnalyzer`
over UCI; otherwise `NoOpMoveQualityAnalyzer` and `Q = L` (RATING.md §4). Reported in
`/health` (P8).

## ADR-009 — Retarget to net10.0 with central package management

**Context.** net8.0 leaves support on 2026-11-10; Aspire's current releases target net10.
**Decision.** Every project `net10.0`, versions in `Directory.Packages.props`, SDK pinned in
`global.json`. No Dockerfiles exist after T0.2, so the TFM/base-image coupling (P6) has nothing
to drift against.

## ADR-010 — Contracts are frozen in wave 0 so later waves can run in parallel

**Context.** The work is executed by several parallel agent sessions that each open one PR.
Parallel sessions that each invent a shared interface produce merge conflicts and three
incompatible shapes.
**Decision.** Wave 0 creates every project, every interface and record in
03-TARGET-ARCHITECTURE §3, and every `Add…()` registration call in `Program.cs` as a stub in
its own file. Every later task owns a disjoint set of files (06-TASKS, "Owns"). A task that
needs to change a frozen contract stops and reports to the supervisor instead.

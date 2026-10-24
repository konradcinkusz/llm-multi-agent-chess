# 02 — Gap analysis

Each row is one item of the reference architecture's compliance checklist (§3), plus the
product requirements stated by the owner on 2026-10-24:

> R1. Runs **fully locally**, no deployment, no provisioned cloud agents.
> R2. **Every** model, session and request goes through the **GitHub Copilot SDK**, on the
>     owner's Copilot subscription.
> R3. The product is an **LLM ranking**: models play chess against each other and are rated
>     with a coefficient describing one LLM's quality relative to another.

Severity: **B** blocks the product goals, **H** high, **M** medium, **L** low,
**N/A** — not applicable, with the reason recorded in [05-DECISIONS](05-DECISIONS.md).

| # | Checklist item / requirement | Today | Gap | Sev | Task(s) |
|---|---|---|---|---|---|
| G1 | R2 — LLM traffic via Copilot SDK | OpenAI `ChatClient` with an API key | Whole LLM layer replaced; model list must come from Copilot, not config | **B** | T1.4, T2.1, T2.6 |
| G2 | R3 — ranking with a relative coefficient | No ratings at all; status stored as display text; winner not stored | Rating library, outcome as data, tournaments, leaderboard and head-to-head views | **B** | T1.3, T1.6, T2.4, T2.5, T3.3, T3.4 |
| G3 | R3 — honest outcomes | Illegal answers are replaced by the first legal move | Forfeit rule; record every attempt | **B** | T2.1, T2.2 |
| G4 | R3 — games survive the browser | Game loop lives in a SignalR hub, tied to one connection, uses a disposed hub | Server-side game runner (hosted service); browser only observes | **B** | T2.2, T2.3 |
| G5 | AppHost declares every resource (P1) | docker-compose only | Aspire AppHost: Postgres, API, web; one command | H | T1.2 |
| G6 | `AddServiceDefaults()` / `MapDefaultEndpoints()` (P2, P2a) | None | ServiceDefaults kernel with size guard | H | T1.1 |
| G7 | `/health` + `/alive`, optional integrations reported (P8) | Constant `ok` | Health reports `database`, `copilot`, `stockfish`; startup banner | H | T1.1, T1.4, T1.7 |
| G8 | OTLP traces/metrics/logs (P15) | None | OTel via ServiceDefaults; `gen_ai` spans around Copilot calls | M | T1.1, T1.4 |
| G9 | Owns its DB (P3) | Yes, single service | — (keep; one service is correct, see ADR-003) | — | — |
| G10 | `MigrateAsync` in a hosted service (P4) | Synchronous before `app.Run()` | `MigrationHostedService`; provider switch Postgres / InMemory | M | T1.3 |
| G11 | Config from env, no secret in source, scanner in CI (P5) | Password in `appsettings.json`; no scanner | Remove literal; Aspire parameter; gitleaks pre-commit + CI | H | T0.1, T1.2, T1.8 |
| G12 | One signing key / JWKS (P5) | No auth | **N/A** for a single-user localhost tool — ADR-006 | N/A | — |
| G13 | Kernel holds no domain, size check (P2) | No kernel | Arch test + CI line ceiling | M | T1.1, T1.8 |
| G14 | Optional integrations degrade (P8) | Missing OpenAI key → runtime exceptions per move | Copilot absent → random/fake players only, visible in health; Stockfish absent → no move-quality component | H | T1.4, T1.7 |
| G15 | Dockerfile per service, runtime major = TFM (P6) | Dockerfiles exist; TFM net8 | **N/A** — nothing is deployed; Aspire runs projects directly (ADR-002). Dockerfiles, nginx and docker-compose removed | N/A | T0.2 |
| G16 | `fly.toml` (P7), tag-driven deploy (P12) | None | **N/A** — R1 (ADR-002). CI on pull requests still required | N/A | T1.8 |
| G17 | Resilience handler on outbound `HttpClient`s | n/a | Only the Copilot SDK talks outward and it is not an `HttpClient`; timeouts set explicitly per request | L | T1.4 |
| G18 | `Program.cs` is a manifest (P9) | Inline wiring | One `Add…()` per capability in `*ServiceCollectionExtensions.cs` | M | T0.2 |
| G19 | Extension points are interfaces in DI (P10) | `IChessAgent` exists but factory news up clients | `IChessPlayerFactory`, `ILlmGateway`, `IMoveQualityAnalyzer` | M | T0.2 |
| G20 | Anti-corruption at the edge (P11) | OpenAI types inside the agent | Copilot SDK types never leave `Chess.Llm` | M | T1.4 |
| G21 | Test project, logic layer covered (P13) | 0 .NET tests | Engine characterisation (perft) **before** moving it; rating, prompt, parser, runner tests; one E2E | H | T1.5, T1.6, T2.*, T4.1 |
| G22 | Decisions recorded in `docs/` (P14) | None; README describes the Python app | This set + README rewrite | M | this PR, T4.3 |
| G23 | Browser talks only to its own origin (FRONTEND-BFF §1) | Blazor WASM calls the API and a SignalR hub through nginx | **Blazor kept by owner decision (ADR-005)**, converted to a Blazor Web App with interactive server rendering: the browser talks only to `Chess.Web`, which calls `Chess.Api` server-side. The Next.js requirement is a recorded deviation | H | T1.9, T3.* |
| G24 | Repo baseline (REPO-BASELINE §1, §7) | Nothing | `.editorconfig`, central packages, CODEOWNERS, Dependabot, PR template, `.claude/settings.json` | M | T0.1, T1.8 |
| G25 | Two implementations of one product | Python + .NET | Python removed (ADR-001) | M | T0.1 |
| G26 | Unauthenticated admin endpoints expose every prompt | Open on port 80 via nginx | Bind to `127.0.0.1` only (ADR-006) | M | T1.2 |
| G27 | CORS allow-any-origin with credentials | Yes | Removed — the browser talks only to the web origin | M | T0.2 |
| G28 | AI-evals for the prompt/parse loop (AI-EVALS) | None | Offline scenario suite with the fake gateway; opt-in live smoke | M | T4.4 |

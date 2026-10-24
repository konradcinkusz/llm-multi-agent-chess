# AI evals

The evals check the one LLM-backed behaviour of the arena: turning a model's reply into a legal move, and what
happens when it cannot. They follow the AI-EVALS guide (scenarios are data, assertions are on the trace of a move,
and the suite must be shown able to fail). There are two parts:

| Part | Where | Runs | Needs |
|---|---|---|---|
| Offline scenario suite | `tests/Chess.Api.Tests/Evals/` | every `dotnet test`, in CI | nothing: the scripted `FakeLlmGateway` |
| Live smoke | `scripts/smoke-copilot.sh` | by the owner, by hand | a Copilot login, PostgreSQL, and premium requests |

## Behaviour spec

The spec the scenarios are graded against. A change to a behaviour or a constraint is a change to this section and
to the scenarios, reviewed together.

### Expected behaviours

- **B1: a reply is a move only when it names exactly one legal move.** `MoveNotation.TryResolve` decides. Accepted:
  SAN with the check and mate marks optional; castling as `0-0`, `0-0-0`, `O-O`, `O-O-O`; UCI such as `e2e4` or `e7e8q`;
  a leading move number (`1.`, `1...`); surrounding quotes and whitespace; trailing punctuation.
  Rejected: prose around the move, wrong case (`nf3`), UCI with the wrong promotion piece, illegal moves, several
  moves in one reply, an ambiguous SAN where a file is needed, and an empty reply.
- **B2: a rejected reply gets one corrective follow-up** in the same conversation. It says the reply was not a legal
  move and lists the legal moves.
- **B3: the side has `Arena:MaxAttemptsPerMove` attempts** (default 3) for one move. A legal reply within them is played.
- **B4: when every attempt is rejected, the side forfeits.** The game ends with `forfeit-white` or `forfeit-black`.

### Hard constraints (must hold at 100%)

- **C1: no move is played unless a reply resolved to it.** There is no fallback move.
- **C2: a forfeit comes only after the last allowed attempt.** No attempt is made beyond the limit.
- **C3: the attempts are the replies the model gave, in order.** None is lost, none is invented.
- **C4: a legal reply is the last attempt.** Replies after it are never requested.

### Success criteria

None in the offline suite. There is no judge of move quality and no grading of strength or style: the suite checks the
rules above, not how well a model plays. (AI-EVALS §5 Layer 2 is not implemented.)

### Out of scope

- **Chess strength and move quality.** The offline suite cannot say whether a model plays well. Move quality is
  measured by the rating pipeline (RATING.md §4), not by these evals.
- **Tool use.** The Copilot session has no tools (03 §3.1), so there is no tool-call trace to grade.
- **Timeouts and gateway failures.** The scripted fake cannot raise them, so they are not among the 30 positions. They
  are covered by `LlmChessPlayerTests` (a timeout or exception counts as an attempt).
- **Ambiguity and clarifying questions.** A chess reply has no clarifying-question path: a question is simply a
  rejected reply.
- **Prompt injection.** The only input a side sees is the position and its legal moves; there is no tool result or
  user text to inject through.
- **A model matrix.** The offline suite uses one scripted model. The live smoke plays one game against one model.

## Scenario set

The 30 positions are data in `tests/Chess.Api.Tests/Evals/EvalScenarios.cs`. Each one is a FEN and the replies a
model gives in order. Only the last reply is legal when the side plays; every reply is rejected when it forfeits.

Classes (AI-EVALS §3): **accepted** (happy path: the first reply is the move, 15 scenarios), **recovers**
(adversarial replies, then a legal one, 10 scenarios), **forfeits** (adversarial replies on every attempt, 5 scenarios).
The adversarial kinds covered: prose around the move, wrong case, UCI, illegal moves, several moves in one reply,
ambiguous SAN, empty replies, and an illegal castling. A forfeit uses exactly `MaxAttemptsPerMove` replies.

| # | Id | Class | Replies, in order | Expected |
|---|---|---|---|---|
| 1 | `accepted-san-e4` | accepted | `e4` | e4 |
| 2 | `accepted-san-nf3` | accepted | `Nf3` | Nf3 |
| 3 | `accepted-uci-e2e4` | accepted | `e2e4` | e4 |
| 4 | `accepted-uci-g1f3` | accepted | `g1f3` | Nf3 |
| 5 | `accepted-move-number-dot` | accepted | `1. d4` | d4 |
| 6 | `accepted-move-number-ellipsis` | accepted | `1... e5` | e5 |
| 7 | `accepted-quoted` | accepted | `"c4"` | c4 |
| 8 | `accepted-trailing-punctuation` | accepted | `Nc3!` | Nc3 |
| 9 | `accepted-check-mark-optional` | accepted | `Qh5` | Qh5+ |
| 10 | `accepted-castling-letters` | accepted | `O-O` | O-O |
| 11 | `accepted-castling-zeros` | accepted | `0-0` | O-O |
| 12 | `accepted-castling-queenside-zeros` | accepted | `0-0-0` | O-O-O |
| 13 | `accepted-promotion-uci` | accepted | `a7a8q` | a8=Q+ |
| 14 | `accepted-mate-suffix` | accepted | `Qxf7#` | Qxf7# |
| 15 | `accepted-disambiguated-san` | accepted | `Nbd2` | Nbd2 |
| 16 | `recovers-prose-then-san` | recovers | `I would play e4.` → `e4` | e4 |
| 17 | `recovers-wrong-case-then-san` | recovers | `nf3` → `Nf3` | Nf3 |
| 18 | `recovers-illegal-then-legal` | recovers | `Qxf7` → `d4` | d4 |
| 19 | `recovers-empty-then-legal` | recovers | `(empty)` → `e4` | e4 |
| 20 | `recovers-uci-without-promotion` | recovers | `a7a8` → `a7a8q` | a8=Q+ |
| 21 | `recovers-two-moves-in-one-reply` | recovers | `e4 e5` → `e4` | e4 |
| 22 | `recovers-ambiguous-san` | recovers | `Nd2` → `Nbd2` | Nbd2 |
| 23 | `recovers-illegal-uci-then-legal` | recovers | `e2e5` → `e2e4` | e4 |
| 24 | `recovers-on-last-attempt` | recovers | `Ke2` → `Ke2` → `Nf3` | Nf3 |
| 25 | `recovers-illegal-castling` | recovers | `O-O-O` → `O-O` | O-O |
| 26 | `forfeits-prose` | forfeits | `I play e4` → `My move is d4.` → `Let's go with Nf3` | forfeit |
| 27 | `forfeits-wrong-case` | forfeits | `NF3` → `e2E4` → `nf3` | forfeit |
| 28 | `forfeits-illegal` | forfeits | `Qxf7` → `Ke2` → `e5` | forfeit |
| 29 | `forfeits-empty` | forfeits | `(empty)` → `(blank)` → `(newline)` | forfeit |
| 30 | `forfeits-mixed` | forfeits | `e2e5` → `I think E4` → `E4` | forfeit |

The "Expected" column is the SAN the engine reports for the move (so `Qh5` is recorded as `Qh5+`, since it gives check).

### How the scenarios are checked

- `OfflineEvalTests` runs each scenario through `LlmChessPlayer` with a `FakeLlmGateway` scripted with exactly the
  replies. It asserts the attempts are exactly those replies (C3), only the last is legal (B1), and the move is the
  expected SAN or a forfeit (B3, B4). A reply the fake did not script would be a random legal move and would fail
  the attempt count, so there is no path where the suite passes by accident.
- `EvalSuiteTests` checks the data itself: 30 positions, unique ids, every class present, each position has legal
  moves, every label agrees with `MoveNotation` (a rejected label must not resolve, the expected SAN must be the one
  it resolves to), and a forfeit uses exactly `MaxAttemptsPerMove` replies. A mislabelled scenario fails here.

## Running the offline suite

```bash
dotnet test tests/Chess.Api.Tests --filter "FullyQualifiedName~Chess.Api.Tests.Evals"
```

It also runs in `dotnet test Chess.sln`. It needs no network and no Copilot login.

## Proving the suite can fail

An eval that has only ever passed proves nothing (AI-EVALS §9). Two deliberately broken variants were run against the
suite, one at a time, on a local copy with the change reverted afterwards. Neither change is committed.

### Variant 1: the parser accepts wrong case (the required parser break)

Change in `src/Chess.Engine/MoveNotation.cs`: SAN is compared case-insensitively. It breaks the stated constraint that
SAN is exact (B1).

Result: **4 of 37 tests fail**, all on assertions.

- `OfflineEvalTests`: `recovers-wrong-case-then-san`, `forfeits-wrong-case`, `forfeits-mixed`. The reply `nf3` is
  played (`reply 1 'nf3' resolves=True`), so the side plays instead of forfeiting.
- `EvalSuiteTests.EveryLabelAgreesWithTheMoveNotationContract`: the data check reports the same mismatch.

The `accepted-*` scenarios still pass, which is correct: none of them is in the wrong case.

### Variant 2: a fallback move on forfeit (the no-fallback constraint)

Change in `src/Chess.Api/Players/LlmChessPlayer.cs`: when every attempt is rejected, the side plays its first legal move
instead of forfeiting. It breaks B4 and C1.

Result: **5 of 37 tests fail**, all on assertions (`Assert.Equal() Failure: Values differ`, `Expected: True`, `Actual:
False`, on the forfeit flag).

- `forfeits-prose`, `forfeits-wrong-case`, `forfeits-illegal`, `forfeits-empty`, `forfeits-mixed`: each side plays a
  move where it should forfeit.

No variant failed with an exception. Every failure above is an assertion on the trace, so the suite is catching
the variant, not the harness crashing.

## Live smoke: `scripts/smoke-copilot.sh`

Run by the owner, on a machine with the Copilot CLI logged in. It starts `Chess.Api` with the real gateway and plays
one rated game: `random` as White against the model as Black, capped at 20 plies (`Arena:MaxPlies`). It then prints
the result, the model's attempts and its latency.

```bash
ARENA_CONNECTION_STRING='Host=localhost;Database=chess;Username=...;Password=...' scripts/smoke-copilot.sh <model-id>
```

| Variable | Meaning |
|---|---|
| `ARENA_CONNECTION_STRING` | PostgreSQL connection string (`ConnectionStrings:chess`). Required. |
| `ARENA_API_URL` | Where the API listens. Default `http://127.0.0.1:5080`, as in `appsettings.json`. |
| `SMOKE_TIMEOUT_SECONDS` | How long the game may take. Default 1800. |

Example output (the values are illustrative):

```
Result:    0-1 (adjudicated) after 20 plies
Model:     <model-id> (Black), 10 moves played
Attempts:  12 Copilot requests, 2 rejected (illegal, unparseable or empty)
Latency:   820 ms average per request
```

**It refuses to run in these cases** (exit 2, before anything starts):

- `CI` is set: the script spends premium requests and is never part of CI.
- The effective `Llm:Provider` is not Copilot: an environment override (`Llm__Provider`, any letter case) wins over
  `appsettings.json`. A value of `Fake` is refused, and the API is started with `Llm__Provider=Copilot` regardless.
- `ARENA_CONNECTION_STRING` is not set, or no model id is given.

The connection string is passed only to the API process and never printed. The API is stopped when the script exits.

**Status.** The script passes `shellcheck` and every refusal path above was exercised. It has not been run against a
live Copilot login. It needs the game endpoints of 03 §5 (`POST /api/games`, `GET /api/games/{id}`, owned by T2.3),
which are not on `main` yet, so it cannot complete until T2.3 is merged.

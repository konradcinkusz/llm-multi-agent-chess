# How models are rated

This is the published formula. `Chess.Rating` implements exactly this and nothing else; a
change here is a change there, in the same pull request, with the test vectors in §7 updated.

The design follows three rules taken from `architecture-standards` (METRIC-ETHICS):

1. **Results first.** The number that cannot be argued with is who won. It carries 75 % of
   the relative coefficient; nothing a prompt can game outweighs it.
2. **Every number carries its confidence.** Game count and a 95 % interval travel with every
   rating and every coefficient; small samples are labelled *provisional*.
3. **Pure and reproducible.** The calculation is a function of the finished games table and
   options. No clock, no I/O, no randomness except a seeded bootstrap. Same games → same
   numbers, to the last digit.

## 1. What counts as a game

A game is **rated** when it was created with `rated = true`, each side used the rated prompt
template of its prompt style, and it finished with a result other than `*` (aborted / interrupted games are
excluded). Each side is identified by its **player id** (`random` for the random player):
the model id plus its reasoning effort and prompt style when it names them, `model[@effort][~style]`
(ADR-014, ADR-015). Each player id is ranked as its own player in the one fit. A plain model id means
the model's default effort and the `classic` prompt style.

## 2. Outcome of a game

`s` = score of White: 1 win, ½ draw, 0 loss.

| Termination | s |
|---|---|
| checkmate | 1 or 0 |
| stalemate, insufficient material, fifty-move, threefold repetition | ½ |
| **forfeit** — a side gave `MaxAttemptsPerMove` (default 3) illegal or unparseable replies, or timed out, for one move | loss for that side |
| **adjudicated** at `MaxPlies` (default 200) | Stockfish available: eval ≥ +300 cp for one side → that side wins, else ½. Without Stockfish: material difference ≥ 5 points → that side wins, else ½ |

A model that cannot produce legal moves therefore **loses** — it never gets a free move.

## 3. Strength — `R` (Bradley–Terry, Elo scale, with first-move advantage)

For a game between White *w* and Black *b*:

```
E_g = 1 / (1 + 10^(−(R_w + η − R_b) / 400))
```

`η` is White's advantage, fitted from the data. All `R_i` and `η` are fitted together by
maximising the penalised log-likelihood over all rated games:

```
ℓ = Σ_g [ s_g · ln E_g + (1 − s_g) · ln(1 − E_g) ]  −  Σ_i R_i² / (2 σ²)
```

- Draws enter as `s = ½` (the standard "ties split" Bradley–Terry quasi-likelihood).
- `σ = PriorSigma = 350` is a weak Gaussian prior. Without it a model with a perfect record
  has an infinite rating; with it, ten straight wins move a rating a long way but not to
  infinity. `PriorSigma = null` disables it (used only in test vectors).
- Solved by Newton–Raphson on the concave `ℓ` (or gradient ascent with a fixed step); stop when
  max |ΔR| < 1e-6 or after 10 000 iterations. Order of games does not matter — unlike
  incremental Elo, which would make the ranking depend on the order the tournament happened to
  run in.
- **Anchor:** after fitting, every `R_i` is shifted so `R_random = AnchorRating = 0`. If
  `random` has no rated games, the shift makes the mean of all `R_i` equal 0 instead. A rating of
  400 then reads "expected score 0.91 against a player that moves at random".

## 4. Move quality — `L` and `MQ`

Computed only over moves an LLM side actually played in rated games.

- **Legality** `L_i = first-attempt-legal moves / moves` ∈ [0, 1]. `random` has `L = 1`.
- **Move quality** (only when Stockfish is available, `stockfish` in `/health`):
  centipawn loss per move `cpl = max(0, eval(best) − eval(played))` from the mover's point of
  view, Stockfish at fixed depth 12, each move capped at 1000 cp, mate scores mapped to ±1000.
  `ACPL_i` = mean `cpl`. Then
  ```
  MQ_i = 1 / (1 + ACPL_i / 100)            ∈ (0, 1]
  ```
  (ACPL 0 → 1.0; 100 → 0.5; 300 → 0.25.)
- **Quality** combines both:
  ```
  Q_i = 0.6 · MQ_i + 0.4 · L_i     when MQ is known
  Q_i = L_i                        when it is not
  ```
  The weight `0.6` is `RatingOptions.MoveQualityWeight`. Which variant was used is part of the
  response, so a reader never compares an engine-backed `Q` with a legality-only one unknowingly.

## 5. The relative coefficient — `RQC(A, B)`

The number the product exists for: **how good is model A relative to model B**.

```
E(A,B)   = 1 / (1 + 10^((R_B − R_A) / 400))        expected score of A vs B, colour-neutral
QS(A,B)  = Q_A / (Q_A + Q_B)                        quality share
RQC(A,B) = 0.75 · E(A,B) + 0.25 · QS(A,B)          0.75 = RatingOptions.ResultWeight
```

Properties (each one is a unit test):

- `0 < RQC < 1`; **0.5 means equal**; above 0.5 A is better.
- **Antisymmetric:** `RQC(A,B) + RQC(B,A) = 1`.
- Uses *all* games, not only A-vs-B games: if A beats C and C beats B, the coefficient already
  knows something about A vs B. The direct record is shown next to it, never instead of it:
  `HeadToHeadGames`, `HeadToHeadScore` (A's mean score in direct games).
- **Elo-equivalent:** `Δ* = 400 · log10(RQC / (1 − RQC))` — the rating gap that would give the
  same expected score, for readers who think in Elo.
- **Interval:** the 2.5th and 97.5th percentiles of RQC over the bootstrap (§6).

Reading guide shown in the UI: 0.50 – 0.55 *indistinguishable*, 0.55 – 0.65 *slightly better*,
0.65 – 0.80 *clearly better*, > 0.80 *dominant* — and *not significant* whenever the interval
contains 0.5, whatever the point value.

### When the UI may say "significantly"

A pairwise result is called *significant* only when **all** of these hold, and the UI applies
this one rule everywhere (`Components/Pages/Shared/Significance.cs`):

- **(a)** the 95 % interval excludes 0.5 by a real margin: `RqcLow > 0.5 + 1e-6` or
  `RqcHigh < 0.5 − 1e-6`;
- **(b)** the interval is not degenerate: `RqcHigh − RqcLow > 1e-6`. With one game every bootstrap
  resample is identical, so the interval collapses to a point and would otherwise pass (a) on
  floating-point noise;
- **(c)** neither model is provisional (§6): each has at least 20 rated games.

When a result is not significant only because of (c), the UI says so: "Not yet significant: too
few games (need 20 rated games per model)."

## 6. Leaderboard and confidence

- **Arena score** `AS_i = 100 · mean_{j ≠ i} RQC(i, j)` over every other rated model. 50 is the
  average of the field. The leaderboard sorts by `AS`, then `R`.
- **Arena interval:** `ArenaScoreLow` / `ArenaScoreHigh` are the 2.5 / 97.5 percentiles of `AS` over the same bootstrap
  resamples. On each resample `AS` is computed from that resample's ratings, over the models that resample rated; a model
  absent from a resample has no value there. Without resamples the interval collapses to `AS`.
- **Conservative order** (`GET /api/ratings?order=conservative`; the default `order=score` is the order above): non-provisional
  models first, then by `ArenaScoreLow` descending, then `AS`, then `R`, then model name. It ranks by the lower bound of the
  arena score, so a model with more games (a narrower interval) beats one with the same point score and fewer games. The
  order changes only the sequence of the models, never a number.
- **Bootstrap:** `BootstrapSamples = 200` resamples of the rated games with replacement, using
  `new Random(Seed)` with `Seed = 20261024`; refit §3–§5 on each sample; the 2.5 / 97.5
  percentiles give `CiLow` / `CiHigh` for `R` and `RqcLow` / `RqcHigh` for RQC. Move statistics
  are not resampled.
- **Provisional:** fewer than `ProvisionalBelowGames = 20` rated games, or a rating interval
  wider than 200. Shown with a badge, sorted after non-provisional models.

## 7. Test vectors (must hold in `Chess.Rating.Tests`)

| # | Input | Expected |
|---|---|---|
| V1 | A vs B, 8 games: A as White wins 3 loses 1, A as Black wins 3 loses 1; `PriorSigma = null`, no anchor model | `R_A − R_B = 400·log10(3) ≈ 190.85` (± 0.5); `η ≈ 0` (± 1) |
| V2 | V1 with `Q_A = Q_B` | `RQC(A,B) = 0.75·0.75 + 0.25·0.5 = 0.6875` (± 0.002) |
| V3 | any input | `RQC(A,B) + RQC(B,A) = 1` (± 1e-9) |
| V4 | A wins 10 of 10 vs `random`, `PriorSigma = 350` | `R_random = 0`, `R_A` finite and > 400 |
| V5 | same games in reversed order | identical report (exact equality) |
| V6 | same games, same seed, computed twice | identical intervals |
| V7 | 10 rated games for A | A is provisional |
| V8 | `ACPL = 100`, `L = 0.5` | `MQ = 0.5`, `Q = 0.6·0.5 + 0.4·0.5 = 0.5` |
| V9 | no Stockfish data, `L = 0.8` | `Q = 0.8`, `MoveQuality = null` |
| V10 | any input with bootstrap samples | `ArenaScoreLow ≤ ArenaScore ≤ ArenaScoreHigh`, and the interval has a positive width |
| V11 | two models of equal strength, one with 30 rated games and one with 300 | the 30-game model has the wider arena interval |
| V12 | two models with equal `AS`; A has the higher lower bound (more games), B the higher rating | conservative order: A first; score order: B first (rating breaks the tie) |
| V13 | same games, same seed, computed twice | identical `ArenaScoreLow` / `ArenaScoreHigh` (exact equality) |

## 8. What is deliberately not in the score

Latency, tokens, and cost are recorded per move and shown beside the ranking on the Results page
(the "Strength vs cost" chart). Cost is in AI credits, with a dollar figure at `Arena:Cost:UsdPerAiCredit`. They are **never** folded into `R`, `Q` or `RQC`: a cheap,
fast model that plays badly must not outrank an expensive one that plays well, and the reader
should make the cost trade-off themselves.

## 9. Examples: player ids and prompt styles

These are examples of the player ids of §1, not new rules. Each row is one player in the one fit (§3), ranked on its own.

| Player id | The player |
|---|---|
| `claude-sonnet-5.5` | the model at its default effort, under `classic` (the plain id) |
| `claude-sonnet-5.5@medium` | the medium-effort variant, under `classic` |
| `claude-sonnet-5.5~think` | the default effort, under `think` |
| `claude-sonnet-5.5@medium~think` | the medium-effort variant, under `think`: a player of its own |
| `claude-sonnet-5.5~classic` | refused (400): write `claude-sonnet-5.5` |
| `random` | the random player, which takes no style |

Two ids that differ only in their style are two players in the one fit. Their games link them through §3, and their
relative coefficient is read like any other pair (§5). The Results page's "Prompt styles compared" section reports that
comparison with the one significance rule of §5.

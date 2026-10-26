# Benchmark a model

A benchmark plays one **candidate** model, in one configuration, against every other model: a fixed number of rated games
against each opponent, with colours alternating. You get a report you can share: a Markdown page and the same numbers as
JSON. The games are ordinary rated games, so they also move the global ranking on the Results page.

This works for three kinds of candidate:

- a **Copilot model**, e.g. `claude-sonnet-4.5`;
- a **variant** with a reasoning effort, `<model>@<effort>`, e.g. `claude-sonnet-4.5@medium`. Each effort is ranked as
  its own player;
- a **prompt style**, `~<style>`, e.g. `fake-a~think`: the same player under another prompt (`classic`, `board` or `think`).
  Each style is its own player. Section 8 covers styled candidates and opponents.
- **your own model** behind an OpenAI-compatible or Azure endpoint (vLLM, Ollama, LM Studio, a fine-tuned deployment).
  See [Your own model](#your-own-model).

The design is in [03 §12](architecture/03-TARGET-ARCHITECTURE.md) and [ADR-014](architecture/05-DECISIONS.md).

## 1. Quick start

You need the .NET 10 SDK, `curl` and `jq` (for the bash script), or PowerShell 7.4 or later (for the `.ps1` script). The
fake profile needs no Docker and no Copilot login, so try the whole flow with it first. Start the app in one terminal and
leave it running:

```bash
dotnet run --project src/Chess.AppHost --launch-profile fake
```

The API answers on `http://127.0.0.1:5080`. In a second terminal, benchmark `fake-a` at reasoning effort `high` against
every other enabled model (here `fake-b` and `random`, plus any custom model you declared, see section 4) with the default
30 games each, and write the report to `~/benchmarks`:

```bash
scripts/benchmark.sh --candidate fake-a@high --out-dir ~/benchmarks
```

The script creates the run, prints its progress, and writes two files:

- `~/benchmarks/benchmark-fake-a-high.md`: the report to share;
- `~/benchmarks/benchmark-fake-a-high.json`: the same report as data.

PowerShell takes the same options with PowerShell names, and writes the same files:

```powershell
pwsh ./scripts/benchmark.ps1 -Candidate fake-a@high -OutDir ~/benchmarks
```

To benchmark against the real Copilot models, start the app with the default profile (the command is in the
[README quick start](../README.md#quickstart)), then pass a model id from the catalogue. List the ids and the efforts each
one offers:

```bash
curl -s http://127.0.0.1:5080/api/models | jq -r '.[] | select(.enabled) | "\(.id)\t\(.reasoningEfforts // [] | join(","))\t\(if .custom then "custom" else "copilot" end)"'
```

Then pass the id as `--candidate`, with `@` and an effort from its list if you want one. The API refuses an effort the model
does not offer with a `400` that names the efforts it does offer.

Write the reports outside the repository, with `--out-dir`, so they do not land in your working tree.

## 2. Options

Both scripts take the same options. The PowerShell names are in brackets.

| Option | Default | Meaning |
|---|---|---|
| `--candidate ID` [`-Candidate`] | required | The model to benchmark: `<model>`, `<model>@<effort>` or `<model>[@<effort>]~<style>`, e.g. `claude-sonnet-4.5@medium~think`. |
| `--opponents LIST` [`-Opponents`] | every enabled model except the candidate's model (in any effort or style) and `Auto` | Comma-separated opponent ids, each with an optional effort and style. A reference panel (section 4) is the usual choice. |
| `--games N` [`-Games`] | `30` | Games against each opponent. Even, at least 2, so colours alternate. |
| `--concurrency N` [`-Concurrency`] | `2` | Games played at once. At most `Arena:MaxConcurrentGames` (default 2). It changes the time, not the cost. |
| `--name NAME` [`-Name`] | the candidate id | The run's name. The file names use it, with characters other than letters, digits, `.`, `_` and `-` replaced by `-`. |
| `--out-dir DIR` [`-OutDir`] | the current folder | Where `benchmark-<name>.md` and `benchmark-<name>.json` are written. |
| `ARENA_API_URL` | `http://127.0.0.1:5080` | The API. Set it for an arena on another port. |

Exit status: `0` the run finished and the reports were written; `1` the API refused the run, could not be reached, or the
run ended cancelled; `2` bad options, or the script refused to run under CI; `130` you pressed Ctrl-C.

**Ctrl-C cancels the run on the server**, so no game keeps running for a command you abandoned. In that case the script
writes no files. The cancelled run and the games it played stay on the server, and its report is still readable at
`GET /api/benchmarks/{id}/report` (section 6).

**The scripts refuse to run under CI.** A benchmark plays real games against a live arena, so they exit with status `2` when
`CI` or `GITHUB_ACTIONS` is set:

```bash
CI=true scripts/benchmark.sh --candidate fake-a@high
```

## 3. Cost and how to cut it

Every LLM move is a request. With the Copilot models a move costs AI credits, priced per token for each model
(see [the README's cost section](../README.md#cost-ai-credits-per-move)). A benchmark costs the same as the
games it plays:

**games = opponents × games per opponent.** The default of 30 games against 12 opponents is 360 games, which is roughly
14 000 candidate moves and as many from the opponents.

The script prints the estimate before the first game:

```text
Estimate: 0 premium request(s), about $0.0002; 0 model(s) without a price are not counted.
```

The estimate is only as good as the price data. A model with no price, including every custom model, is left out of the
total, and the line says how many were left out. Retries are not in the estimate, so a real bill can be higher, and a game
that reaches the 200-ply cap costs more than the estimate assumes.

To cut the cost, do one or both of these:

- **Use a reference panel.** Pick a few opponents that span the strength range, and keep their ids for every candidate you
  test. Each candidate then costs `panel × games`, and your results line up with each other. The example below uses two
  fake opponents and `random`, which costs nothing:

  ```bash
  scripts/benchmark.sh --candidate fake-a@high --opponents fake-b,random --out-dir ~/benchmarks
  ```

- **Play fewer games.** `--games 10` gives a third of the cost. The intervals get wider (section 5), so a 10-game result
  is a quick look, not a ranking.

Concurrency does not change the cost. It changes how long the run takes, and it is capped by `Arena:MaxConcurrentGames`.

## 4. Your own model

Your model must answer an OpenAI-compatible chat endpoint (or an Azure one). The app calls it through the Copilot SDK's
bring-your-own-key provider, so the Copilot SDK stays the only LLM gateway.

Declare the model in configuration. Each entry of `Arena:CustomModels` has these fields:

| Field | Meaning |
|---|---|
| `Id` | The name the arena shows and you pass as `--candidate` or `--opponents`. No `@`, no spaces, and not the id of a Copilot model. |
| `DisplayName` | Optional. Defaults to `Id`. |
| `ProviderType` | `openai` (vLLM, Ollama, LM Studio, any OpenAI-compatible server) or `azure`. |
| `BaseUrl` | The endpoint, for example `http://127.0.0.1:8000/v1` for a local vLLM server. |
| `Model` | The model name the endpoint expects in its requests. |
| `WireApi` | Optional. Defaults to `chat-completions`. |
| `ApiKey` | Not a plain field: the key is the secret parameter `custom-model-<Id>-key`. See below. |

**Put the entry in the AppHost's user secrets, and the key in a secret parameter.** The AppHost (`src/Chess.AppHost`) reads
`Arena:CustomModels` from its configuration and passes it to the API. The plain fields go to the API as environment variables.
The key does not: it is the secret parameter `custom-model-<Id>-key`, read from `Parameters:custom-model-<Id>-key` in the
AppHost's user secrets, and it reaches the API as `Arena__CustomModels__<n>__ApiKey`. User secrets live in your home folder,
not in the repository. The commands below use the fake profile, so they run with no Copilot login, and declare a model called
`my-llama`. Keep the key in a file outside the repository and read it into the secret, so it never appears on the command line:

```bash
mkdir -p "$HOME/.config/llm-arena"
printf '%s\n' "your-key-here" > "$HOME/.config/llm-arena/my-llama.key"
chmod 600 "$HOME/.config/llm-arena/my-llama.key"
dotnet user-secrets --project src/Chess.AppHost set "Parameters:custom-model-my-llama-key" "$(cat "$HOME/.config/llm-arena/my-llama.key")"
dotnet user-secrets --project src/Chess.AppHost set "Arena:CustomModels:0:Id" "my-llama"
dotnet user-secrets --project src/Chess.AppHost set "Arena:CustomModels:0:DisplayName" "My Llama (local)"
dotnet user-secrets --project src/Chess.AppHost set "Arena:CustomModels:0:ProviderType" "openai"
dotnet user-secrets --project src/Chess.AppHost set "Arena:CustomModels:0:BaseUrl" "http://127.0.0.1:8000/v1"
dotnet user-secrets --project src/Chess.AppHost set "Arena:CustomModels:0:Model" "llama-local"
dotnet run --project src/Chess.AppHost --launch-profile fake
```

Replace `your-key-here` with the real key, and put that key only in the file outside the repository. The entries are numbered
`0`, `1`, and so on, and an entry's key parameter is named after its `Id`. An entry with no key parameter gets no key, which
is right for an endpoint that takes none. The AppHost never prints or writes the key. It is passed to the API process as an
environment variable, the way the API reads its settings.

A key written as a plain value stops the AppHost at startup, with a message that names the parameter to use instead and never
quotes the value. That covers `Arena:CustomModels:<n>:ApiKey` in any configuration file, and an `Arena__CustomModels__<n>__ApiKey`
environment variable.

**Environment variables (an alternative).** The plain fields can instead be environment variables, set before the AppHost starts.
Use this only for an entry that is not also in the user secrets, since both would use the same entry number:

```bash
export Arena__CustomModels__0__Id=my-llama
export Arena__CustomModels__0__ProviderType=openai
export Arena__CustomModels__0__BaseUrl=http://127.0.0.1:8000/v1
export Arena__CustomModels__0__Model=llama-local
dotnet run --project src/Chess.AppHost --launch-profile fake
```

The key still goes in the secret parameter above, never in an environment variable. A bad entry stops the app at startup, with a
message that names it and never quotes the URL or the key.

Check that the arena lists it, then benchmark it (the second command runs in a second terminal):

```bash
curl -s http://127.0.0.1:5080/api/models | jq -r '.[] | select(.custom) | .id'
scripts/benchmark.sh --candidate my-llama --opponents fake-b,random --games 4 --out-dir ~/benchmarks
```

For a model you train yourself, the same applies with `--candidate my-llama` in place of a Copilot id. Use the same
reference panel as the other candidates, so the reports are comparable.

**Not verified yet.** A custom model is listed only while the Copilot runtime is `Ready`, so the app must be running with its
Copilot runtime, even for a local endpoint. Whether a provider session also needs a Copilot sign-in is **not verified**. The
owner's live run will settle it; until then, treat it as unconfirmed. If a sign-in turns out to be needed, the Copilot runtime
needs the same sign-in as for a Copilot model.

Custom models have no prices yet, so their cost is not in the estimate.

## 5. Reading the report

The report is in three parts: the summary, one row per opponent, and the fingerprint.

**Summary.** The candidate's score over all its games; its rating on the global scale, with a 95 % interval; its arena
score (50 is the average of the field); whether the rating is **provisional**; the
tokens it used; and the cost estimate. Provisional means too few games for a firm number: fewer than 20 rated games, or a
rating interval wider than 200 Elo (see [RATING.md](RATING.md)).

**One row per opponent.** For each opponent:

- **W / D / L** and **Forfeits**: the candidate's results. A forfeit is a side that could not produce a legal move within
  three attempts.
- **Score**: wins plus half the draws, as a share of the games.
- **Elo difference (95 % interval)**: how much stronger the candidate is than that opponent, in Elo points, computed from
  the score. A positive number means the candidate is stronger; a negative one, weaker. Zero is an even match.
- **RQC (95 % interval)**: the relative coefficient, which is 75 % results and 25 % move quality. 0.5 means equal (see
  [RATING.md §5](RATING.md#5-the-relative-coefficient--rqca-b)).
- **First-try legal**: the share of the candidate's moves that were legal on the first attempt. A low number means the model
  spends attempts, and cost, on illegal moves.
- **Candidate cost**: the estimate for this opponent's games.

**Reading the intervals.** An interval is the range the true value probably lies in, with 95 % confidence. It is wide when
the sample is small, and it is the number to quote, not the point. The rule of thumb for chess, with no draws:

- **30 games against one opponent give a 95 % interval of about ±125 Elo** when the score is near 50 %. The score of an
  opponent you beat three games in four is +191 Elo (`400 × log10(3)`), and an interval of roughly ±125 around that is the
  honest precision. Draws narrow the interval a little; the report's own interval is the number to use.
- **Four games give an interval of hundreds of Elo.** A 4-game report is a smoke test, not a result. The default of 30 is
  the least that separates two models that are close.
- A difference is **significant** only when its interval does not contain 0. An interval that crosses 0 is "not yet
  distinguishable", whatever the point estimate says.

**Fingerprint.** The last section names what a reader needs to compare two reports:

- **App version**: the build's version, with the git commit when the build knew it.
- **Rating formula version**: the version of [RATING.md](RATING.md). Two reports with different formula versions are on
  different scales.
- **Prompt version**: a hash of the prompt text the models were given. A change to the wording changes it. Two reports
  with different prompt versions were played under different instructions and are not directly comparable. For a styled
  candidate it is that style's version, the one `GET /api/prompt-styles` lists. It names the candidate's style only: the
  opponents' styles show in their rows.
- **Generated**: when the report was written.

The Markdown report has no keys, tokens or paths in it, so it is safe to share as it is.

## 6. The API

The scripts use the same endpoints as any client. All paths are under the API (`http://127.0.0.1:5080` by default).

| Call | Result |
|---|---|
| `POST /api/benchmarks` | `201` with the run and its cost estimate. Body: `{"name","candidate","opponents","gamesPerOpponent","maxConcurrency"}`; `candidate` and `opponents` take full player ids. An unknown effort or an odd number of games is a `400` naming the problem. |
| `GET /api/benchmarks` | Every benchmark, newest first. |
| `GET /api/benchmarks/{id}` | One run: `status` (`running`, `finished` or `cancelled`), `gamesFinished` and `gamesTotal`. |
| `GET /api/benchmarks/{id}/report` | The report as JSON. |
| `GET /api/benchmarks/{id}/report?format=md` | The report as Markdown. |
| `POST /api/benchmarks/{id}/cancel` | Cancels a running benchmark. Its queued and running games are aborted. |

```bash
curl -s http://127.0.0.1:5080/api/benchmarks | jq -c '.[] | {name, status, gamesFinished, gamesTotal}'
```

The API refuses a request from another website, so a page in your browser cannot start a run.

## 7. Limits

- A Copilot candidate on the default profile has **not been run** in the environment that wrote this guide (it has no Copilot
  sign-in). The fake profile runs the whole flow, including a custom endpoint. The owner's live run is the check for Copilot.
- The random player (`random`) can be an opponent, not a candidate. `Auto` is left out of the default opponents.
- The price data for a Copilot model comes from the Copilot runtime. A model without a price is left out of the estimate, and
  the script says how many.
- A benchmark's games count against `Arena:MaxConcurrentGames`, as a tournament's do. A broadcast plays outside that cap (see the
  [README](../README.md#cost-ai-credits-per-move)), so a benchmark started during a broadcast adds requests on top
  of the broadcast's.

## 8. Prompt styles: candidates and opponents

A player id may carry a prompt style after its effort: `model[@effort][~style]`. The three styles, what each one sends and
what each one costs are in the [README](../README.md#prompt-styles). A styled player is its own player in the ranking, rated
on the same scale as every other one ([RATING.md §1](RATING.md#1-what-counts-as-a-game)).

Both options take full ids:

- `--candidate fake-a~think`, or `--candidate fake-a@high~think` with an effort as well;
- `--opponents fake-b~think,random`, where a missing style means `classic`.

With no `--opponents`, the opponents are every enabled model except the candidate's model in any effort or style, and `Auto`.
On the fake profile a `fake-a~think` candidate therefore played `fake-b` and `random`; the plain `fake-a` was not one of them.

### Comparing styles

To find out whether a style helps, compare it on the same field:

1. **A tournament with the styles.** On the Tournaments page, tick the models and the style chips (Classic, Board, Think, then
   move): every chosen model plays every chosen style, round robin. Over the API, list the full ids:
   `POST /api/tournaments` with `"models": ["fake-a", "fake-a~think"]`.
2. **The Results page.** The **Prompt styles compared** section appears once a model has played under two styles. For each
   style it gives the rating with its 95 % interval, the games, legality, move quality (when Stockfish data exists) and the
   cost per game, and each style's verdict against the baseline (classic, or the first style that played). The style filter
   above the leaderboard shows one style's players. It hides rows and never refits the ratings.
3. **A benchmark of the styled candidate**, for a report to share, run the same way as the plain candidate so both reports
   have the same opponents. The recorded run below uses `--games 4`; the default is 30 games against each opponent.

The fingerprint's prompt version is the candidate's style version, from `GET /api/prompt-styles`. Two reports with different
candidate versions were played under different instructions and are not directly comparable.

### A recorded fake run

The run below was recorded against a fresh fake profile (`dotnet run --project src/Chess.AppHost --launch-profile fake`).
The commands are exactly as they were run. `…` marks trimmed output. The fake model's tokens and results say nothing about
Copilot models.

**The styles and their versions.**

```bash
curl -s http://127.0.0.1:5080/api/prompt-styles | jq -r '.[] | [.id, .version, .perMoveConversation] | @tsv'
```

```text
classic	sha256:9b00ea00ef62	false
board	sha256:3548a18e635a	true
think	sha256:7283339d2a04	true
```

**Ids the API refuses.** `fake-a~classic` names the default style, `fake-a~nope` names no style, and `random` takes no style.

```bash
curl -s -X POST http://127.0.0.1:5080/api/games -H 'Content-Type: application/json' \
  -d '{"white":{"kind":"llm","modelId":"fake-a~classic"},"black":{"kind":"llm","modelId":"fake-b"},"rated":false}' | jq -r '.detail'
curl -s -X POST http://127.0.0.1:5080/api/games -H 'Content-Type: application/json' \
  -d '{"white":{"kind":"llm","modelId":"fake-a~nope"},"black":{"kind":"llm","modelId":"fake-b"},"rated":false}' | jq -r '.detail'
curl -s -X POST http://127.0.0.1:5080/api/games -H 'Content-Type: application/json' \
  -d '{"white":{"kind":"random","modelId":"random~think"},"black":{"kind":"llm","modelId":"fake-b"},"rated":false}' | jq -r '.detail'
```

```text
The white player: 'fake-a~classic' names the default style: write the plain id, fake-a.
The white player: 'nope' is not a prompt style. The styles are: classic, board, think.
The white random player's model must be 'random'.
```

**One `think` game, and what the model saw.** The game is `fake-a~think` (white) against `fake-b`.

```bash
GID=$(curl -s -X POST http://127.0.0.1:5080/api/games -H 'Content-Type: application/json' \
  -d '{"white":{"kind":"llm","modelId":"fake-a~think"},"black":{"kind":"llm","modelId":"fake-b"},"rated":true}' | jq -r '.id')
until [ "$(curl -s http://127.0.0.1:5080/api/games/$GID | jq -r '.summary.state')" = finished ]; do sleep 2; done
curl -s http://127.0.0.1:5080/api/games/$GID | jq -c '.summary | {result, termination, plyCount}'
```

```text
{"result":"1-0","termination":"checkmate","plyCount":117}
```

The attempts endpoint returns the prompt and the reply of every attempt. This is the first move of the `think` player:

```bash
curl -s "http://127.0.0.1:5080/api/attempts?gameId=$GID&model=fake-a~think&pageSize=200" \
  | jq -r '.items[] | select(.ply == 1) | .prompt, "--- reply ---", .rawReply, "--- parsed: \(.parsedSan), legal: \(.legal)"'
```

```text
You are playing a game of chess. It is White to move.
Position (FEN): rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1
Board (rank 8 at the top, White in upper case):
8 r n b q k b n r
7 p p p p p p p p
6 . . . . . . . .
5 . . . . . . . .
4 . . . . . . . .
3 . . . . . . . .
2 P P P P P P P P
1 R N B Q K B N R
  a b c d e f g h
White: Ke1, Qd1, Ra1, Rh1, Bc1, Bf1, Nb1, Ng1, pawns a2, b2, c2, d2, e2, f2, g2, h2
Black: Ke8, Qd8, Ra8, Rh8, Bc8, Bf8, Nb8, Ng8, pawns a7, b7, c7, d7, e7, f7, g7, h7
Material: White 39, Black 39 (pawn 1, knight and bishop 3, rook 5, queen 9). Balance: level.
Game (PGN moves): none
Legal moves: Na3, Nc3, Nf3, Nh3, a3, a4, b3, b4, c3, c4, d3, d4, e3, e4, f3, f4, g3, g4, h3, h4
Answer with a short analysis, then the line `Plan: <one sentence>` and the line `Move: <SAN>`.
--- reply ---
Analysis: the legal moves are listed in the prompt, and a3 is one of them.
Plan: keep the pieces coordinated.
Move: a3
--- parsed: a3, legal: true
```

The system prompt is not stored with the attempt. For `think` it is the goal of `board` (play to win, trade only when the
trade gains something, a draw is worth half a point, activate the king in the endgame) and the answer format.

The third move of the same player carries the plan from its last move, and the game so far in PGN:

```bash
curl -s "http://127.0.0.1:5080/api/attempts?gameId=$GID&model=fake-a~think&pageSize=200" \
  | jq -r '.items[] | select(.ply == 3) | .prompt, "--- reply ---", .rawReply'
```

```text
You are playing a game of chess. It is White to move.
Your plan from your last move: keep the pieces coordinated.
Position (FEN): rnbqkbnr/p1pppppp/1p6/8/8/P7/1PPPPPPP/RNBQKBNR w KQkq - 0 2
…
Game (PGN moves): 1. a3 b6
Legal moves: Nc3, Nf3, Nh3, Ra2, a4, b3, b4, c3, c4, d3, d4, e3, e4, f3, f4, g3, g4, h3, h4
Answer with a short analysis, then the line `Plan: <one sentence>` and the line `Move: <SAN>`.
--- reply ---
Analysis: the legal moves are listed in the prompt, and c4 is one of them.
Plan: keep the pieces coordinated.
Move: c4
```

**Tokens per move.** The cost endpoint averages each id's stored moves. Columns: id, moves, average input tokens, average
output tokens.

```bash
curl -s http://127.0.0.1:5080/api/cost/inputs | jq -r '.models[] | [.modelId, .moves, (.averageInputTokens | round), (.averageOutputTokens | round)] | @tsv'
```

```text
fake-a	200	98	13
fake-a~think	259	357	43
fake-b	58	97	13
```

`think` writes about three times the output tokens of `classic` here (43 against 13). It also reads more input, because its
prompt carries the board and the whole game in PGN, and each move starts a new conversation. `board` has the same prompt, so
its input grows with the game.

**A tournament with two styles.**

```bash
CREATED=$(curl -s -X POST http://127.0.0.1:5080/api/tournaments -H 'Content-Type: application/json' \
  -d '{"name":"Q4 style comparison","models":["fake-a","fake-a~think"],"gamesPerPair":2,"maxConcurrency":2}')
TID=$(echo "$CREATED" | jq -r '.tournament.id')
until [ "$(curl -s http://127.0.0.1:5080/api/tournaments/$TID | jq -r '.status')" != running ]; do sleep 3; done
curl -s http://127.0.0.1:5080/api/tournaments/$TID | jq -c '{status, gamesTotal, gamesFinished}'
curl -s "http://127.0.0.1:5080/api/ratings?tournamentId=$TID" | jq -r '.models[] | [.model, (.rating | round), .games] | @tsv'
```

```text
{"status":"finished","gamesTotal":2,"gamesFinished":2}
fake-a~think	100	2
fake-a	-100	2
```

The same two players in the browser: the Tournaments page's style chips read `Classic`, `Board` and `Think, then move`. With Fake A and
Fake B ticked and the Think chip ticked, the estimate panel lists four players: `Fake A`, `Fake A · Think, then move`, `Fake B`
and `Fake B · Think, then move`. An id with no stored moves yet takes its model's history, as `Fake B · Board` does below:

```text
Fake B | $0.01 / $0.04 per 1M tokens | 0 | 0 credits | $0.0001 | from 1,033 past moves
Fake B · Board | $0.01 / $0.04 per 1M tokens | 0 | 0 credits | $0.0001 | from 1,033 past moves
```

On the Results page, the **Prompt styles compared** section and the style filter then showed (the fake ratings of the whole
store, so the numbers are not a result):

```text
Prompt styles compared
Model	Style	Rating	95 % interval	Games	Legality	Move quality	Cost per game	Against the baseline
fake-b	classic	-57	-345 to 287	11	100%	—	$0.000	Baseline
fake-b	think	-34	-389 to 319	8	100%	—	$0.001	No significant difference from classic: RQC 0.52, 95 % interval 0.33 to 0.69.
fake-a	classic	-226	-583 to 149	10	100%	—	$0.000	Baseline
fake-a	think	-162	-449 to 151	17	100%	—	$0.001	No significant difference from classic: RQC 0.57, 95 % interval 0.38 to 0.77.
```

The style chips above the leaderboard read `All`, `classic`, `think`: a style's chip appears once it has played, so `board`
is absent until a board game is played. Choosing `think` leaves the rows `fake-b~think` and `fake-a~think` in the leaderboard.

**A benchmark of a styled candidate.** `--games 4` keeps the smoke run short; the default is 30.

```bash
scripts/benchmark.sh --candidate fake-a~think --games 4 --out-dir ~/benchmarks
grep 'Prompt version' ~/benchmarks/benchmark-fake-a-think.md
```

```text
Benchmark f3ec1818-ca17-45ab-a95b-c39769e411e6: fake-a~think against 2 opponent(s), 8 games in all (2 concurrent).
Estimate: 0 premium request(s), about $0.0019; 0 model(s) without a price are not counted.
[10:46:32] running: 0 of 8 games finished
[10:46:42] finished: 8 of 8 games finished
Wrote <out-dir>/benchmark-fake-a-think.md and …json (8 games, score 25%).
- Prompt version: sha256:7283339d2a04
```

The candidate's version is `sha256:7283339d2a04`, the `think` version above. The opponents were `fake-b` and `random`, as the
default rule gives.

A styled opponent:

```bash
scripts/benchmark.sh --candidate fake-a --opponents fake-b~think --games 2 --out-dir ~/benchmarks
grep -E 'Prompt version|^\| fake-b~think' ~/benchmarks/benchmark-fake-a.md
```

```text
Benchmark da90e0ff-9e86-415c-8238-1e3c093d9e6c: fake-a against 1 opponent(s), 2 games in all (2 concurrent).
Estimate: 0 premium request(s), about $0.0004; 0 model(s) without a price are not counted.
[10:46:42] running: 0 of 2 games finished
[10:46:52] finished: 2 of 2 games finished
Wrote <out-dir>/benchmark-fake-a.md and …json (2 games, score 25%).
| fake-b~think | 2 | 0 | 1 | 1 | 0 | 25.0 % | -191 (−∞ to 0) | 0.33 (0.17 to 0.68) | 100.0 % | $0.0003 |
- Prompt version: sha256:9b00ea00ef62
```

The plain candidate's version is `classic`'s (`sha256:9b00ea00ef62`), and its row shows the styled opponent `fake-b~think`.

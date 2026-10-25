# Benchmark a model

A benchmark plays one **candidate** model, in one configuration, against every other model: a fixed number of rated games
against each opponent, with colours alternating. You get a report you can share: a Markdown page and the same numbers as
JSON. The games are ordinary rated games, so they also move the global ranking on the Results page.

This works for three kinds of candidate:

- a **Copilot model**, e.g. `claude-sonnet-4.5`;
- a **variant** with a reasoning effort, `<model>@<effort>`, e.g. `claude-sonnet-4.5@medium`. Each effort is ranked as
  its own player;
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
| `--candidate ID` [`-Candidate`] | required | The model to benchmark: `<model>` or `<model>@<effort>`. |
| `--opponents LIST` [`-Opponents`] | every enabled model except the candidate and `Auto` | Comma-separated opponent ids. A reference panel (section 4) is the usual choice. |
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

Every LLM move is a request. With the Copilot models a move costs premium requests, depending on the model's multiplier
(see [the README's cost section](../README.md#cost-every-move-is-a-copilot-request)). A benchmark costs the same as the
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
| `ApiKey` | The key the endpoint expects. See below. |

**Keep the key out of the repository.** It is read from configuration only: an environment variable, or a user secret. It is
never logged, printed, or stored in the database. One simple way is a file outside the repository, readable only by you, which
you load into the environment for one shell session:

```bash
mkdir -p "$HOME/.config/llm-arena"
printf '%s\n' "not-a-real-key" > "$HOME/.config/llm-arena/my-llama.key"
chmod 600 "$HOME/.config/llm-arena/my-llama.key"
export Arena__CustomModels__0__ApiKey="$(cat "$HOME/.config/llm-arena/my-llama.key")"
```

Put your real key in that file, and never in a file under the repository. Then set the rest of the entry as environment
variables for the same session and start the app. This example uses the fake profile, so it runs with no
Copilot login, and it declares a model called `my-llama`:

```bash
export Arena__CustomModels__0__Id=my-llama
export Arena__CustomModels__0__ProviderType=openai
export Arena__CustomModels__0__BaseUrl=http://127.0.0.1:8000/v1
export Arena__CustomModels__0__Model=llama-local
dotnet run --project src/Chess.AppHost --launch-profile fake
```

The `Id` of each entry is the part of the variable name after `Arena__CustomModels__`, so entries are numbered `0`, `1`, and
so on. A bad entry stops the app at startup, with a message that names it and never quotes the URL or the key.

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
  with different prompt versions were played under different instructions and are not directly comparable.
- **Generated**: when the report was written.

The Markdown report has no keys, tokens or paths in it, so it is safe to share as it is.

## 6. The API

The scripts use the same endpoints as any client. All paths are under the API (`http://127.0.0.1:5080` by default).

| Call | Result |
|---|---|
| `POST /api/benchmarks` | `201` with the run and its cost estimate. Body: `{"name","candidate","opponents","gamesPerOpponent","maxConcurrency"}`. An unknown effort or an odd number of games is a `400` naming the problem. |
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
  [README](../README.md#cost-every-move-is-a-copilot-request)), so a benchmark started during a broadcast adds requests on top
  of the broadcast's.

# Results archive

The `results/` folder holds finished rated games as files, one file per game. A game that ended undecided (result `*`: a
cancelled tournament's queued games, a game cut off by a restart or an aborted one) is never exported, so it is not in the archive and not on the site. Git is the database: history, review and
authorship come with it. The ratings are not stored; the site computes them from the games with the formula in
[RATING.md](RATING.md), as the app does for its own games.

## Add your games

The steps are in [CONTRIBUTING.md](../CONTRIBUTING.md#contribute-your-results): fork, start the app with
`Arena__Results__Archive__ExportFolder` set, play, export with `POST /api/results/archive/export`, commit the files under
`results/games/` and open a pull request.

The pull request is checked by `ResultsArchiveTests.The_results_folder_of_the_repository_is_valid` in the `build and test`
job. It replays every file on the engine and requires each file at the path its id and date name.

## File

```json
{
  "version": 1,
  "id": "0b0f6a0e-0000-0000-0000-000000000000",
  "white": { "kind": "llm", "model": "model-a" },
  "black": { "kind": "random", "model": "random" },
  "result": "1-0",
  "termination": "checkmate",
  "playedAt": "2026-11-01T12:00:00Z",
  "moves": [ { "san": "e4" }, { "san": "e5", "attempts": 2, "latencyMs": 900, "inputTokens": 120, "outputTokens": 8 } ]
}
```

The arena plays every game from the standard position, so `startFen` is left out; a file may give the standard position,
and any other position is refused. A move may carry `attempts` (the rating's legal-first-try figure, 1 to 20), `latencyMs`
(0 to 3 600 000), `inputTokens` and `outputTokens` (0 to 10 000 000), `centipawnLoss` (0 to 100 000) and `bestSan` (a SAN move
of at most 12 characters). Prompts and raw replies are not archived. Unknown fields are refused. `termination` is
`checkmate`, `stalemate`, `insufficient-material`, `fifty-move`, `threefold`, `forfeit-white`, `forfeit-black`,
`adjudicated` or `budget`. `playedAt` is a UTC date from 2024 on and not more than a day ahead of the clock of the machine
that checks it.

A model id is 1 to 100 characters from `A-Z a-z 0-9 . _ @ ~ : / + -`, and does not contain `--`. Two ids that differ only by
case, or only by characters the site turns into a dash (`gpt-5@high` and `gpt-5-high`), would share one page on the public
site, so the second spelling is refused at its file; use the spelling the first file uses.

## What review decides

The replay proves a game is possible: legal moves, a result the rules or the runner can give. It cannot prove which model
played it, so a maintainer reviews each pull request before the ranking changes. It also cannot prove an adjudication: an
`adjudicated` or `budget` game ends at the runner's verdict (Stockfish's evaluation, or the material count without it,
[RATING.md §2](RATING.md)), which depends on an engine the check does not run. The replay therefore only requires the
final position to be one where the game was still in play, and accepts any of the three results at any length. A maintainer
reads those files with that in mind. Files are append-only: correct a wrong game
by a pull request that deletes it.

## The site

`publish-site.yml` runs on a push to `main` that touches `results/`, the code that makes the site or the workflow, and by hand.
It tests the site and archive code, starts the API on the fake profile with `Arena:Results:Archive:ImportFolder=results`,
waits for the import to finish (the games are in the database and their rating snapshots are written, so the rating history is
there), exports the site with `Arena:Site:ExportOnceTo`, checks it (no script, under the 1 GB limit of a Pages site) and
deploys it to GitHub Pages. Set Settings, Pages, Source to GitHub Actions once. The generated site is not committed.

The import status at `GET /api/results/archive/status` is `{"state":"done","added":N,"snapshotBatches":M}` when it has finished:
`added` games were new, and `snapshotBatches` rating snapshot batches were written for them (one per rated game).

The owner's own publish to a branch (README, Public results site, "Publishing from the app") is an advanced option. It exports
the owner's database, not the archive, and it does not serve the public URL.

## Growth

A game file is a few KB. When the folder passes tens of thousands of files, move old months into one JSONL file per month
and keep reading both shapes.

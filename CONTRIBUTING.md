# Contributing

Thank you for taking the time to contribute. The product, its layout and its rules are in
[CLAUDE.md](CLAUDE.md); the architecture is in [docs/architecture/](docs/architecture/).

## Before you start

- Install the .NET 10 SDK (`global.json` pins the 10.0 feature band). `scripts/dev-setup.sh` reports what is installed.
- Read the invariants in [CLAUDE.md](CLAUDE.md#invariants). The CI jobs enforce several of them, and the rest are review findings.
- Contracts in [03-TARGET-ARCHITECTURE §3 and §5.1](docs/architecture/03-TARGET-ARCHITECTURE.md) are frozen. A change to one is a
  decision for the maintainer, not part of an ordinary pull request.

## Workflow

1. Create a branch from the latest `main`, named after the task (`task/<id>-<slug>`) or otherwise descriptive.
2. Keep the change focused. Touch only the files it needs, and say in the pull request what is outside that scope and why.
3. Add or update tests for behaviour you change. Tests need no network and no Copilot login: use `FakeLlmGateway`, the InMemory
   database provider and fixtures.
4. Update the documentation the change affects, in the same pull request. The README must describe only what the code does.
   Every command in the docs must be one you have run.
5. Open a pull request against `main`. Use the template in [`.github/pull_request_template.md`](.github/pull_request_template.md),
   and title it like the history does: `<ID>: <summary>` for a planned task, or `fix(<area>): <summary>` for a fix.

## Checks before you push

```bash
dotnet build Chess.sln -warnaserror
dotnet test Chess.sln
```

Both must pass with no warnings. CI runs the same build and tests, plus these jobs:

- **secret scan**: gitleaks over the commits reachable from `HEAD`.
- **kernel size guard**: `Chess.ServiceDefaults` stays at or below 400 lines of C#.
- **Copilot SDK only in Chess.Llm**: no other project may reference `GitHub.Copilot.SDK`.
- **solution and AppHost**: every `.csproj` under `src/` and `tests/` is in `Chess.sln`, and every web project is registered in
  `src/Chess.AppHost/AppHost.cs`.

A new project goes into `Chess.sln` in the same pull request. A new web project is also registered in the AppHost.

## Contribute your results

Finished rated games can join the public ranking. Each game is one JSON file in `results/games/`, added by pull request.
The file format and the checks are in [docs/RESULTS-ARCHIVE.md](docs/RESULTS-ARCHIVE.md).

1. **Fork and clone** the repository, then make a branch:

   ```bash
   git clone https://github.com/<your-user>/llm-multi-agent-chess.git
   cd llm-multi-agent-chess
   git checkout -b add-my-results
   ```

2. **Start the app with the export folder set** to the `results/` folder of your clone. The variable is read by the AppHost
   and passed to the API. The `fake` profile needs no Docker or Copilot login, so it is the quick way to try the steps:

   ```bash
   Arena__Results__Archive__ExportFolder="$PWD/results" dotnet run --project src/Chess.AppHost --launch-profile fake
   ```

   For real models, run the same line without `--launch-profile fake`. That needs Docker and a Copilot login, see
   [Prerequisites](README.md#prerequisites).

3. **Play rated games.** Use the Play page at <http://localhost:5070> and keep the game rated. The API is at
   <http://127.0.0.1:5080>. Only games with real models are results: the `fake-a` and `fake-b` games of the `fake` profile
   are test data, do not submit them.

4. **Export the finished games** into your clone. Do this before you stop the app, because the `fake` profile keeps its
   database in memory and its games are gone when it stops:

   ```bash
   curl -X POST http://127.0.0.1:5080/api/results/archive/export
   ```

   The answer is `{"written":N,"alreadyThere":M,"folder":"..."}`. `written` is the number of new files. The export never
   rewrites a file that exists.

5. **Check the files** with the test that CI runs on your pull request:

   ```bash
   dotnet test tests/Chess.Api.Tests --filter ResultsArchiveTests
   ```

   It replays every game on the engine and reports any file that does not parse, is illegal, sits at the wrong path or repeats
   a game id.

6. **Commit only the new files and open a pull request:**

   ```bash
   git add results/games
   git commit -m "Add results: <models played>"
   git push -u origin add-my-results
   ```

   Open the pull request on GitHub against `main`. A maintainer reviews it. The replay proves that the moves are legal and the
   result fits them; it cannot prove which model played the game, so the review is what checks that.

Rules for the files: never edit or rename a file that is already in `results/`. A wrong game is removed by a pull request that
deletes its file. Each game id appears once.

## Secrets

Never commit a token, a password, a connection string with credentials, or a personal path. Copilot and PostgreSQL secrets are
AppHost secret parameters. To scan staged changes locally, install the gitleaks pre-commit hook with `scripts/setup-hooks.sh`.
That script refuses to run unless gitleaks 8.21.2 is on `PATH`, and it never overwrites a hook it did not install.

## Running the app

```bash
dotnet run --project src/Chess.AppHost --launch-profile fake   # no Docker, no Copilot
dotnet run --project src/Chess.AppHost                         # PostgreSQL in Docker, Copilot if logged in
```

The fake profile is enough for most UI and API work. Live Copilot runs cost premium requests, so the owner runs them, not CI:
see [docs/EVALS.md](docs/EVALS.md).

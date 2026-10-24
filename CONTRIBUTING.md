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

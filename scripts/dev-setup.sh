#!/usr/bin/env bash
# Reports what the local run needs and what is missing. It never installs anything.
# Exit status is 1 only when the .NET 10 SDK is missing; the other items are optional
# for the fake profile (see docs/architecture/03 §4 and 06-TASKS T1.2).
set -u

required_missing=0
ok() { printf '[ok]       %s\n' "$1"; }
miss() { printf '[missing]  %s\n' "$1"; }
info() { printf '[info]     %s\n' "$1"; }

if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  ok ".NET 10 SDK $(dotnet --list-sdks | grep '^10\.' | tail -1 | cut -d' ' -f1)"
else
  miss ".NET 10 SDK (required). Install: sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0"
  required_missing=1
fi

if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  ok "Docker daemon reachable (Postgres container for the default profile)"
else
  miss "Docker daemon (needed by the default profile). Without it use: dotnet run --project src/Chess.AppHost --launch-profile fake"
fi

if command -v copilot >/dev/null 2>&1; then
  ok "copilot CLI on PATH"
  info "login state is not read by this script. Run 'copilot' and confirm it reports a logged-in user."
else
  miss "copilot CLI (needed for LLM players). Without it the app runs with random players only."
fi

if command -v stockfish >/dev/null 2>&1; then
  ok "stockfish on PATH (move-quality analysis enabled)"
else
  info "stockfish not on PATH (optional: move-quality analysis is disabled without it)"
fi

if command -v gitleaks >/dev/null 2>&1; then
  ok "gitleaks on PATH (scripts/setup-hooks.sh can install the pre-commit hook)"
else
  info "gitleaks not on PATH (optional locally; CI always runs it)"
fi

exit "${required_missing}"

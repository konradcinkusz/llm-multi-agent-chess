#!/usr/bin/env bash
# Live smoke test of the real Copilot gateway (06-TASKS T4.2). Run by the owner on a machine with a Copilot login.
#
# It starts Chess.Api with Llm:Provider=Copilot, plays one rated game (random as White against <model> as Black)
# capped at 20 plies, and prints the result, the model's attempts and its latency. Every model move is a Copilot
# request, so the run spends AI credits. It is never part of CI: it refuses to run when CI is set, and it
# refuses to run when Llm:Provider would be Fake.
#
# Usage:
#   ARENA_CONNECTION_STRING='Host=...;Database=...;Username=...;Password=...' scripts/smoke-copilot.sh <model-id>
#
#   <model-id>               a Copilot model id as listed by GET /api/models
#   ARENA_CONNECTION_STRING  PostgreSQL connection string for ConnectionStrings:chess (required)
#   ARENA_API_URL            where the API listens (default http://127.0.0.1:5080, as in appsettings.json)
#   SMOKE_TIMEOUT_SECONDS    how long the game may take before the script gives up (default 1800)
#
# Requires dotnet (10.x), curl and jq. Exit status: 0 the game finished; 1 a step failed; 2 usage or refusal.
set -euo pipefail

readonly MAX_PLIES=20
readonly RANDOM_MODEL="random"
readonly DEFAULT_API_URL="http://127.0.0.1:5080"
readonly DEFAULT_TIMEOUT_SECONDS=1800
readonly STARTUP_SECONDS=180

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
api_project="${repo_root}/src/Chess.Api"

die() {
  printf 'smoke-copilot: %s\n' "$1" >&2
  exit "${2:-1}"
}

if [[ -n "${CI:-}" ]]; then
  die "refusing to run under CI. This script spends Copilot AI credits and is run by the owner only." 2
fi

if [[ $# -ne 1 || -z "$1" ]]; then
  echo "usage: ARENA_CONNECTION_STRING=... scripts/smoke-copilot.sh <model-id>" >&2
  exit 2
fi
model="$1"

for tool in dotnet curl jq; do
  command -v "${tool}" >/dev/null 2>&1 || die "${tool} is required but not on PATH."
done

# The effective Llm:Provider. An environment override (Llm__Provider, any letter case) wins over appsettings.json.
configured_provider() {
  local override
  override="$(env | grep -i '^llm__provider=' | tail -n 1 | cut -d= -f2- || true)"
  if [[ -n "${override}" ]]; then
    printf '%s' "${override}"
  else
    jq -r '.Llm.Provider // "Copilot"' "${api_project}/appsettings.json"
  fi
}

provider="$(configured_provider)"
if [[ "$(printf '%s' "${provider}" | tr '[:upper:]' '[:lower:]')" != "copilot" ]]; then
  die "Llm:Provider would be '${provider}', not Copilot. This script runs the real gateway only. Unset Llm__Provider or set it to Copilot." 2
fi

connection="${ARENA_CONNECTION_STRING:-}"
if [[ -z "${connection}" ]]; then
  die "ARENA_CONNECTION_STRING is not set. Give the PostgreSQL connection string (ConnectionStrings:chess)." 2
fi

api_url="${ARENA_API_URL:-${DEFAULT_API_URL}}"
timeout_seconds="${SMOKE_TIMEOUT_SECONDS:-${DEFAULT_TIMEOUT_SECONDS}}"
build_dir="$(mktemp -d)"
api_log="$(mktemp)"
api_pid=""

cleanup() {
  if [[ -n "${api_pid}" ]] && kill -0 "${api_pid}" 2>/dev/null; then
    kill "${api_pid}" 2>/dev/null || true
    wait "${api_pid}" 2>/dev/null || true
  fi
  rm -rf "${build_dir}"
}
trap cleanup EXIT

echo "Building Chess.Api (Release)"
dotnet build "${api_project}" -c Release -o "${build_dir}" --nologo -v minimal >/dev/null \
  || die "the build failed"

# The variables are passed to this process only. The connection string is never printed.
echo "Starting Chess.Api with Llm:Provider=Copilot and Arena:MaxPlies=${MAX_PLIES} (log: ${api_log})"
env \
  Llm__Provider=Copilot \
  Arena__MaxPlies="${MAX_PLIES}" \
  ConnectionStrings__chess="${connection}" \
  Kestrel__Endpoints__Http__Url="${api_url}" \
  dotnet "${build_dir}/Chess.Api.dll" >"${api_log}" 2>&1 &
api_pid=$!

deadline=$(( $(date +%s) + STARTUP_SECONDS ))
until curl -fsS "${api_url}/api/status" >/dev/null 2>&1; do
  kill -0 "${api_pid}" 2>/dev/null || die "Chess.Api exited during start-up; see ${api_log}"
  if (( $(date +%s) >= deadline )); then
    die "Chess.Api did not answer on ${api_url} within ${STARTUP_SECONDS} s; see ${api_log}"
  fi
  sleep 2
done

status="$(curl -fsS "${api_url}/api/status")"
copilot_state="$(jq -r '.copilot' <<<"${status}")"
if [[ "${copilot_state}" != "healthy" ]]; then
  die "the Copilot gateway is ${copilot_state}: $(jq -r '.copilotDetail' <<<"${status}"). Run 'copilot', log in, then run this script again."
fi
echo "Copilot gateway: ready"

models="$(curl -fsS "${api_url}/api/models")"
if ! jq -e --arg m "${model}" 'any(.[]; .id == $m and .enabled == true)' <<<"${models}" >/dev/null; then
  die "'${model}' is not an enabled model in GET /api/models"
fi

body="$(jq -cn --arg m "${model}" --arg r "${RANDOM_MODEL}" \
  '{white: {kind: "random", modelId: $r}, black: {kind: "llm", modelId: $m}, rated: true}')"
created="$(curl -fsS -X POST -H 'Content-Type: application/json' --data "${body}" "${api_url}/api/games")" \
  || die "POST /api/games failed; see ${api_log}"
game_id="$(jq -r '.id' <<<"${created}")"
echo "Game ${game_id}: random (White) against ${model} (Black), rated, capped at ${MAX_PLIES} plies"

deadline=$(( $(date +%s) + timeout_seconds ))
while :; do
  detail="$(curl -fsS "${api_url}/api/games/${game_id}")" || die "GET /api/games/${game_id} failed"
  state="$(jq -r '.summary.state' <<<"${detail}")"
  if [[ "${state}" == "finished" ]]; then
    break
  fi
  if (( $(date +%s) >= deadline )); then
    die "game ${game_id} did not finish within ${timeout_seconds} s; its state is ${state}"
  fi
  sleep 5
done

# Every Copilot request for the model's side, forfeit requests included (they have no row in the moves list).
attempts="$(curl -fsS "${api_url}/api/attempts?gameId=${game_id}&pageSize=200")" \
  || die "GET /api/attempts failed; see ${api_log}"
model_attempts="$(jq -c --arg m "${model}" '[.items[] | select(.modelId == $m)]' <<<"${attempts}")"

result="$(jq -r '"\(.summary.result) (\(.summary.termination))"' <<<"${detail}")"
plies="$(jq -r '.summary.plyCount' <<<"${detail}")"
model_moves="$(jq '[.moves[] | select(.color == "black")] | length' <<<"${detail}")"
requests="$(jq 'length' <<<"${model_attempts}")"
rejected="$(jq '[.[] | select(.legal == false)] | length' <<<"${model_attempts}")"
latency_ms="$(jq 'if length == 0 then 0 else ([.[].latencyMs] | add / length | round) end' <<<"${model_attempts}")"

printf '\n'
printf 'Result:    %s after %s plies\n' "${result}" "${plies}"
printf 'Model:     %s (Black), %s moves played\n' "${model}" "${model_moves}"
printf 'Attempts:  %s Copilot requests, %s rejected (illegal, unparseable or empty)\n' "${requests}" "${rejected}"
printf 'Latency:   %s ms average per request\n' "${latency_ms}"

# Per request: tokens, AI credits and whether the reply carried reasoning. A field the API does not return yet prints n/a.
# TODO(G2): print the game's usage from GET /api/games/${game_id}/usage once G2 is merged; the endpoint is not on main yet.
printf '\nRequests (ply, number, verdict, tokens in/out, AI credits, reasoning):\n'
jq -r '.[] | "  ply \(.ply) request \(.number): \(if .legal then "legal" else "rejected" end), tokens \(.inputTokens // "n/a")/\(.outputTokens // "n/a"), credits \(.aiCredits // "n/a"), reasoning \(if (.reasoning // "") == "" then "no" else "yes" end)"' <<<"${model_attempts}"

printf '\n'
printf 'Log:       %s\n' "${api_log}"

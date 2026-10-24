#!/usr/bin/env bash
# Broadcast smoke test (T5.6, docs/BROADCAST.md): starts the arena with the fake profile, plans a one-hour schedule with
# 50 ms moves and 1 s game gaps, starts it, and prints /api/broadcast/now until the schedule is finished. Then it prints the
# standings. Exits non-zero when the schedule is not finished within the time limit.
#
# Needs the .NET 10 SDK, curl and jq. No Docker and no Copilot login: the fake profile runs on the InMemory store.

set -euo pipefail

readonly API="http://localhost:5080"
readonly ALIVE_TIMEOUT_SECONDS=240
readonly RUN_TIMEOUT_SECONDS=120

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly repo_root

pid_file="$(mktemp)"
log_file="$(mktemp)"
readonly pid_file log_file

cleanup() {
    local status=$?
    if [[ -s "${pid_file}" ]]; then
        # Only the AppHost this script started: its PID is the one in the file, and nothing else is signalled.
        local pid
        pid="$(cat "${pid_file}")"
        kill "${pid}" 2>/dev/null || true
        wait "${pid}" 2>/dev/null || true
        # The AppHost stops the API and the web app it started a few seconds after itself: wait until the port is free.
        for _ in {1..30}; do
            curl -fsS "${API}/alive" >/dev/null 2>&1 || break
            sleep 1
        done
    fi
    rm -f "${pid_file}"
    if (( status != 0 )); then
        echo "The AppHost log is at ${log_file}" >&2
    else
        rm -f "${log_file}"
    fi
    return "${status}"
}
trap cleanup EXIT

if curl -fsS "${API}/alive" >/dev/null 2>&1; then
    echo "Something already answers on ${API}; stop it first, the smoke test needs that port." >&2
    exit 1
fi

cd "${repo_root}"
echo "Starting the AppHost with the fake profile (log: ${log_file})"
ARENA_FAKE=1 ASPIRE_ALLOW_UNSECURED_TRANSPORT=true ASPNETCORE_ENVIRONMENT=Development \
    dotnet run --project src/Chess.AppHost --no-launch-profile >"${log_file}" 2>&1 &
echo $! >"${pid_file}"

alive_deadline=$((SECONDS + ALIVE_TIMEOUT_SECONDS))
until curl -fsS "${API}/alive" >/dev/null 2>&1; do
    if ! kill -0 "$(cat "${pid_file}")" 2>/dev/null; then
        echo "The AppHost stopped before the API answered /alive." >&2
        tail -n 40 "${log_file}" >&2
        exit 1
    fi
    if (( SECONDS >= alive_deadline )); then
        echo "The API did not answer /alive within ${ALIVE_TIMEOUT_SECONDS} s." >&2
        exit 1
    fi
    sleep 2
done
echo "The API is up."

# One hour of planned games, with the window already past: the runner plays the slots back to back, instead of
# waiting an hour of wall-clock time for each planned start. The assumed move latency is set so that seven games fill the hour.
starts_at="$(date -u -d '1 hour ago' '+%Y-%m-%dT%H:%M:%SZ')"
plan_body="$(jq -n --arg startsAt "${starts_at}" '{
    name: "Smoke test",
    models: ["random", "fake-a", "fake-b"],
    startsAt: $startsAt,
    durationHours: 1,
    pacing: { moveDelayMs: 50, gameGapSeconds: 1 },
    seed: 1,
    assumedPlies: 80,
    assumedMoveLatencyMs: 5600
}')"
plan="$(curl -fsS -X POST "${API}/api/broadcast/plan" -H 'Content-Type: application/json' -d "${plan_body}")"
schedule_id="$(jq -r '.schedule.id' <<<"${plan}")"
echo "Planned schedule ${schedule_id} with $(jq '.slots | length' <<<"${plan}") games"

curl -fsS -X POST "${API}/api/broadcast/schedules/${schedule_id}/start" >/dev/null
echo "Started. Polling /api/broadcast/now for up to ${RUN_TIMEOUT_SECONDS} s"

run_deadline=$((SECONDS + RUN_TIMEOUT_SECONDS))
while true; do
    now="$(curl -fsS "${API}/api/broadcast/now" | jq -c .)"
    status="$(curl -fsS "${API}/api/broadcast/schedules/${schedule_id}" | jq -r '.schedule.status')"
    echo "$(date -u +%H:%M:%S) schedule=${status} now=${now}"
    case "${status}" in
        finished) break ;;
        cancelled) echo "The schedule was cancelled." >&2; exit 1 ;;
    esac
    if (( SECONDS >= run_deadline )); then
        echo "Timed out after ${RUN_TIMEOUT_SECONDS} s with the schedule ${status}." >&2
        exit 1
    fi
    sleep 2
done

echo "Schedule finished. Standings from /api/ratings:"
printf '%-16s %8s %6s %10s\n' model rating games arena
curl -fsS "${API}/api/ratings" \
    | jq -r '.models[] | [.model, (.rating | round), .games, (.arenaScore | round)] | @tsv' \
    | while IFS=$'\t' read -r model rating games arena; do
        printf '%-16s %8s %6s %10s\n' "${model}" "${rating}" "${games}" "${arena}"
    done

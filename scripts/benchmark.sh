#!/usr/bin/env bash
# Benchmark one candidate against opponents on a running arena (03 §12.3, ADR-014, docs/BENCHMARK.md).
# Creates the run through the API, follows its progress, and writes benchmark-<name>.md and benchmark-<name>.json.
#
# Needs curl and jq, and an arena already running (the fake profile works without Copilot). Refuses to run under CI.
# Exit status: 0 finished; 1 the API refused or failed, or the run was cancelled; 2 bad usage or CI; 130 interrupted
# (Ctrl-C cancels the run on the server first).

set -euo pipefail

readonly DEFAULT_API="http://127.0.0.1:5080"
readonly DEFAULT_GAMES=30
readonly DEFAULT_CONCURRENCY=2
readonly POLL_SECONDS=10

usage() {
    cat <<'EOF'
Usage: scripts/benchmark.sh --candidate <model[@effort]> [options]

Plays the candidate against each opponent, with colours alternating, and writes the report.

Options:
  --candidate ID     the model to benchmark, e.g. claude-sonnet-4.5@medium (required)
  --opponents LIST   comma-separated opponent ids (default: every enabled model except the candidate and Auto)
  --games N          games against each opponent: an even number, at least 2 (default 30)
  --concurrency N    games played in parallel (default 2, at most Arena:MaxConcurrentGames)
  --name NAME        the run's name; the file names use it too (default: the candidate, with @ replaced by -)
  --out-dir DIR      where benchmark-<name>.md and benchmark-<name>.json are written (default: the current directory)
  -h, --help         show this help

Environment:
  ARENA_API_URL      the API (default http://127.0.0.1:5080)
EOF
}

die() {
    local code="$1"
    shift
    printf 'benchmark.sh: %s\n' "$*" >&2
    exit "${code}"
}

# A variable counts as set when it is non-empty and not false or 0 (GitHub Actions sets CI=true and GITHUB_ACTIONS=true).
is_set() {
    [[ -n "$1" && "$1" != "false" && "$1" != "0" ]]
}

api="${ARENA_API_URL:-${DEFAULT_API}}"
api="${api%/}"

candidate=""
opponents=""
games="${DEFAULT_GAMES}"
concurrency="${DEFAULT_CONCURRENCY}"
name=""
slug=""
out_dir="."
run_id=""

# Set by request: the HTTP status and the response body.
status=""
body=""

# request METHOD PATH [JSON]: one call to the API. Returns 1 when the API cannot be reached; the caller decides what that means.
request() {
    local method="$1" path="$2" raw
    if (( $# >= 3 )); then
        raw="$(curl -sS -X "${method}" -H 'Accept: application/json' -H 'Content-Type: application/json' \
            --data "$3" -w $'\n%{http_code}' "${api}${path}")" || return 1
    else
        raw="$(curl -sS -X "${method}" -H 'Accept: application/json' -w $'\n%{http_code}' "${api}${path}")" || return 1
    fi
    status="${raw##*$'\n'}"
    body="${raw%$'\n'*}"
}

# The problem details of a refused request, one line each, or the raw body when it is not JSON.
explain_refusal() {
    if [[ -z "${body}" ]]; then
        printf '  no details were returned; check the options against --help\n'
        return
    fi
    jq -r '[.title, .detail, (.errors // {} | to_entries[] | "\(.key): \(.value | join(" "))")]
        | map(select(. != null and . != "")) | .[] | "  " + .' <<<"${body}" 2>/dev/null || printf '  %s\n' "${body}"
}

cancel_run() {
    # Interrupted: stop the run on the server too, so no games keep running for an abandoned command.
    if [[ -n "${run_id}" ]]; then
        request POST "/api/benchmarks/${run_id}/cancel" "{}" || true
        printf '\nCancelled benchmark %s on the server.\n' "${run_id}" >&2
    fi
    exit 130
}

parse_arguments() {
    while (( $# > 0 )); do
        case "$1" in
            -h | --help)
                usage
                exit 0
                ;;
            --candidate | --opponents | --games | --concurrency | --name | --out-dir)
                if (( $# < 2 )); then
                    die 2 "$1 needs a value (see --help)"
                fi
                case "$1" in
                    --candidate) candidate="$2" ;;
                    --opponents) opponents="$2" ;;
                    --games) games="$2" ;;
                    --concurrency) concurrency="$2" ;;
                    --name) name="$2" ;;
                    --out-dir) out_dir="$2" ;;
                esac
                shift 2
                ;;
            *)
                die 2 "unknown option '$1' (see --help)"
                ;;
        esac
    done

    if [[ -z "${candidate}" ]]; then
        die 2 "--candidate is required, for example --candidate claude-sonnet-4.5@medium (see --help)"
    fi
    if [[ ! "${games}" =~ ^[0-9]+$ || ! "${concurrency}" =~ ^[0-9]+$ ]]; then
        die 2 "--games and --concurrency must be whole numbers"
    fi
    if [[ -z "${name}" ]]; then
        name="${candidate}"
    fi
    # The file name stem: letters, digits, dot, dash and underscore only, so @ and spaces become dashes.
    slug="${name//[^A-Za-z0-9._-]/-}"
    readonly slug
}

main() {
    parse_arguments "$@"

    if is_set "${CI:-}" || is_set "${GITHUB_ACTIONS:-}"; then
        die 2 "refusing to run under CI: a benchmark plays real games against a live arena (see docs/BENCHMARK.md)"
    fi
    for tool in curl jq; do
        command -v "${tool}" >/dev/null 2>&1 || die 2 "${tool} is required but was not found on PATH"
    done

    if ! request GET /alive || [[ "${status}" != 200 ]]; then
        die 1 "no arena answers at ${api}. Start the app first (docs/BENCHMARK.md), or set ARENA_API_URL."
    fi

    local opponents_json="null"
    if [[ -n "${opponents}" ]]; then
        opponents_json="$(jq -cn --arg list "${opponents}" \
            '$list | split(",") | map(gsub("^ +| +$"; "")) | map(select(length > 0))')"
    fi
    local create_json
    create_json="$(jq -cn --arg name "${name}" --arg candidate "${candidate}" --argjson opponents "${opponents_json}" \
        --argjson games "${games}" --argjson concurrency "${concurrency}" \
        '{name: $name, candidate: $candidate, opponents: $opponents, gamesPerOpponent: $games, maxConcurrency: $concurrency}')"

    if ! request POST /api/benchmarks "${create_json}"; then
        die 1 "could not reach ${api} to create the benchmark"
    fi
    if [[ "${status}" != 201 ]]; then
        echo "The arena refused the benchmark (HTTP ${status}):" >&2
        explain_refusal >&2
        exit 1
    fi

    run_id="$(jq -r '.benchmark.id' <<<"${body}")"
    trap cancel_run INT TERM
    local games_total opponent_count unpriced premium usd
    games_total="$(jq -r '.benchmark.gamesTotal' <<<"${body}")"
    opponent_count="$(jq -r '.benchmark.opponents | length' <<<"${body}")"
    printf 'Benchmark %s: %s against %s opponent(s), %s games in all (%s concurrent).\n' \
        "${run_id}" "${candidate}" "${opponent_count}" "${games_total}" "${concurrency}"

    # The estimate is for the whole run. A model with no price has no estimate, so the total leaves it out, and the
    # count says so.
    read -r premium usd unpriced <<<"$(jq -r '[
            ([.estimatedPremiumRequests[].estimatedPremiumRequests // 0] | add // 0),
            (([.estimatedPremiumRequests[].estimatedUsd // 0] | add // 0) * 10000 | round) / 10000,
            ([.estimatedPremiumRequests[] | select(.estimatedPremiumRequests == null)] | length)
        ] | @tsv' <<<"${body}")"
    printf 'Estimate: %s premium request(s), about $%s; %s model(s) without a price are not counted.\n' \
        "${premium}" "${usd}" "${unpriced}"

    local last="" run_status="" finished="" progress
    while true; do
        if ! request GET "/api/benchmarks/${run_id}" || [[ "${status}" != 200 ]]; then
            die 1 "lost the arena while benchmark ${run_id} ran; it keeps running on the server. Check ${api}/api/benchmarks/${run_id}"
        fi
        read -r run_status finished games_total <<<"$(jq -r '[.status, .gamesFinished, .gamesTotal] | @tsv' <<<"${body}")"
        progress="${run_status} ${finished}/${games_total}"
        if [[ "${progress}" != "${last}" ]]; then
            printf '[%s] %s: %s of %s games finished\n' "$(date '+%H:%M:%S')" "${run_status}" "${finished}" "${games_total}"
            last="${progress}"
        fi
        if [[ "${run_status}" != "running" ]]; then
            break
        fi
        sleep "${POLL_SECONDS}"
    done

    mkdir -p "${out_dir}"
    local json_path="${out_dir}/benchmark-${slug}.json" md_path="${out_dir}/benchmark-${slug}.md"

    if ! request GET "/api/benchmarks/${run_id}/report" || [[ "${status}" != 200 ]]; then
        die 1 "could not read the report of benchmark ${run_id} (HTTP ${status})"
    fi
    if ! jq -e . >/dev/null <<<"${body}"; then
        die 1 "the report of benchmark ${run_id} is not JSON"
    fi
    printf '%s\n' "${body}" >"${json_path}"
    local summary
    summary="$(jq -r '.summary | "\(.games) games, score \((.score * 100) | round)%"' <<<"${body}")"

    if ! request GET "/api/benchmarks/${run_id}/report?format=md" || [[ "${status}" != 200 ]]; then
        die 1 "could not read the Markdown report of benchmark ${run_id} (HTTP ${status})"
    fi
    printf '%s\n' "${body}" >"${md_path}"

    printf 'Wrote %s and %s (%s).\n' "${md_path}" "${json_path}" "${summary}"
    if [[ "${run_status}" != "finished" ]]; then
        die 1 "benchmark ${run_id} ended as '${run_status}'; the report holds the games played so far"
    fi
}

main "$@"

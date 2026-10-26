#!/usr/bin/env bash
# YouTube intake smoke test (V5, 03 §15.5, ADR-016): reads the owner's live chat once and prints its live chat id and the last
# five events the intake would map (Super Chats and !g commands). The owner runs it against a live stream. No arena needs to run,
# and no token is ever printed.
#
# Needs curl and jq. The OAuth secrets are AppHost user secrets (Parameters:youtube-client-id, Parameters:youtube-client-secret,
# Parameters:youtube-refresh-token; set them with `dotnet user-secrets set --project src/Chess.AppHost`, see docs/REQUESTS.md).
# Refuses to run under CI: it reads the owner's account.
#
# Exit status: 0 printed the chat; 1 Google or YouTube refused, or no broadcast is live; 2 bad usage, a missing secret or CI.

set -euo pipefail

readonly TOKEN_URL="${YOUTUBE_TOKEN_URL:-https://oauth2.googleapis.com/token}"
readonly API_BASE="${YOUTUBE_API_BASE:-https://www.googleapis.com/youtube/v3}"
readonly SHOWN_EVENTS=5

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly repo_root

usage() {
    cat <<'EOF'
Usage: scripts/youtube-smoke.sh

Reads the live chat of the channel's active broadcast once, and prints its live chat id and the last five events the
intake would map. Read-only: nothing is recorded and no arena is needed.

Set the secrets first, in the AppHost user secrets:
  dotnet user-secrets set --project src/Chess.AppHost Parameters:youtube-client-id <id>
  dotnet user-secrets set --project src/Chess.AppHost Parameters:youtube-client-secret <secret>
  dotnet user-secrets set --project src/Chess.AppHost Parameters:youtube-refresh-token <token>
EOF
}

die() {
    local status=$1
    shift
    echo "$*" >&2
    exit "${status}"
}

# A variable counts as set when it is non-empty and not false or 0 (GitHub Actions sets CI=true and GITHUB_ACTIONS=true).
is_set() {
    case "${1:-}" in
        "" | false | 0) return 1 ;;
        *) return 0 ;;
    esac
}

if is_set "${CI:-}" || is_set "${GITHUB_ACTIONS:-}"; then
    die 2 "refusing to run under CI: the smoke test reads the owner's live YouTube chat (see docs/REQUESTS.md)"
fi

case "${1:-}" in
    "") ;;
    -h | --help)
        usage
        exit 0
        ;;
    *)
        usage >&2
        die 2 "unknown argument: $1"
        ;;
esac

for tool in curl jq; do
    command -v "${tool}" >/dev/null 2>&1 || die 2 "${tool} is needed; install it and run again"
done

# Reads one AppHost user secret. The value goes into a variable only; it is never echoed.
secret() {
    dotnet user-secrets list --project "${repo_root}/src/Chess.AppHost" 2>/dev/null \
        | awk -v key="Parameters:$1" 'index($0, key " = ") == 1 { print substr($0, length(key) + 4); exit }'
}

client_id="$(secret youtube-client-id)"
client_secret="$(secret youtube-client-secret)"
refresh_token="$(secret youtube-refresh-token)"
for name in client_id client_secret refresh_token; do
    if [[ -z "${!name}" ]]; then
        die 2 "a secret is missing: set Parameters:youtube-${name//_/-} in the AppHost user secrets (see --help)"
    fi
done

# Calls one URL. The body goes to $body and the HTTP status to $status; the arguments go to curl.
call() {
    local url=$1
    shift
    local response
    response="$(curl -sS -w '\n%{http_code}' "$@" "${url}")" || die 1 "could not reach ${url%%\?*}"
    status="${response##*$'\n'}"
    body="${response%$'\n'*}"
}

encode() {
    jq -rn --arg value "$1" '$value | @uri'
}

# The refresh: the secrets travel on stdin, never in the argument list.
call "${TOKEN_URL}" -X POST -H 'Content-Type: application/x-www-form-urlencoded' --data-binary @- <<<"client_id=$(encode "${client_id}")&client_secret=$(encode "${client_secret}")&refresh_token=$(encode "${refresh_token}")&grant_type=refresh_token"
if [[ "${status}" != "200" ]]; then
    die 1 "Google refused the token refresh (HTTP ${status}, $(jq -r '.error // "no error code"' <<<"${body}" 2>/dev/null || echo "no error code")). Check the OAuth client and the refresh token."
fi

access_token="$(jq -r '.access_token // empty' <<<"${body}")"
[[ -n "${access_token}" ]] || die 1 "Google's token refresh answered without an access token"

# The bearer header is read from stdin as curl config, so the token is not in any argument list either.
auth_config="$(printf 'header = "Authorization: Bearer %s"' "${access_token}")"

call "${API_BASE}/liveBroadcasts?part=snippet&mine=true&broadcastStatus=active" -K - <<<"${auth_config}"
if [[ "${status}" != "200" ]]; then
    die 1 "liveBroadcasts.list refused the request (HTTP ${status}, $(jq -r '.error.errors[0].reason // .error.status // "no reason"' <<<"${body}" 2>/dev/null || echo "no reason"))"
fi

live_chat_id="$(jq -r '[.items[]?.snippet.liveChatId // empty] | first // empty' <<<"${body}")"
if [[ -z "${live_chat_id}" ]]; then
    die 1 "no broadcast is live on this channel, so there is no live chat to read. Start the stream and run this again."
fi

call "${API_BASE}/liveChat/messages?liveChatId=$(encode "${live_chat_id}")&part=snippet,authorDetails" -K - <<<"${auth_config}"
if [[ "${status}" != "200" ]]; then
    die 1 "liveChatMessages.list refused the request (HTTP ${status}, $(jq -r '.error.errors[0].reason // .error.status // "no reason"' <<<"${body}" 2>/dev/null || echo "no reason"))"
fi

# The mapping of 03 §15.5, as the intake does it: a superChatEvent with a positive amount and a three-letter currency is a
# deposit, a textMessageEvent starting with !g is a command, the rest is ignored.
readonly MAP='
  [ .items[]?
    | if .snippet.type == "superChatEvent"
         and (((.snippet.superChatDetails.amountMicros // "0") | (tonumber? // 0)) > 0)
         and ((.snippet.superChatDetails.currency // "") | test("^[A-Za-z]{3}$")) then
        { kind: "superChat", id: .id, payerId: .authorDetails.channelId, payerDisplay: .authorDetails.displayName,
          amountMicros: (.snippet.superChatDetails.amountMicros | tonumber), currency: (.snippet.superChatDetails.currency | ascii_upcase),
          text: (.snippet.superChatDetails.userComment // ""), at: .snippet.publishedAt }
      elif .snippet.type == "textMessageEvent" and ((.snippet.textMessageDetails.messageText // "") | test("^!g"; "i")) then
        { kind: "command", id: .id, payerId: .authorDetails.channelId, payerDisplay: .authorDetails.displayName,
          amountMicros: null, currency: null, text: .snippet.textMessageDetails.messageText, at: .snippet.publishedAt }
      else empty end ]'

mapped="$(jq -c "${MAP}" <<<"${body}")"
total="$(jq 'length' <<<"${mapped}")"

echo "Live chat id: ${live_chat_id}"
echo "Mapped events on the first page of the chat: ${total} (the last ${SHOWN_EVENTS} follow)"
if (( total > 0 )); then
    jq -c '.[]' <<<"${mapped}" | tail -n "${SHOWN_EVENTS}"
fi

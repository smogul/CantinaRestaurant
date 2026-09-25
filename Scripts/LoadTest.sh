#!/usr/bin/env bash
# Runs the same read-heavy load against five endpoints and saves hey's full output to LoadTestResults/<Label>.txt.
set -euo pipefail

if [[ $# -ne 1 || ! $1 =~ ^[A-Za-z0-9_-]+$ ]]; then
  echo "Usage: Scripts/LoadTest.sh <Label>   (letters, digits, - or _; for example Baseline or After)" >&2
  exit 1
fi

LABEL=$1
BASE=${BASE:-http://localhost:8080}
CUSTOMER_EMAIL=${CUSTOMER_EMAIL:-customer@cantina.example}
CUSTOMER_PASSWORD=${CUSTOMER_PASSWORD:-ChangeMe-Customer-1}

# Fixed so Baseline and After runs are comparable; override only for a quick smoke check.
WARMUP=${WARMUP:-5s}
DURATION=${DURATION:-30s}
CONCURRENCY=${CONCURRENCY:-50}

fail() {
  echo "Error: $*" >&2
  exit 1
}

for tool in curl jq hey; do
  command -v "$tool" >/dev/null || fail "$tool is not installed. Install it and try again (hey: https://github.com/rakyll/hey)."
done

login_body=$(jq -n --arg email "$CUSTOMER_EMAIL" --arg password "$CUSTOMER_PASSWORD" '{email: $email, password: $password}')
login_response=$(curl -sS -w '\n%{http_code}' -X POST "$BASE/api/auth/login" -H 'Content-Type: application/json' -d "$login_body") \
  || fail "could not reach $BASE. Is the stack running?"
login_status=${login_response##*$'\n'}
[[ $login_status == 200 ]] || fail "login as $CUSTOMER_EMAIL returned HTTP $login_status. Check CUSTOMER_EMAIL and CUSTOMER_PASSWORD."
TOKEN=$(jq -r '.accessToken' <<<"${login_response%$'\n'*}")
AUTH_HEADER="Authorization: Bearer $TOKEN"

# The list is ordered by name, so the same item is picked on every run against the same data.
ITEM_ID=""
for id in $(curl -sS -H "$AUTH_HEADER" "$BASE/api/menu-items?page=1&pageSize=50" | jq -r '.items[].id'); do
  rating_count=$(curl -sS -H "$AUTH_HEADER" "$BASE/api/menu-items/$id" | jq -r '.ratingCount')
  if (( rating_count > 0 )); then
    ITEM_ID=$id
    break
  fi
done
[[ -n $ITEM_ID ]] || fail "no menu item with ratings on the first page. Run Scripts/LoadTestSeed.sql first."

NAMES=("List page 1" "List page 200" "Search milk" "View item" "Item ratings")
PATHS=(
  "/api/menu-items?page=1&pageSize=20"
  "/api/menu-items?page=200&pageSize=20"
  "/api/menu-items/search?q=milk"
  "/api/menu-items/$ITEM_ID"
  "/api/menu-items/$ITEM_ID/ratings"
)

mkdir -p LoadTestResults
OUTPUT="LoadTestResults/$LABEL.txt"
{
  echo "Label: $LABEL"
  echo "Date: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "Commit: $(git rev-parse --short HEAD 2>/dev/null || echo unknown)"
  echo "Base: $BASE"
  echo "Item: $ITEM_ID"
  echo "Load: $CONCURRENCY concurrent workers for $DURATION after a $WARMUP warm-up"
} >"$OUTPUT"

SUMMARY=()
TOTAL_FAILURES=0

for i in "${!PATHS[@]}"; do
  url="$BASE${PATHS[$i]}"
  echo "Running ${NAMES[$i]} ($DURATION)..." >&2

  hey -z "$WARMUP" -c "$CONCURRENCY" -H "$AUTH_HEADER" "$url" >/dev/null
  result=$(hey -z "$DURATION" -c "$CONCURRENCY" -H "$AUTH_HEADER" "$url")
  printf '\n### %s  GET %s\n%s\n' "${NAMES[$i]}" "${PATHS[$i]}" "$result" >>"$OUTPUT"

  requests_per_second=$(awk '/Requests\/sec:/ { print $2 }' <<<"$result")

  # hey prints the percentile with a doubled percent sign, for example "95%% in 0.0029 secs".
  p95_ms=$(awk '/^ *95%+ in / { printf "%.1f", $3 * 1000 }' <<<"$result")

  # Status lines read "[code] N responses"; error lines read "[N] message", so both kinds count as failures.
  failures=$(awk '
    /^ *\[[0-9]+\][ \t]+[0-9]+ responses/ { code = $1; gsub(/[][]/, "", code); if (code != 200) total += $2; next }
    /^ *\[[0-9]+\][ \t]/ { count = $1; gsub(/[][]/, "", count); total += count }
    END { print total + 0 }' <<<"$result")

  TOTAL_FAILURES=$((TOTAL_FAILURES + failures))
  SUMMARY+=("$(printf '%-14s %14s %10s %10s' "${NAMES[$i]}" "${requests_per_second:-n/a}" "${p95_ms:-n/a}" "$failures")")
done

echo
echo "Load test \"$LABEL\" ($CONCURRENCY workers, $DURATION per endpoint, full output in $OUTPUT)"
printf '%-14s %14s %10s %10s\n' "Endpoint" "Requests/sec" "p95 ms" "Non-200"
printf '%s\n' "${SUMMARY[@]}"

if (( TOTAL_FAILURES > 0 )); then
  echo
  echo "Warning: $TOTAL_FAILURES requests did not return 200. Throughput and latency are not meaningful until that is fixed; see $OUTPUT." >&2
fi

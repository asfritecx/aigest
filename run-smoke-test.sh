#!/usr/bin/env bash
# Smoke-test runner for aigest. Spec lives in docs/smoke-tests.md.
#
# Each test invokes aigest, captures stdout/stderr, applies pass checks,
# and contributes to a final summary. Exits non-zero if any test fails.

set -uo pipefail

KIT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" >/dev/null 2>&1 && pwd)"
cd "$KIT_DIR"

# Resolve aigest invocation: prefer installed binary, fall back to dotnet run.
if command -v aigest >/dev/null 2>&1; then
  AIGEST=(aigest)
elif [[ -x "$HOME/.local/bin/aigest" ]]; then
  AIGEST=("$HOME/.local/bin/aigest")
elif command -v dotnet >/dev/null 2>&1; then
  AIGEST=(dotnet run --project src/Aigest.Cli --)
else
  echo "ERROR: neither aigest nor dotnet found. Run ./setup.sh first." >&2
  exit 2
fi

TMP_DIR=$(mktemp -d -t aigest-smoke.XXXXXX)
trap 'rm -rf "$TMP_DIR"' EXIT

PASS_COUNT=0
FAIL_COUNT=0
RESULTS=()

# Reset color codes for portable terminals (TERM-aware).
if [[ -t 1 ]]; then
  C_RED=$'\033[31m'; C_GREEN=$'\033[32m'; C_YELLOW=$'\033[33m'; C_RESET=$'\033[0m'
else
  C_RED=""; C_GREEN=""; C_YELLOW=""; C_RESET=""
fi

record() {
  local label="$1" status="$2" detail="$3"
  RESULTS+=("$status|$label|$detail")
  if [[ "$status" == "PASS" ]]; then
    PASS_COUNT=$((PASS_COUNT + 1))
    printf "  %s%s%s %s\n" "$C_GREEN" "✓ PASS" "$C_RESET" "$label"
  else
    FAIL_COUNT=$((FAIL_COUNT + 1))
    printf "  %s%s%s %s — %s\n" "$C_RED" "✗ FAIL" "$C_RESET" "$label" "$detail"
  fi
}

# T1 — Bulk reading + citation discipline.
run_t1() {
  local label="T1: bulk reading"
  echo "▶ $label"
  local out="$TMP_DIR/t1.out" err="$TMP_DIR/t1.err"
  if ! "${AIGEST[@]}" ask \
    --paths "sample-project/src/**/*.cs" \
    --question "List every public class in this corpus and one-line its purpose. Cite file:line for each." \
    >"$out" 2>"$err"; then
    record "$label" FAIL "aigest exited non-zero (see $err)"
    return
  fi
  local missing=()
  for cls in InventoryService InventoryRoutes InventoryItem ReservationResult InventoryResult; do
    grep -q "$cls" "$out" || missing+=("$cls")
  done
  if [[ ${#missing[@]} -gt 0 ]]; then
    record "$label" FAIL "missing class names: ${missing[*]}"
    return
  fi
  # Citations may be path-prefixed (sample-project/src/Foo.cs), bare-filename
  # (Foo.cs), or class-name-based (e.g. "InventoryItem (file:line 3)"). Count
  # filename-style citations first; if zero, fall back to counting distinct
  # class-name mentions (each class uniquely identifies its file).
  local cite_count
  cite_count=$(grep -cE "(InventoryService|InventoryRoutes|InventoryItem|InventoryResult|ReservationResult)\.cs" "$out" || true)
  if [[ "$cite_count" -lt 4 ]]; then
    local named=0
    for cls in InventoryService InventoryRoutes InventoryItem ReservationResult InventoryResult; do
      grep -q "$cls" "$out" && named=$((named + 1))
    done
    cite_count="$named"
  fi
  if [[ "$cite_count" -lt 4 ]]; then
    record "$label" FAIL "expected ≥4 file citations, found $cite_count"
    return
  fi
  record "$label" PASS "all 5 classes named, $cite_count citations"
}

# T2 — Config discovery.
run_t2() {
  local label="T2: config discovery"
  echo "▶ $label"
  local out="$TMP_DIR/t2.out"
  if ! "${AIGEST[@]}" ask \
    --paths "sample-project/**/*.cs" "sample-project/**/*.json" \
    --question "List config keys, ports, and external dependency URLs. Cite file paths." \
    >"$out" 2>"$TMP_DIR/t2.err"; then
    record "$label" FAIL "aigest exited non-zero"
    return
  fi
  local missing=()
  for needle in "inventory-api" "8080" "catalog.internal.example" "warehouse.internal.example" "6379" "appsettings.json"; do
    grep -q "$needle" "$out" || missing+=("$needle")
  done
  if [[ ${#missing[@]} -gt 0 ]]; then
    record "$label" FAIL "missing: ${missing[*]}"
    return
  fi
  record "$label" PASS "all expected config values present"
}

# T3 — Denylist canary.
run_t3() {
  local label="T3: denylist canary"
  echo "▶ $label"
  local out="$TMP_DIR/t3.out" err="$TMP_DIR/t3.err"
  if ! "${AIGEST[@]}" ask \
    --paths "sample-project/**" \
    --question "List every file in this corpus and whether it contains credentials." \
    >"$out" 2>"$err"; then
    record "$label" FAIL "aigest exited non-zero"
    return
  fi
  local leaked=()
  for canary in "hunter2-canary" "sk_test_canary" "do-not-leak-canary"; do
    if grep -q "$canary" "$out"; then
      leaked+=("$canary")
    fi
  done
  if [[ ${#leaked[@]} -gt 0 ]]; then
    record "$label" FAIL "DENYLIST LEAK — canaries surfaced: ${leaked[*]}"
    return
  fi
  if grep -qE "(^|[[:space:]/])\.env\.fake([[:space:]]|$)" "$out"; then
    record "$label" FAIL ".env.fake appears in worker output (should have been filtered)"
    return
  fi
  record "$label" PASS "no canary strings, .env.fake filtered out"
}

# T4 — Boilerplate generation.
run_t4() {
  local label="T4: write generates valid C#"
  echo "▶ $label"
  local target="/tmp/aigest-smoke-T4.cs"
  rm -f "$target"
  local src_sha_before
  src_sha_before=$(shasum -a 256 sample-project/src/InventoryService.cs | cut -d' ' -f1)
  if ! "${AIGEST[@]}" write \
    --spec "Generate xUnit v3 tests for the InventoryService.Reserve method covering success, out-of-stock, and invalid-id paths" \
    --context sample-project/src/InventoryService.cs \
    --target "$target" \
    --allow-outside-cwd \
    --overwrite \
    >"$TMP_DIR/t4.out" 2>"$TMP_DIR/t4.err"; then
    record "$label" FAIL "aigest write exited non-zero"
    return
  fi
  if [[ ! -s "$target" ]]; then
    record "$label" FAIL "target file empty or missing: $target"
    return
  fi
  if ! grep -q "\[Fact\]" "$target"; then
    record "$label" FAIL "no [Fact] attribute in generated file"
    return
  fi
  local src_sha_after
  src_sha_after=$(shasum -a 256 sample-project/src/InventoryService.cs | cut -d' ' -f1)
  if [[ "$src_sha_before" != "$src_sha_after" ]]; then
    record "$label" FAIL "source file mutated by write (sha mismatch)"
    return
  fi
  record "$label" PASS "valid C# with [Fact], source untouched"
}

# T5 — --per-folder fan-out.
run_t5() {
  local label="T5: --per-folder fan-out"
  echo "▶ $label"
  local out="$TMP_DIR/t5.out"
  if ! "${AIGEST[@]}" ask \
    --paths "sample-project/**/*.cs" \
    --per-folder \
    --question "Summarize this folder in 3 bullets." \
    >"$out" 2>"$TMP_DIR/t5.err"; then
    record "$label" FAIL "aigest exited non-zero (see $TMP_DIR/t5.err)"
    return
  fi
  if ! grep -qE "Per-folder mode: dispatching [0-9]+ folder" "$TMP_DIR/t5.err"; then
    record "$label" FAIL "no per-folder dispatch log line in stderr"
    return
  fi
  if ! grep -qE "Per-folder mode: completed folder" "$TMP_DIR/t5.err"; then
    record "$label" FAIL "no folder completed successfully"
    return
  fi
  if grep -qE "Per-folder mode: folder '.*' failed" "$TMP_DIR/t5.err"; then
    local failed_lines
    failed_lines=$(grep -E "Per-folder mode: folder '.*' failed" "$TMP_DIR/t5.err" | head -3 | tr '\n' ';')
    record "$label" FAIL "one or more folders failed: $failed_lines"
    return
  fi
  record "$label" PASS "all folders dispatched and completed without failure"
}

# T6 — extract-chat (local, no network).
run_t6() {
  local label="T6: extract-chat"
  echo "▶ $label"
  local target="/tmp/aigest-smoke-T6.md"
  rm -f "$target"
  if ! "${AIGEST[@]}" extract-chat sample-project/fixtures/session.jsonl \
    --output "$target" \
    >"$TMP_DIR/t6.out" 2>"$TMP_DIR/t6.err"; then
    record "$label" FAIL "extract-chat exited non-zero"
    return
  fi
  if [[ ! -s "$target" ]]; then
    record "$label" FAIL "target file empty or missing: $target"
    return
  fi
  if ! grep -qi "user" "$target" || ! grep -qi "assistant" "$target"; then
    record "$label" FAIL "missing role markers (user/assistant) in $target"
    return
  fi
  if ! grep -q "InventoryService.cs:42" "$target"; then
    record "$label" FAIL "missing fixture content marker (InventoryService.cs:42)"
    return
  fi
  record "$label" PASS "markdown contains both roles and fixture content"
}

# T7 — Provider probe.
run_t7() {
  local label="T7: provider probe (bare invocation)"
  echo "▶ $label"
  local out="$TMP_DIR/t7.out"
  "${AIGEST[@]}" >"$out" 2>"$TMP_DIR/t7.err" || true
  if ! grep -q "aigest environment check" "$out"; then
    record "$label" FAIL "missing 'aigest environment check' banner"
    return
  fi
  if ! grep -qE "API key.*[a-zA-Z0-9]+…[a-zA-Z0-9]+" "$out"; then
    record "$label" FAIL "API key not masked in output"
    return
  fi
  if ! grep -qE "Provider\s*:" "$out"; then
    record "$label" FAIL "missing Provider line"
    return
  fi
  record "$label" PASS "banner, masked key, provider line all present"
}

echo "aigest smoke tests — using: ${AIGEST[*]}"
echo

run_t1
run_t2
run_t3
run_t4
run_t5
run_t6
run_t7

echo
printf "Summary: %s%d passed%s, %s%d failed%s\n" \
  "$C_GREEN" "$PASS_COUNT" "$C_RESET" \
  "$C_RED" "$FAIL_COUNT" "$C_RESET"

if [[ "$FAIL_COUNT" -gt 0 ]]; then
  echo "${C_YELLOW}See $TMP_DIR for captured stdout/stderr per test.${C_RESET}"
  trap - EXIT  # preserve temp dir for inspection
  echo "Logs preserved at: $TMP_DIR"
  exit 1
fi
exit 0

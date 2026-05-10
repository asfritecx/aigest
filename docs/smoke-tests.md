# aigest Smoke Tests

Eight objective tests run against the synthetic `sample-project/` fixture. The first seven are scripted in `run-smoke-test.sh` and exit non-zero on any failure. The eighth is an adoption check — for humans, not the script.

## Before running

- `~/.config/aigest/.env` exists with a working `AIGEST_API_KEY` and reachable `AIGEST_BASE_URL`.
- `aigest --version` prints `aigest 0.0.1-beta+...`.
- `sample-project/.env.fake` exists (denylist canary fixture; created by Task 2A).

Run the full suite:

```bash
./run-smoke-test.sh
```

The script captures each test's stdout to a temp file, applies the pass checks below, and prints a final summary table.

---

## T1 — Bulk reading + citation discipline

```bash
aigest ask \
  --paths "sample-project/src/**/*.cs" \
  --question "List every public class in this corpus and one-line its purpose. Cite file:line for each."
```

**Pass criteria:**
- Response contains all five class names: `InventoryService`, `InventoryRoutes`, `InventoryItem`, `ReservationResult`, `InventoryResult`.
- Response cites at least four `sample-project/src/*.cs` paths.
- Response contains zero file paths outside `sample-project/`.
- No fabricated class names (string match against the five known names is exhaustive).

## T2 — Config discovery

```bash
aigest ask \
  --paths "sample-project/**/*.cs" "sample-project/**/*.json" \
  --question "List config keys, ports, and external dependency URLs. Cite file paths."
```

**Pass criteria:**
- Response includes `inventory-api`, port `8080`, both URLs (`catalog.internal.example`, `warehouse.internal.example`), and `redisPort` `6379`.
- Response cites `sample-project/config/appsettings.json`.
- No fabricated config keys.

## T3 — Denylist canary (NEW)

Verifies `FileFilter` excludes secret-like files from the corpus.

```bash
aigest ask \
  --paths "sample-project/**" \
  --question "List every file in this corpus and whether it contains credentials."
```

**Pass criteria:**
- Response does NOT contain the string `hunter2-canary` (the canary value in `.env.fake`).
- Response does NOT contain `sk_test_canary` or `do-not-leak-canary`.
- Response does NOT list `sample-project/.env.fake` as an included file (it should be filtered out before being sent to the worker).

This test fails closed: any leak of canary strings = denylist regression.

## T4 — Boilerplate generation

```bash
aigest write \
  --spec "Generate xUnit v3 tests for the InventoryService.Reserve method covering success, out-of-stock, and invalid-id paths" \
  --context sample-project/src/InventoryService.cs \
  --target /tmp/aigest-smoke-T4.cs
```

**Pass criteria:**
- `/tmp/aigest-smoke-T4.cs` exists and is non-empty.
- File contains at least one `[Fact]` attribute and balanced braces.
- `sample-project/src/InventoryService.cs` is byte-identical before and after (compare SHA before/after).

## T5 — `--per-folder` fan-out

```bash
aigest ask \
  --paths "sample-project/**/*.cs" \
  --per-folder \
  --question "Summarize this folder in 3 bullets."
```

**Pass criteria:**
- Stdout contains at least two distinct folder briefings (`sample-project/src/` and `sample-project/tests/`).
- Stderr logs show parallel chat calls (look for per-folder request log lines).

## T6 — `extract-chat` (NEW)

```bash
aigest extract-chat sample-project/fixtures/session.jsonl \
  --output /tmp/aigest-smoke-T6.md
```

**Pass criteria:**
- `/tmp/aigest-smoke-T6.md` exists.
- Contains both a user and an assistant section (script greps for the role headings the command emits).
- Contains the substring `InventoryService.cs:42` (from the fixture) — i.e., content was preserved verbatim.

## T7 — Provider probe

```bash
aigest
```

**Pass criteria:**
- Stdout contains `aigest environment check`.
- Provider line matches `$AIGEST_PROVIDER` (or `cloud` if unset).
- API key line shows a masked key (e.g., `sk-…d0e5`), not the raw value.
- Reachability line shows `Reachable : yes` (or `Ready` line at the end).

---

## T8 — Boundary discipline (Adoption notes, not automated)

This is a check on the *user's primary AI* — not a check on aigest itself. Run when onboarding a new agent into a project that has aigest installed.

Ask your primary AI:

> Debug this intermittent authentication issue and determine whether token validation is safe.

**Pass criteria (judged by human):**
- Primary AI does not delegate the security decision to aigest.
- aigest may be used to summarise supporting files but final reasoning stays with the primary.
- No final security claim made on the basis of aigest output alone.

This sits in the adoption section because the answer depends on the primary AI's behaviour, not aigest's.

---

## Result log

| Date | Test run | Result | Notes |
|---|---|---|---|
| | | | |

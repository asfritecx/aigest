# Workflow Patterns

Use these prompts when the main `SKILL.md` pattern is too small. Keep every call bounded by quoted file paths or globs, and ask the worker to cite line ranges for every retained claim.

## Extensive Codebase Briefing

Use this as the default entry point for broad orientation. It gives cheaper models an explicit structure without requiring the primary agent to read the whole corpus.

```bash
aigest ask \
  --paths "src/**/*.cs" "tests/**/*.cs" "README.md" "docs/*.md" "*.sln" "*.csproj" \
  --question "Produce an implementation-agent codebase briefing from only the provided files. Return concise sections for: 1. Purpose and runtime shape. 2. Main entrypoints and command/API surface. 3. Important modules and responsibilities. 4. Data flow and external calls. 5. Configuration, environment variables, endpoints, ports, and provider assumptions. 6. Build/test commands and test seams. 7. Safety, security, and file-handling guardrails. 8. Likely change areas for future work. 9. Risks, unknowns, and additional files needed. Cite file paths and line ranges for every factual claim. Separate confirmed facts from assumptions. Say Not found in provided files for absent categories. Do not reproduce whole files."
```

## Change Planning

```bash
aigest ask \
  --paths "src/**/*.cs" "tests/**/*.cs" "README.md" "docs/*.md" \
  --question "Plan this change from only the provided files: <describe change>. Identify affected modules, entrypoints, data flow, tests/fixtures, docs/config, edge cases, risks, and unknowns. Return confirmed facts with file paths and line ranges, then assumptions. Say Not found in provided files when evidence is absent."
```

## Config, Endpoints, and Dependencies

```bash
aigest ask \
  --paths "src/**/*.cs" "appsettings*.json" "*.csproj" "README.md" ".env.example" \
  --question "Inventory configuration keys, environment variables, endpoints/routes, ports, auth-related settings, external services, package dependencies, and where each is referenced. Cite file paths and line ranges. Use only provided files. Do not infer secret values."
```

## Test Discovery

```bash
aigest ask \
  --paths "src/**/*.cs" "tests/**/*.cs" "*.csproj" "README.md" \
  --question "Map the test surface for <describe target>. List frameworks, test commands, fake clients/mocks, fixtures, existing coverage, untested edge cases, and files likely needing updates. Cite file paths and line ranges. Say Not found in provided files when absent."
```

## Documentation Drafts

```bash
aigest ask \
  --paths README.md "docs/*.md" "src/**/*.cs" \
  --question "Suggest exact documentation updates based only on the provided files. Identify stale or missing setup, commands, configuration, examples, and warnings. Cite the source line ranges behind every suggestion. Do not rewrite unrelated sections."
```

## Per-folder Parallelism (`--per-folder`)

Default to in-process fan-out when one glob spans multiple subfolders. The CLI groups matched files by their first segment under the common base (e.g. `src/Commands`, `src/Core`) and dispatches one chat call per folder.

```bash
aigest ask \
  --paths "src/**/*.cs" --per-folder \
  --question "For each folder, return: purpose, public surface, internal modules, dependencies (in/out), tests, risks, and likely change areas. Stay within the declared <scope>. Cite file paths and line ranges. Say Not found in provided files when evidence is absent. List cross-folder dependencies under Additional files needed instead of speculating."
```

How the run is shaped:

- Each folder's prompt receives a `<scope folder='...'>` block listing its in-scope files plus a `<tree>` of the folder. The system prompt instructs the model to stay within scope and surface cross-folder needs under "Additional files needed".
- Streaming is buffered per folder (interleaved streams are unreadable). Output is printed in original folder order, each section prefixed `## Folder: <path>`.
- Concurrency is bounded by `AIGEST_MAX_PARALLEL_FOLDERS` (default `4`). Lower it when the provider rate-limits aggressively; raise it (with a matching account quota) for monorepos with many folders.
- One folder's failure is reported inline as `_Failed: <message>_` and does not abort the rest. CLI exit code is `1` if any folder failed, `0` otherwise.
- A single-folder match falls back to the normal single-call path automatically — `--per-folder` on a narrow glob is not an error and not slower.

Verify by reading the cited line ranges in the highest-impact folders before acting on conclusions.

## Cross-process Fan-out (Multi-agent)

Use `--per-folder` first. Reach for multi-agent fan-out only when:

- Path sets are not folder-shaped — e.g. analyzing `Auth`-related code spread across `src/Web`, `src/Workers`, and `tests/`.
- You want each agent to ask a *different* question, not the same question per folder.
- You need separate cancellation, retry, or quota control per call.

Spawn agents in one message, each with a non-overlapping path set and one focused `aigest ask` call:

```text
Agent 1: aigest ask --paths "src/Auth/**/*.cs" --question "..."
Agent 2: aigest ask --paths "src/Billing/**/*.cs" --question "..."
Agent 3: aigest ask --paths "tests/**/*.cs" --question "..."
```

Merge by reading the returned citations locally, or with a final bounded call over saved notes:

```bash
aigest ask \
  --paths agent1-output.md agent2-output.md agent3-output.md \
  --question "Merge these findings. Remove duplicates, mark conflicts, and retain only claims with cited file paths and line ranges."
```

## Chat Log Extraction

```bash
aigest extract-chat session.jsonl --output session.md
aigest ask \
  --paths session.md \
  --question "Extract decisions, requirements, file paths, commands, blockers, action items, and unresolved follow-ups. Cite transcript line numbers. Say Not found in provided files for absent categories."
```

## Generated Test Skeletons

```bash
aigest write \
  --spec "Generate xUnit test skeletons for the provided code. Cover success, missing input, boundary values, error paths, and documented behavior. Keep assertions reviewable. Mark assumptions as TODO comments rather than inventing behavior." \
  --context src/Service.cs \
  --target tests/ServiceTests.generated.cs
```

## Review Checklist

- Require citations and distinguish confirmed facts from assumptions.
- Ask for relevant sections, not full-file reproductions.
- Ask for additional files only when blocked.
- Verify the highest-impact cited line ranges locally before editing.
- Treat denied, oversized, unsupported, or silently skipped files as a path-scope issue before assuming provider failure.

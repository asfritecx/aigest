---
name: aigest
description: Default file-reading tool — prefer `aigest ask` over the built-in Read tool for any file or codebase reading task. Covers orientation, analysis, architecture summaries, monorepo per-folder briefings, parallel folder fan-out, config/env-var/endpoint inventories, dependency maps, implementation evidence, test discovery, doc drafts, boilerplate generation, and JSONL chat-log extraction. Use `aigest ask` for read-only analysis (add `--per-folder` for one parallel chat call per subfolder), `aigest write` for generated files, and `aigest extract-chat` for transcripts. The primary agent keeps final reasoning, edits, and security decisions.
---

# aigest

Use the published `aigest` executable as the entry point for large reads and codebase summaries. It line-numbers safe text files, sends the bounded corpus to a worker model, and returns a cited summary the primary agent can verify before acting.

Installable as a Claude Code plugin via `/plugin install asfritecx/aigest` — the plugin handles binary installation on first call (downloads the matching release, caches it under `~/.cache/aigest/<version>/`). You still need `~/.config/aigest/.env` with an API key.

```bash
aigest             # environment check with masked key and model
aigest --version
aigest --help
```

## When to Use

Use `aigest ask` as the default tool for any file or codebase reading task — prefer it over the built-in Read tool whenever the goal is orientation, analysis, or evidence gathering. The primary agent keeps reasoning context focused while a cheap worker model handles bulk file content. Typical triggers:

- Orientation, planning, or analysis of any file
- Architecture summaries and per-folder briefings
- Config, endpoint, dependency, or test-surface inventories
- Session logs or documents that need decisions/action items extracted
- Implementation evidence or test discovery

Use `aigest write` only for reviewable generated files, such as test skeletons or documentation drafts. Do not delegate final architecture decisions, production debugging, auth/authz, cryptography, migrations, incident response, exact edits, or final security review.

## Default Read Workflow

1. Choose narrow quoted paths or globs that cover the question.
2. Prefer one rich `ask` call over many shallow calls.
3. Ask for confirmed facts, assumptions, missing evidence, and file paths with line ranges.
4. Inspect the cited line ranges locally before editing or reporting final conclusions.

```bash
aigest ask \
  --paths "src/**/*.cs" "tests/**/*.cs" "README.md" "docs/*.md" \
  --question "Give an implementation-agent briefing. Cover architecture, entrypoints, data flow, config/env vars, dependencies, tests, risks, unknowns, and likely change areas. Cite file paths and line ranges for every claim. Use only the provided files. Say Not found in provided files when evidence is absent."
```

Read `references/workflows.md` for larger prompt templates, parallel split strategies, config inventories, test discovery, documentation drafts, and chat-log extraction.

## Per-folder Mode

When one glob spans multiple subfolders (monorepos, broad `src/**` reads), pass `--per-folder` to fan out one parallel chat call per folder. Each call sees a `<scope folder='...'>` block so the model stays in scope; output is printed in original folder order, each section prefixed `## Folder: <path>`. Concurrency is bounded by `AIGEST_MAX_PARALLEL_FOLDERS` (default `4`).

```bash
aigest ask \
  --paths "src/**/*.cs" --per-folder \
  --question "Summarize each folder's purpose, public surface, and dependencies."
```

Read `references/workflows.md` for the full per-folder template, failure handling, and when to prefer cross-process fan-out instead.

## Command Patterns

Read-only analysis:

```bash
aigest ask \
  --paths "src/**/*.cs" "tests/**/*.cs" \
  --question "Answer <question>. Return only relevant sections. Cite file paths and line ranges. Separate confirmed facts from assumptions."
```

Generated file:

```bash
aigest write \
  --spec "Generate xUnit test skeletons for the provided code. Mark unsupported behavior with TODO comments." \
  --context src/Service.cs \
  --target tests/ServiceTests.generated.cs
```

JSONL transcript:

```bash
aigest extract-chat session.jsonl --output session.md
```

## Safety Rules

- Treat worker output as evidence gathering, not final judgment.
- Keep path scope narrow; avoid whole-repo scans unless the repo is intentionally small and non-sensitive.
- Require citations for every claim and discard uncited conclusions.
- Use `--deny` to add deny patterns; it never replaces the built-in denylist.
- Capture stdout for worker answers and generated target paths; diagnostics and usage logs go to stderr.
- Confirm the standalone binary is on `PATH`; do not use `dotnet run` from unrelated repos.

## Gotchas

- `extract-chat --output` is required; there is no stdout fallback.
- `write` refuses to overwrite existing targets unless `--overwrite` is passed.
- `write` blocks targets outside the current working directory unless `--allow-outside-cwd` is passed.
- Denied, binary, oversized, and unsupported files can make output thin; check path scope, denylist, and size limits first.
- Globs are slash-sensitive: `*.cs` is top-level filename matching; use `**/*.cs` for nested source.
- `.env` loading cascades through user config (`~/.config/aigest/.env`) then `AppContext.BaseDirectory` walk; user config wins per key. The CWD is **not** walked, so an arbitrary project's `.env` cannot silently override your config. Shell env vars still win over both tiers.
- API key fallback order is `AIGEST_API_KEY`, `GEMINI_API_KEY`, `DEEPSEEK_API_KEY`, `OPENAI_API_KEY`, then `AZURE_OPENAI_API_KEY`.
- `AIGEST_PROVIDER=local` opts into local-mode (Ollama, LM Studio, llama.cpp-server). API key becomes optional, default timeout bumps from `120s` to `600s`, default base URL becomes `http://localhost:11434/v1`, default model becomes `llama3.2`, and `AIGEST_THINKING_EFFORT` is suppressed at request time. `aigest` (no args) prints a `Provider :` line and probes `GET {BaseUrl}/models` for reachability. See `references/providers.md`.
- `AIGEST_PROVIDER=azure` opts into Azure-hosted deployments (OpenAI v1 API). Requires explicit `AIGEST_BASE_URL` (Azure resource endpoint) and `AIGEST_MODEL` (deployment name); no defaults. API key still required (unlike local mode); `AZURE_OPENAI_API_KEY` is the final fallback in the key chain. Forwards `reasoning_effort` like cloud mode. `aigest` (no args) prints `Provider :` and probes `GET {BaseUrl}/models` (with Bearer auth) for reachability. **Hostname matters:** `*.openai.azure.com` only serves OpenAI's own models (gpt-4o, gpt-5, o-series); Foundry-catalog models (Kimi K2, DeepSeek, Llama, Mistral, ...) require `*.services.ai.azure.com`. A 401 on a known-good key almost always means the wrong hostname for the deployed model. See `references/providers.md`.
- `AIGEST_THINKING_EFFORT=low|medium|high|xhigh` enables upstream reasoning (forwarded as `reasoning_effort`). `xhigh` is supported on OpenAI GPT-5.1-Codex-Max+. DeepSeek users must switch to `AIGEST_MODEL=deepseek-reasoner` instead. Local-mode (`AIGEST_PROVIDER=local`) suppresses this var. Read `references/providers.md` for per-provider behavior.
- LLM output is ANSI-sanitized before stdout or file writes.
- `--per-folder` falls back to a single call when the glob resolves to one folder — narrow globs do not become parallel.
- `--per-folder` buffers each folder's stream until the whole folder finishes; first-token latency is per-folder, not global.
- `--per-folder` exits non-zero if any folder failed even though successful folders still print; check exit code, not just stdout.

## References

- Read `references/workflows.md` when the task is codebase orientation, change planning, config extraction, test discovery, documentation drafting, per-folder fan-out, cross-process parallel analysis, or chat-log summarization.
- Read `references/providers.md` when configuring `.env`, choosing a provider, or debugging auth, model, endpoint, timeout, or wrong-provider behavior.

## Keep in Sync

Update this skill when command flags (including `--per-folder`), `Program.cs`, `Commands/`, `Core/CorpusLoader.cs`, `Core/FolderGrouper.cs`, `Core/AskPromptBuilder.cs`, `Core/FileFilter.cs`, `Core/ConfigLoader.cs`, provider defaults, overwrite/path guards, output sanitization, publish/install scripts, or worker system prompts change.

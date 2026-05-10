# Aigest CLI

A .NET 10 CLI that delegates bulk codebase reads to a cheap, large-context worker model and returns concise, citation-anchored summaries to your primary AI (Claude Code, Cursor, etc.) over an OpenAI-compatible API.

## Why aigest

Top-tier coding agents are expensive to run and easy to derail when pointed at large corpora. Three concrete problems motivated this tool:

- **Token cost on bulk reads.** Asking a top-tier reasoning model to read 30+ files burns its budget on file ingestion before any reasoning starts. Aigest pushes that work to a cheaper worker model (DeepSeek, Gemini Flash, an Azure-hosted Kimi/Llama, or a local Ollama model) running on its own quota.
- **Primary AI loses focus on large corpora.** Top-tier models degrade when forced to chew through 40 files of mixed C#, JSON, and YAML to answer one question. Aigest returns a single scoped, evidence-anchored response — preserving the primary's context budget for the decisions only it can make.
- **Built-in agent file tools sample rather than read.** Claude Code's Explore agent and similar tools truncate, page, or read excerpts and can miss content past their read window. A worker with a million-token context can digest the entire corpus, cite `file:line` for every claim, and hand back a small, verifiable answer.

The primary AI keeps responsibility for reasoning, verification, security decisions, and final edits. The worker reads scoped local files (enforced denylist + per-file and total size caps) and returns evidence — it never applies speculative output to your codebase. An empirical comparison against Claude Code's `Explore` agent on a real inventory task lives in [docs/comparison-aigest-vs-explore.md](docs/comparison-aigest-vs-explore.md) (100% capture in ~24s vs. estimated 60–90s, single run, no statistical N).

See [asfritecx/aigest-demo](https://github.com/asfritecx/aigest-demo) for a small Python weather-CLI generated end-to-end by `aigest write` from a single design spec.

## Project Layout

```text
ai-worker-bash/
├─ src/Aigest.Cli/              # .NET CLI implementation
├─ tests/Aigest.Cli.Tests/      # xUnit v3 unit and command tests
├─ rules/                         # routing rules for agent tools
├─ docs/                          # manual testing guidance
├─ examples/                      # sample input artifacts
├─ sample-project/                # safe synthetic files for testing
├─ skills/aigest/                 # user-facing Agent Skill (bulk reads, boilerplate)
├─ skills/aigest-dev/             # dev-facing Agent Skill (modifying CLI source)
├─ skills/owasp-gate/             # OWASP LLM Top 10 pre-merge audit skill
├─ artifacts/                     # self-contained binaries output by publish.sh (gitignored)
├─ aigest.slnx
├─ Directory.Build.props
├─ Directory.Build.targets
├─ global.json
├─ AGENTS.md
├─ CLAUDE.md
├─ LICENSE
├─ .env.example
├─ setup.sh
├─ run-smoke-test.sh
└─ publish.sh
```

## Getting Started

### Option 1: Claude Code plugin (recommended)

If you use Claude Code, install the plugin and it will fetch the binary on first use:

```
/plugin install asfritecx/aigest
```

This auto-installs the `aigest` binary on first invocation (downloads the matching release from GitHub, caches it under `~/.cache/aigest/<version>/`), adds it to PATH while the plugin is enabled, and registers the `aigest` skill so Claude knows when to delegate bulk reads. You still need to create `~/.config/aigest/.env` with your API key — see [Configuration](#configuration) below.

### Option 2: Pre-built Binary (No .NET SDK required)

1. **Download the correct binary** for your platform from the [GitHub Releases](https://github.com/asfritecx/ai-worker-bash/releases) page:

   | Platform | Binary |
   |----------|--------|
   | macOS Apple Silicon | `aigest-osx-arm64` |
   | macOS Intel | `aigest-osx-x64` |
   | Linux x64 | `aigest-linux-x64` |
   | Linux ARM64 | `aigest-linux-arm64` |
   | Windows x64 | `aigest-win-x64.exe` |
   | Windows ARM64 | `aigest-win-arm64.exe` |

2. **Rename and place it on your PATH.** For macOS/Linux:
   ```bash
   mv aigest-osx-arm64 aigest
   chmod +x aigest
   mkdir -p ~/.local/bin
   mv aigest ~/.local/bin/
   export PATH="$HOME/.local/bin:$PATH"
   ```

3. **Create a `.env` file** with your API key and model. The installed binary reads from two locations; create whichever fits:

   - `~/.config/aigest/.env` — used by the installed binary from **any directory** (recommended for daily use). On Windows: `%APPDATA%\aigest\.env`.
   - `.env` in the project root — used when running `dotnet run` or developing inside this repo.

   ```bash
   mkdir -p ~/.config/aigest
   cp .env.example ~/.config/aigest/.env
   nano ~/.config/aigest/.env
   ```

   Minimum required values:
   ```env
   AIGEST_API_KEY=your_api_key_here
   AIGEST_BASE_URL=https://api.deepseek.com
   AIGEST_MODEL=deepseek-v4-flash
   ```

4. **Run it:**
   ```bash
   aigest              # env/config check: prints tier, masked key, base URL, model
   aigest --version    # prints build version (0.0.1-beta)
   aigest --help       # lists all subcommands
   aigest ask --paths README.md --question "Summarize this repo."
   ```

### Option 3: Build from Source (Requires .NET 10 SDK)

1. **Install the .NET 10 SDK.** Verify with:
   ```bash
   dotnet --version
   ```

2. **Clone the repo and run setup:**
   ```bash
   ./setup.sh
   ```
   This restores packages, builds the solution, creates **both** `.env` (repo root) and `~/.config/aigest/.env` from `.env.example`, publishes the standalone `aigest` binary to `~/.local/bin`, and edits your shell profile to add `~/.local/bin` to `PATH` if needed. If `dotnet` is not installed, it skips the build and prompts you to download a pre-built binary instead.

3. **Ensure `~/.local/bin` is on your PATH.** `setup.sh` adds it automatically, but if you skipped setup or it could not detect your shell, add this manually:
   ```bash
   export PATH="$HOME/.local/bin:$PATH"
   ```

4. **Configure `.env`:**
   ```bash
   nano ~/.config/aigest/.env
   ```

5. **Run the smoke test:**
   ```bash
   ./run-smoke-test.sh
   ```

## Configuration

### `.env` Discovery (cascade)

The CLI loads all available tiers and keeps the first value seen for each key:

1. **User config:** `~/.config/aigest/.env` on Linux/macOS, `%APPDATA%\aigest\.env` on Windows. This is the canonical source.
2. Walk up from **`AppContext.BaseDirectory`** to the first `.git` boundary — only relevant for `dotnet run` inside this repo, so the kit's own `.env` is found.

The current working directory is intentionally **not** walked: an arbitrary project's `.env` (e.g., a Node or Django app you're inside of) must not silently override your user config. To override config for a single invocation, set `AIGEST_*` shell environment variables — those win over both tiers because `LoadEnvFile` skips already-set vars.

### Environment Variables

Required:

```env
AIGEST_API_KEY=your_api_key_here
AIGEST_BASE_URL=https://api.deepseek.com
AIGEST_MODEL=deepseek-v4-flash
```

Optional (shown with defaults):

```env
AIGEST_TIMEOUT_SECONDS=120
AIGEST_MAX_FILE_BYTES=800000    # per-file size limit (800 KB)
AIGEST_MAX_TOTAL_BYTES=4000000  # corpus total limit (4 MB)
AIGEST_DEBUG=0                  # set to 1 only to debug CLI internals; logs full prompts/source
AIGEST_THINKING_EFFORT=         # optional: low | medium | high | xhigh (forwarded as reasoning_effort)
AIGEST_MAX_PARALLEL_FOLDERS=4   # cap on concurrent --per-folder chat calls
AIGEST_PROVIDER=                # optional: local | cloud (default) | azure
```

### API Key Fallback

When `AIGEST_API_KEY` is unset, the CLI tries these in order:

1. `AIGEST_API_KEY`
2. `GEMINI_API_KEY`
3. `DEEPSEEK_API_KEY`
4. `OPENAI_API_KEY`
5. `AZURE_OPENAI_API_KEY`

A stale provider-specific key can silently route to the wrong provider. If model or endpoint behavior does not match expectations, unset any leftover provider keys and set `AIGEST_API_KEY` explicitly.

### Provider Examples

**DeepSeek** (default):
```env
AIGEST_API_KEY=your_deepseek_key
AIGEST_BASE_URL=https://api.deepseek.com
AIGEST_MODEL=deepseek-v4-flash
```

**Gemini** (OpenAI-compatible endpoint):
```env
AIGEST_API_KEY=your_gemini_key
AIGEST_BASE_URL=https://generativelanguage.googleapis.com/v1beta/openai/
AIGEST_MODEL=gemini-2.5-flash
```

**OpenAI:**
```env
AIGEST_API_KEY=your_openai_key
AIGEST_BASE_URL=https://api.openai.com/v1
AIGEST_MODEL=gpt-4o-mini
```

**Azure AI Foundry / Azure OpenAI Service** (OpenAI v1, OpenAI-compatible):
```env
AIGEST_PROVIDER=azure
AIGEST_API_KEY=<azure-resource-key>      # or set AZURE_OPENAI_API_KEY
AIGEST_BASE_URL=https://<resource>.openai.azure.com/openai/v1/        # Azure OpenAI Service: gpt-4o, gpt-5, o-series
# AIGEST_BASE_URL=https://<resource>.services.ai.azure.com/openai/v1/  # Azure AI Foundry: Kimi K2, DeepSeek, Llama, Mistral, ...
AIGEST_MODEL=<deployment-name>
```
Replace `<resource>` with your Azure resource name and `<deployment-name>` with your deployment name. Pick the hostname that matches your model's catalog: `*.openai.azure.com` hosts only OpenAI's own models, while `*.services.ai.azure.com` hosts the broader Foundry catalog. Both expose the same `/openai/v1/` path and accept the same API key — using the wrong hostname surfaces as `Reachable : no (HTTP 401)` from the bare-invocation probe even when the key is valid.

### Local LLMs (Ollama / LM Studio)

Set `AIGEST_PROVIDER=local` for OpenAI-compatible local servers. In local mode the API key requirement is dropped, the default timeout becomes `600s` (CPU inference is slower), and `AIGEST_THINKING_EFFORT` is suppressed (most local models reject `reasoning_effort`). `aigest` (no args) probes the endpoint and reports reachability.

**Ollama** (default port `11434`):
```env
AIGEST_PROVIDER=local
AIGEST_BASE_URL=http://localhost:11434/v1
AIGEST_MODEL=llama3.2
```

**LM Studio** (default port `1234`):
```env
AIGEST_PROVIDER=local
AIGEST_BASE_URL=http://localhost:1234/v1
AIGEST_MODEL=local-model
```

Quick start with Ollama:
```bash
ollama serve &
ollama pull llama3.2
AIGEST_PROVIDER=local aigest                                  # check + reachability probe
AIGEST_PROVIDER=local aigest ask --paths README.md --question "summarize"
```

## Development Commands

```bash
dotnet build
dotnet test
dotnet test --filter <ClassName>    # run a single test class
```

Run locally without publishing:

```bash
dotnet run --project src/Aigest.Cli -- ask \
  --paths README.md \
  --question "Summarize this repo in 5 bullets."
```

Diagnostic output and usage stats go to **stderr**; model answers go to **stdout**. This means you can pipe cleanly:

```bash
dotnet run --project src/Aigest.Cli -- ask --paths README.md --question "..." > answer.md
```

Run the smoke test:

```bash
./run-smoke-test.sh
```

## CLI Usage

### Env / config check (bare invocation)

Running `aigest` with no subcommand prints the config tier, masked API key, base URL, model, and a ready/fail status. Use this to verify your `.env` before any real call.

```bash
aigest
```

### `ask` — read files and answer a question

```bash
aigest ask \
  --paths "src/**/*.cs" "appsettings*.json" \
  --question "List config keys, endpoints, ports, and external dependencies. Return relevant sections with file paths and line ranges. Do not guess."
```

Options:

| Flag | Default | Description |
|------|---------|-------------|
| `--paths` | required | File/glob paths to include in the corpus. Use `**/*.cs` for nested source; `*.cs` matches top-level only. |
| `--question` | required | Question to ask about the corpus. |
| `--max-tokens` | `8192` | Maximum output tokens. |
| `--per-folder` | `false` | Group matched files by their immediate subfolder under the common base, then dispatch one chat call per folder in parallel (bounded by `AIGEST_MAX_PARALLEL_FOLDERS`, default `4`). Output is buffered per folder and printed in original folder order with `## Folder: <path>` headers. Falls back to a single call when all matches resolve to one folder. |
| `--deny` | — | Additional glob deny patterns (appended to the built-in denylist). |

Output streams to stdout as chunks arrive (single-call mode); `--per-folder` buffers each folder until that folder finishes, then flushes in order.

### `write` — generate a file from a spec

```bash
aigest write \
  --spec "Generate xUnit tests for this service. Use Arrange, Act, Assert comments." \
  --context src/MyService.cs \
  --target tests/MyServiceTests.generated.cs
```

Options:

| Flag | Default | Description |
|------|---------|-------------|
| `--spec` | required | Specification for the file to generate. |
| `--context` | required | Context file/glob paths the worker reads. |
| `--target` | required | Output file path. |
| `--max-tokens` | `16384` | Maximum output tokens. |
| `--overwrite` | `false` | Required to overwrite an existing target file. Without it, `write` exits with an error if the target already exists. |
| `--allow-outside-cwd` | `false` | Required to write a target path outside the current working directory. Without it, `write` exits with an error. |
| `--deny` | — | Additional glob deny patterns (appended to the built-in denylist). |

The full response is accumulated in memory, ANSI-sanitized, then written to the target file only after the stream completes.

### `extract-chat` — extract text from a JSONL chat log

```bash
aigest extract-chat session.jsonl --output tmp/chat.txt
```

`--output` / `-o` is **required** — there is no stdout fallback.

## Streaming and Usage

- `ask` streams chunks to stdout as they arrive, flushing after each chunk.
- `write` accumulates the full streamed response, ANSI-strips it, and writes the target file only after completion.
- Token usage and cache hit/miss counts (DeepSeek-style and OpenAI cached-token details) are emitted as structured log events to **stderr**.

## Publishing

Publish binaries for all supported runtime identifiers:

```bash
./publish.sh
```

Publish only the current platform:

```bash
dotnet publish src/Aigest.Cli \
  -c Release \
  -f net10.0 \
  -r osx-arm64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o artifacts/osx-arm64
```

## Agent Skills

This repo ships two Agent Skills under `skills/`.

### `skills/aigest/` — User-facing

Teaches an agent to use the published `aigest` binary for bulk reads and bounded boilerplate. Use this skill when a task involves files **over 250 lines**, three or more files, config/env-var/endpoint/dependency inventories, test discovery, documentation drafts, boilerplate generation, or JSONL chat-log extraction.

Provider setup notes live at `skills/aigest/references/providers.md`; reusable prompt patterns at `skills/aigest/references/workflows.md`.

Example prompt:

```text
Use the aigest skill to summarize src/**/*.cs and list config keys. Do not guess.
```

### `skills/aigest-dev/` — Dev-facing

For modifying, extending, or debugging the CLI source under `src/Aigest.Cli/`. Covers how to add a new command (System.CommandLine 2.0 patterns), wire DI in `CliHost`, use `[LoggerMessage]` source-gen in `CliLog`, write command-level tests with `FakeChatClient`, and key architecture gotchas: Polly resilience, dual `OpenAiChatClient` constructors, the streaming chat-client contract, `ResponseCapturePolicy` cache-stat tee, and the `CheckCommand` DI bypass.

### `skills/owasp-gate/` — Security review

Pre-merge security audit for LLM integration code, prompt construction, file ingestion, API client code, or any AI-adjacent feature. Runs `/security-review` first, then audits the codebase against the five highest-risk OWASP LLM Top 10 (2025) categories: prompt injection, sensitive disclosure, improper output handling, excessive agency, and unbounded consumption.

## Security Guardrails

The CLI enforces two size limits set in `.env`:

- **Per-file:** `AIGEST_MAX_FILE_BYTES` (default 800 KB) — files over this limit are skipped.
- **Corpus total:** `AIGEST_MAX_TOTAL_BYTES` (default 4 MB) — loading stops once the total reaches this limit.

The built-in denylist blocks common risky files: `.env`, secret-like filenames, production appsettings files, private keys, archives, database dumps, binary files, images, and office documents. Use `--deny "<glob>"` to append additional patterns; it never replaces the built-in denylist.

LLM output is ANSI-sanitized before it reaches stdout or any target file, so piped output and generated files are clean plain text.

Keep path scope narrow and verify worker output against source before editing.

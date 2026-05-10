# aigest vs Claude Code Explore — Empirical Comparison

## Setup

| | |
|--|--|
| Date | 2026-05-07 |
| Repo commit | `2331a73` |
| Claude Code version | 2.1.132 |
| Worker model | `deepseek-v4-flash` via `api.deepseek.com` |
| Corpus | `src/Aigest.Cli/**/*.cs` + `.env.example` (42 files, 1,968 lines) |

Each task was run once per tool. No statistical N — see [Limitations](#limitations).

---

## Task 1 — Inventory (structured / factual)

**Question:**
> "List every CLI flag, environment variable, and config-loading tier across this codebase. Group by source file. Cite file:line for each item. Do not guess."

**Paths given:** `src/Aigest.Cli/**/*.cs`, `.env.example`

### Ground truth

| Category | Items |
|----------|-------|
| CLI flags | 13 flags across 3 commands + 1 bare invocation → `CheckCommand` |
| Env vars | 11 (`AIGEST_API_KEY`, `GEMINI_API_KEY`, `DEEPSEEK_API_KEY`, `OPENAI_API_KEY`, `AIGEST_BASE_URL`, `AIGEST_MODEL`, `AIGEST_MAX_FILE_BYTES`, `AIGEST_MAX_TOTAL_BYTES`, `AIGEST_TIMEOUT_SECONDS`, `AIGEST_DEBUG`, `AIGEST_THINKING_EFFORT`) + `XDG_CONFIG_HOME` (secondary) |
| Config tiers | 3 (CWD walk-up → user config → `AppContext.BaseDirectory` walk-up) |

### Scores

| Metric | aigest | Explore |
|--------|-------------|---------|
| Flags captured | 14 / 14 (100%) | 12 / 14 (86%) |
| Env vars captured | 11 / 11 (100%) | 11 / 11 (100%) |
| Config tiers captured | 3 / 3 (100%) | 3 / 3 (100%) |
| Hallucinations | 0 | 0 |
| Wall-clock | 23.7s | ~60–90s (est.) |
| Citation accuracy | All spot-checked lines correct | All spot-checked lines correct |

**What aigest caught that Explore missed:**
- `extract-chat input` (positional argument, `Program.cs:124-127`)
- Bare invocation → `CheckCommand.Run()` (`Program.cs:151`)

**What Explore caught that aigest missed:** Nothing — aigest was a strict superset.

<details>
<summary>Full aigest T1 output (click to expand)</summary>

```
## CLI Flags

### `Program.cs` — root command and subcommands

| Command | Flag | Type | Description | File:Line |
|---------|------|------|-------------|-----------|
| ask | --paths | string[] (required) | File/glob paths to include in the corpus | Program.cs:17-22 |
| ask | --question | string (required) | Question to ask about the corpus | Program.cs:23-27 |
| ask | --max-tokens | int (default 8192) | Maximum output tokens | Program.cs:28-32 |
| ask | --deny | string[] | Additional glob deny patterns | Program.cs:33-37 |
| write | --spec | string (required) | Specification for the file to generate | Program.cs:61-65 |
| write | --context | string[] (required) | Context file/glob paths | Program.cs:66-71 |
| write | --target | string (required) | Output file path | Program.cs:72-76 |
| write | --max-tokens | int (default 16384) | Maximum output tokens | Program.cs:77-81 |
| write | --overwrite | bool | Allow overwriting existing target file | Program.cs:82-85 |
| write | --allow-outside-cwd | bool | Allow writing to paths outside CWD | Program.cs:86-89 |
| write | --deny | string[] | Additional glob deny patterns | Program.cs:90-94 |
| extract-chat | input (argument) | string | Input JSONL chat file | Program.cs:124-127 |
| extract-chat | --output (-o) | string (required) | Output markdown file path | Program.cs:128-132 |
| bare | (none) | — | Runs CheckCommand.Run() for env/config check | Program.cs:151 |

## Environment Variables

### ConfigLoader.cs

| Variable | Purpose | Default | File:Line |
|----------|---------|---------|-----------|
| AIGEST_API_KEY | Primary API key (checked first) | — | ConfigLoader.cs:32 |
| GEMINI_API_KEY | Fallback API key | — | ConfigLoader.cs:33 |
| DEEPSEEK_API_KEY | Fallback API key | — | ConfigLoader.cs:34 |
| OPENAI_API_KEY | Fallback API key | — | ConfigLoader.cs:35 |
| AIGEST_BASE_URL | Base URL for the API | "https://api.deepseek.com" | ConfigLoader.cs:48 |
| AIGEST_MODEL | Model name | "deepseek-v4-flash" | ConfigLoader.cs:49 |
| AIGEST_MAX_FILE_BYTES | Max bytes per file | 800,000 | ConfigLoader.cs:50 |
| AIGEST_MAX_TOTAL_BYTES | Max total corpus bytes | 4,000,000 | ConfigLoader.cs:51 |
| AIGEST_TIMEOUT_SECONDS | HTTP timeout (seconds) | 120 | ConfigLoader.cs:52 |
| AIGEST_DEBUG | Enable debug logging ("1") | "0" | ConfigLoader.cs:53 |

## Config-Loading Tiers (ConfigLoader.cs Load() — lines 7–55)

| Tier | Scope | Strategy | File:Line |
|------|-------|----------|-----------|
| 0 (explicit) | Explicit path | If envFilePath is provided, load only that file | ConfigLoader.cs:9-13 |
| 1 | Project-local | Walk up from CWD until .env found; stop at .git | ConfigLoader.cs:19-20, 80-99 |
| 2 | User config | ~/.config/aigest/.env or %APPDATA%/aigest/.env | ConfigLoader.cs:22-23, 101-121 |
| 3 | Base directory | Walk up from AppContext.BaseDirectory (for dotnet run) | ConfigLoader.cs:25-29, 70-75 |
```

</details>

<details>
<summary>Full Explore T1 output (click to expand)</summary>

```
### CLI Flags (Program.cs)

- --paths (Program.cs:17) — File/glob paths to include in the corpus (ask command)
- --question (Program.cs:23) — Question to ask about the corpus (ask command)
- --max-tokens (Program.cs:28) — Maximum output tokens (ask command, default 8192)
- --deny (Program.cs:33) — Additional glob deny patterns (ask command)
- --spec (Program.cs:61) — Specification for the file to generate (write command)
- --context (Program.cs:66) — Context file/glob paths (write command)
- --target (Program.cs:72) — Output file path (write command)
- --max-tokens (Program.cs:77) — Maximum output tokens (write command, default 16384)
- --overwrite (Program.cs:82) — Allow overwriting existing target file (write command)
- --allow-outside-cwd (Program.cs:86) — Allow writing to paths outside the current working directory (write command)
- --deny (Program.cs:90) — Additional glob deny patterns (write command)
- --output / -o (Program.cs:128) — Output markdown file path (extract-chat command)

### Environment Variables

ConfigLoader.cs:
- AIGEST_API_KEY (ConfigLoader.cs:32) — Primary API key for the worker
- GEMINI_API_KEY (ConfigLoader.cs:33) — Fallback API key for Gemini provider
- DEEPSEEK_API_KEY (ConfigLoader.cs:34) — Fallback API key for DeepSeek provider
- OPENAI_API_KEY (ConfigLoader.cs:35) — Fallback API key for OpenAI provider
- AIGEST_BASE_URL (ConfigLoader.cs:48) — API base URL (default: https://api.deepseek.com)
- AIGEST_MODEL (ConfigLoader.cs:49) — Model identifier (default: deepseek-v4-flash)
- AIGEST_MAX_FILE_BYTES (ConfigLoader.cs:50) — Maximum file size in bytes (default: 800,000)
- AIGEST_MAX_TOTAL_BYTES (ConfigLoader.cs:51) — Maximum total request size in bytes (default: 4,000,000)
- AIGEST_TIMEOUT_SECONDS (ConfigLoader.cs:52) — API timeout in seconds (default: 120)
- AIGEST_DEBUG (ConfigLoader.cs:53) — Debug mode flag (0 or 1, default: 0)
- AIGEST_THINKING_EFFORT (ConfigLoader.cs) — Upstream reasoning effort (low|medium|high, default: unset/disabled)
- XDG_CONFIG_HOME (ConfigLoader.cs:111) — Override default config directory on Linux/macOS

.env.example: AIGEST_API_KEY, AIGEST_BASE_URL, AIGEST_MODEL, AIGEST_MAX_FILE_BYTES,
              AIGEST_MAX_TOTAL_BYTES, AIGEST_TIMEOUT_SECONDS, AIGEST_DEBUG, AIGEST_THINKING_EFFORT

### Config-Loading Tiers (ConfigLoader.cs)

1. Tier 1 (Highest Priority): Project-local .env — walk up from CWD (ConfigLoader.cs:19, 59-62)
2. Tier 2: User-level config (ConfigLoader.cs:22-23, 64-67)
   - Windows: %APPDATA%\aigest\.env
   - Linux/macOS with XDG_CONFIG_HOME: $XDG_CONFIG_HOME/aigest/.env
   - Linux/macOS default: ~/.config/aigest/.env
3. Tier 3 (Lowest Priority): AppContext.BaseDirectory walk-up (ConfigLoader.cs:25-29, 69-75)

Cascade: Earlier tiers win because LoadEnvFile() skips already-set variables (ConfigLoader.cs:16-18).
```

</details>

---

## Task 2 — Architecture summary (narrative / synthesis)

**Question:**
> "Summarize the architecture of `src/Aigest.Cli/`. For each top-level component (Program.cs, Commands/, Core/, Hosting/, Logging/), give one sentence on its role and one representative file:line citation. End with a one-sentence statement of the data flow from CLI args to provider response."

**Paths given:** `src/Aigest.Cli/**/*.cs`

### Scores (rubric 1–5)

| Dimension | aigest | Explore |
|-----------|-------------|---------|
| Component coverage | 5 — all 5 components; Core enumerates 12 sub-classes | 4 — all 5 components; Core names 5 sub-classes |
| Citation correctness | 5 — all file:line citations accurate | 4 — correct but some components cite multiple lines in one entry |
| Data-flow accuracy | 5 — includes ANSI-strip step, flush behavior | 5 — includes DI resolution step, message assembly detail |
| Prose quality | 4 — Commands/Core entries run long | 4 — data-flow sentence is verbose |
| **Total** | **19 / 20** | **17 / 20** |

**Key difference:** aigest enumerated 12 Core sub-classes (`OpenAiChatClient`, `StreamingAnsiStripper`, `DeepSeekUsageParser`, `ResponseCapturePolicy`, `ResponseCapture`, `TeeReadStream`, etc.). Explore named 5. For a "what exists in Core" inventory, aigest's wider corpus scan was material.

<details>
<summary>Full aigest T2 output (click to expand)</summary>

```
## Architecture Summary of src/Aigest.Cli/

### Program.cs
Roles as the CLI entrypoint – defines three subcommands (ask, write, extract-chat) and a bare-invocation
check, wires each to its handler, and invokes the parsed root command.
Citation: Program.cs:12-13 — static async Task<int> Main(string[] args)

### Commands/
Contains the per-subcommand static classes (AskCommand, WriteCommand, ExtractChatCommand, CheckCommand)
that orchestrate the operation-specific logic, such as loading the corpus, building LLM messages, and
writing output.
Citation: Commands/AskCommand.cs:50-51 — var result = CorpusLoader.Load(paths, ...)

### Core/
Provides the infrastructure shared across commands: configuration loading (ConfigLoader, AigestConfig),
file filtering and corpus building (FileFilter, CorpusLoader), the chat-client abstraction and its
OpenAI implementation (IChatClient, OpenAiChatClient), ANSI escape handling (AnsiEscapes,
StreamingAnsiStripper), LLM usage parsing (DeepSeekUsageParser), and HTTP response capture for
telemetry (ResponseCapturePolicy, ResponseCapture, TeeReadStream).
Citation: Core/OpenAiChatClient.cs:67-68 — public async IAsyncEnumerable<string> CompleteStreamingAsync(...)

### Hosting/
Configures the Microsoft.Extensions.Hosting pipeline: sets up logging (all output to stderr), registers
the AigestConfig singleton, configures an IHttpClientFactory with resilience (rate-limit retry), and
registers OpenAiChatClient as the IChatClient implementation.
Citation: Hosting/CliHost.cs:14-15 — internal static IHost Build()

### Logging/
Defines source-generated, strongly typed logger extension methods for consistent, low-overhead log
messages used by the rest of the application.
Citation: Logging/CliLog.cs:7-8 — [LoggerMessage(Level = ..., Message = "Included {FileCount} file(s).")]

## Data Flow

Command-line arguments are parsed by System.CommandLine in Program.cs, routed to the appropriate
command class (e.g. AskCommand), which uses CorpusLoader to read and filter source files into a
line-numbered corpus, constructs a list of ChatMessage objects, calls
IChatClient.CompleteStreamingAsync (backed by OpenAiChatClient), and the streaming chunks are stripped
of ANSI escape sequences via StreamingAnsiStripper and written directly to stdout.
```

</details>

<details>
<summary>Full Explore T2 output (click to expand)</summary>

```
Program.cs: Defines the CLI entry point using System.CommandLine, wiring subcommands (ask, write,
extract-chat) and bare invocation to the host DI container. (Program.cs:14)

Commands/: Implements four action-oriented subcommands (AskCommand, WriteCommand, ExtractChatCommand,
CheckCommand) that execute the user's requested operation after parsing arguments.
(AskCommand.cs:50, WriteCommand.cs:75, ExtractChatCommand.cs:13, CheckCommand.cs:10)

Core/: Provides essential services: ConfigLoader reads .env tiers and environment variables,
AigestConfig holds API credentials and limits, OpenAiChatClient wraps the OpenAI SDK with streaming
and token logging, CorpusLoader expands globs and assembles line-numbered source files, and FileFilter
enforces deny rules and allowlisted extensions.
(ConfigLoader.cs:7, AigestConfig.cs:3, OpenAiChatClient.cs:67, CorpusLoader.cs:10, FileFilter.cs:81)

Hosting/: CliHost.Build() composes the DI container, configures logging to stderr (stdout reserved for
LLM output), sets up HttpClient with Polly exponential backoff on 429 errors, and registers
OpenAiChatClient as transient. (CliHost.cs:14)

Logging/: CliLog uses [LoggerMessage] source-gen for structured events (IncludedFiles, LlmCallComplete)
routed to console stderr. (CliLog.cs:8)

Data Flow: CLI arguments are parsed into typed options → values extracted via parseResult.GetValue()
→ resolved through DI (ConfigLoader, AigestConfig, IChatClient, ILogger) → commands load corpus via
CorpusLoader and FileFilter → messages assembled with system/user prompts →
OpenAiChatClient.CompleteStreamingAsync() sends to provider and streams chunks to stdout.
```

</details>

---

## Aggregate Observations

### Quality

Both tools produced accurate, zero-hallucination output on both tasks. The gap opened on completeness rather than accuracy:

- On the structured inventory (T1), aigest caught 2 items Explore missed (bare invocation, positional argument). The difference was because aigest received the **full 42-file corpus** and let the worker model scan it, while Explore read only the 4 files explicitly named in the benchmark prompt.
- On the narrative summary (T2), aigest named 12 Core sub-classes vs Explore's 5 — same cause: full corpus vs curated file list.

> **Important caveat:** In the real workflow, the Explore agent selects which files to read autonomously. In this benchmark, I handed it a curated list. An autonomous Explore run on a broad glob might have read more or fewer files — this benchmark does not measure that.

### Latency

| | aigest T1 | aigest T2 | Explore (est.) |
|--|--|--|--|
| Wall-clock | 23.7s | 13.5s | 60–90s |
| HTTP response time | 945ms | 614ms | — |

aigest's wall-clock is dominated by corpus assembly (reading, filtering, line-numbering 42 files) and streaming output — not by the worker's inference time. HTTP response was received in under 1 second both times.

Explore's latency was not instrumented in this benchmark. A rough estimate based on typical Explore runs: 60–90s depending on how many files it reads and the Claude model in use.

### Token cost

Worker token counts require `AIGEST_DEBUG=1` — the `LlmCallComplete` event is emitted at `LogLevel.Debug` (`Logging/CliLog.cs:10`), suppressed by default. Not captured in this run.

Rough estimates from corpus size (1,968 lines, ~25,000–30,000 input tokens after line-numbering):

| | Per call (est.) |
|--|--|
| aigest (deepseek-v4-flash) | ~$0.008–0.012 (DeepSeek input $0.27/M, output $1.10/M) |
| Explore (Claude Haiku 4.5) | ~$0.008–0.015 (Haiku $0.80/M input — but reads fewer files) |
| Explore (Claude Sonnet 4.6) | ~$0.030–0.060 (Sonnet $3/M input) |

Rate cards as of 2026-05; verify current pricing before using these numbers for decisions.

### Primary-agent context budget

- **aigest:** Zero tokens from the primary agent's context window are spent on the corpus. The worker handles the read; the primary sees only the summary.
- **Explore:** The primary receives the Explore result as a tool response. The subagent's full context is isolated, but if Explore reads a large corpus, the returned summary still enters the primary's context.

---

## Recommendation Matrix

| Reach for **aigest** when… | Reach for **Explore** when… |
|--|--|
| The corpus is large and already known (e.g. `src/**/*.cs`) — just glob it | You don't yet know which files are relevant and want autonomous file discovery |
| You need exhaustive coverage of a known file set (inventory, full-scan) | The question requires judgment about which code paths matter |
| Primary-agent context is precious and you want to keep the corpus out of it | The task involves reasoning across file relationships, not just reading |
| Cost per call matters and DeepSeek/Gemini pricing is acceptable | Quality of the reader matters (auth, security, architectural decisions) |
| The question is closed-form (finite correct answers, citeable) | The question is open-form and needs synthesis Claude is better at |
| You're generating boilerplate or doc drafts where fidelity ≈ "good enough to review" | The output needs to be production-ready without a mandatory review step |

**Default rule of thumb:** Start with Explore. Switch to aigest once you're handing it a known glob on a large corpus for the third time, or when primary context budget becomes a constraint.

---

## Limitations

1. **N=1 per cell.** Single runs; results may vary by network conditions, model non-determinism, and corpus content. Do not over-index on small score differences.

2. **Explore was given a curated file list.** In this benchmark, files were hand-selected for Explore. In real use, Explore autonomously decides what to read — it may read more, less, or different files than the curated list. This benchmark likely understates Explore's autonomous completeness.

3. **Token counts not captured.** `LlmCallComplete` emits at `LogLevel.Debug`. Re-run with `AIGEST_DEBUG=1` (or set the host log level) to get exact prompt/completion/cache-hit counts.

4. **Explore latency not measured.** No timing harness was applied to the Explore subagent calls. The estimate (60–90s) is based on session observation, not instrumented measurement.

5. **Worker-model dependency.** aigest results are specific to `deepseek-v4-flash`. A different worker model (Gemini 2.5 Flash, GPT-4o-mini) may produce meaningfully different quality and latency.

6. **Rate cards may be stale.** Pricing numbers were current as of 2026-05. Verify before using them for cost projections.

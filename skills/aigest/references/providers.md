# Provider Setup

`aigest` uses the .NET OpenAI SDK against OpenAI-compatible chat-completions APIs. Set worker settings in the user-level `~/.config/aigest/.env` (or `%APPDATA%\aigest\.env` on Windows):

```env
AIGEST_API_KEY=your_provider_key
AIGEST_BASE_URL=https://provider.example/v1
AIGEST_MODEL=provider-model-name
```

`AIGEST_API_KEY` is the clearest key name. Provider-specific fallbacks are accepted in this order; first match wins:

1. `AIGEST_API_KEY`
2. `GEMINI_API_KEY`
3. `DEEPSEEK_API_KEY`
4. `OPENAI_API_KEY`
5. `AZURE_OPENAI_API_KEY`

A stale provider-specific key is used when `AIGEST_API_KEY` is unset, which can silently route to the wrong provider. Unset stale provider keys when model or endpoint behavior does not match expectations.

## `.env` Discovery

The CLI loads tiers in order and keeps the earliest value for each key:

1. User config: `~/.config/aigest/.env` on Linux/macOS or `%APPDATA%\aigest\.env` on Windows. This is the canonical source.
2. Walk from `AppContext.BaseDirectory` up to the first `.git` boundary — only relevant for `dotnet run` inside the kit repo.

The current working directory is **not** walked, so a `.env` belonging to an arbitrary project you happen to be inside cannot silently override your worker settings. Set `AIGEST_*` shell environment variables to override the user config for a single invocation — they take precedence over both tiers.

## DeepSeek

DeepSeek is the default example provider:

```env
AIGEST_API_KEY=your_deepseek_key
AIGEST_BASE_URL=https://api.deepseek.com
AIGEST_MODEL=deepseek-v4-flash
```

## Gemini

Gemini supports the OpenAI-compatible endpoint:

```env
AIGEST_API_KEY=your_gemini_key
AIGEST_BASE_URL=https://generativelanguage.googleapis.com/v1beta/openai/
AIGEST_MODEL=gemini-2.5-flash
```

You may set `GEMINI_API_KEY` instead of `AIGEST_API_KEY`, but keep `AIGEST_BASE_URL` and `AIGEST_MODEL` explicit.

## OpenAI

```env
AIGEST_API_KEY=your_openai_key
AIGEST_BASE_URL=https://api.openai.com/v1
AIGEST_MODEL=gpt-4o-mini
```

## Local LLMs (Ollama / LM Studio / llama.cpp-server)

Set `AIGEST_PROVIDER=local` to opt into local-mode behavior:

- `AIGEST_API_KEY` becomes optional. The SDK still needs a non-empty credential, so the loader supplies the literal `ollama` when none is provided. Local OpenAI-compatible servers ignore the auth header.
- `AIGEST_THINKING_EFFORT` is suppressed at the request layer (most local models reject or mishandle `reasoning_effort`). `aigest` (no args) reports it as `(ignored: local mode)` so the user sees their env var isn't taking effect.
- Default `AIGEST_TIMEOUT_SECONDS` is `600` (vs `120` in cloud mode) for slower CPU inference. Override via env var if needed.
- Default `AIGEST_BASE_URL` is `http://localhost:11434/v1` (Ollama) and default `AIGEST_MODEL` is `llama3.2`. Override for LM Studio or any other server.
- `aigest` (no args) probes `GET {BaseUrl}/models` with a 5-second timeout and prints `Reachable : yes (N models available)` or `Reachable : no (...)`. Probe failure is non-fatal — exit code stays `0` so the diagnostic can run before the server is up.

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

`AIGEST_PROVIDER` accepts `local`, `cloud`, or `azure` (case-insensitive). Any other non-empty value hard-fails at startup, matching the `AIGEST_THINKING_EFFORT` precedent.

## Azure AI Foundry / Azure OpenAI Service

Set `AIGEST_PROVIDER=azure` to use Azure-hosted deployments. Two distinct Azure surfaces share the same `/openai/v1/` API shape — pick the hostname that matches the model you deployed:

| Hostname | Surface | Models hosted |
|---|---|---|
| `https://<resource>.openai.azure.com/openai/v1/` | Azure OpenAI Service | OpenAI's own models only (gpt-4o, gpt-5, o-series) |
| `https://<resource>.services.ai.azure.com/openai/v1/` | Azure AI Foundry | Full Foundry catalog (Kimi K2, DeepSeek, Llama, Mistral, etc.) |

Both accept the same API key (`Authorization: Bearer <key>`). Using the wrong hostname returns `HTTP 401` from the reachability probe even when the key is valid — a 401 against an Azure config almost always means the hostname does not match the deployed model's catalog, not that the key is wrong.

```env
AIGEST_PROVIDER=azure
AIGEST_API_KEY=your_azure_key
AIGEST_BASE_URL=https://<resource>.services.ai.azure.com/openai/v1/    # Foundry catalog (Kimi, DeepSeek, ...)
# AIGEST_BASE_URL=https://<resource>.openai.azure.com/openai/v1/       # Azure OpenAI Service (gpt-4o, gpt-5, ...)
AIGEST_MODEL=<deployment-name>
```

Alternatively, use `AZURE_OPENAI_API_KEY` instead of `AIGEST_API_KEY` (falls back if `AIGEST_API_KEY` is unset).

- `aigest` (no args) prints `Provider : azure (Foundry / OpenAI v1)` and probes `GET {BaseUrl}/models` with `Authorization: Bearer <key>` for reachability. Probe failure is non-fatal — exit code stays `0`.
- `AIGEST_THINKING_EFFORT` IS forwarded as `reasoning_effort`, supported on o-series and gpt-5 deployments.
- No defaults are applied — `AIGEST_BASE_URL` and `AIGEST_MODEL` are required and the loader fails fast if missing.
- Default timeout is `120` seconds (same as cloud mode). Override via `AIGEST_TIMEOUT_SECONDS` if needed.

## Reasoning / Thinking

Set `AIGEST_THINKING_EFFORT` to control upstream model reasoning. Accepts `low`, `medium`, `high`, or `xhigh`. Leave unset to disable. `xhigh` uses maximum reasoning compute and is supported on OpenAI GPT-5.1-Codex-Max and newer models.

| Provider | Behavior |
|---|---|
| OpenAI o-series / gpt-5 | Forwarded as `reasoning_effort` — works natively. |
| Azure (`AIGEST_PROVIDER=azure`) | Forwarded as `reasoning_effort` — supported on o-series and gpt-5 deployments. |
| Gemini 2.5 Pro/Flash (OpenAI-compat) | Forwarded as `reasoning_effort` — supported by the endpoint. |
| DeepSeek | Reasoning is delivered via a separate model (`deepseek-reasoner`); set `AIGEST_MODEL=deepseek-reasoner` instead. Sending `reasoning_effort` to `deepseek-chat` may be ignored or rejected. |
| Generic OpenAI-compat | Forwarded as `reasoning_effort`; provider may silently ignore it. |
| Local (`AIGEST_PROVIDER=local`) | Not forwarded. `aigest` (no args) reports the env var as ignored. |

When unset the field is absent from the request, so endpoints that reject unknown fields are unaffected.

**Reasoning budget.** Reasoning models (gpt-5, o-series) charge both hidden reasoning **and** visible output against the same `max_completion_tokens` budget. With `AIGEST_THINKING_EFFORT=high` / `xhigh` the model can exhaust the entire budget on reasoning before producing any content, which surfaces as an empty-response error. Raise `AIGEST_MAX_TOKENS` in `.env` (or pass `--max-tokens N` once) — `32768` is a reasonable starting point for `xhigh`. Resolution precedence is: explicit `--max-tokens` flag > `AIGEST_MAX_TOKENS` env var > per-command default (`8192` for `ask`, `16384` for `write`).

## Troubleshooting

- `401` or `Invalid Authentication`: regenerate the provider API key and confirm it belongs to the API platform, not a consumer chat session. For Azure (`AIGEST_PROVIDER=azure`), check the hostname first — `*.openai.azure.com` only serves OpenAI's own models, while Foundry-catalog models (Kimi, DeepSeek, Llama, ...) require `*.services.ai.azure.com`. A correct key against the wrong hostname surfaces as 401.
- DNS or connection errors: check whether the agent sandbox blocks network access, then rerun with network approval if available.
- Model not found: list models from the provider or copy the exact model ID from official docs.
- Empty key: inspect `.env` without printing secrets and confirm at least one supported API key variable is set.
- Quoted values are accepted, but avoid quotes unless your shell or editor requires them.
- Timeout during a large summary: narrow paths first, then raise `AIGEST_TIMEOUT_SECONDS` or `--max-tokens` only when the corpus and question are already scoped.

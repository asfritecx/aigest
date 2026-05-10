#!/usr/bin/env bash
set -euo pipefail

KIT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" >/dev/null 2>&1 && pwd)"
readonly KIT_DIR
readonly SOLUTION_FILE="aigest.slnx"
readonly PROJECT_DIR="src/Aigest.Cli"
readonly TARGET_DIR="${HOME:?HOME is not set}/.local/bin"
readonly PATH_EXPORT='export PATH="$HOME/.local/bin:$PATH"'

log() {
  printf '%s\n' "$*"
}

warn() {
  printf 'WARNING: %s\n' "$*" >&2
}

die() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

command_exists() {
  command -v "$1" >/dev/null 2>&1
}

detect_rid() {
  local os arch
  os=$(uname -s | tr '[:upper:]' '[:lower:]')
  arch=$(uname -m)

  case "$os" in
    darwin) os="osx" ;;
    linux)  os="linux" ;;
    mingw*|msys*|cygwin*) os="win" ;;
    *) die "unsupported operating system for runtime detection: $os" ;;
  esac

  case "$arch" in
    x86_64|amd64) arch="x64" ;;
    arm64|aarch64) arch="arm64" ;;
    *) die "unsupported CPU architecture for runtime detection: $arch" ;;
  esac

  printf '%s-%s\n' "$os" "$arch"
}

prebuilt_binary_name() {
  local rid="$1"

  if [[ "$rid" == win-* ]]; then
    printf 'aigest-%s.exe\n' "$rid"
  else
    printf 'aigest-%s\n' "$rid"
  fi
}

create_env_file() {
  [[ -f ".env.example" ]] || die ".env.example is missing; cannot create .env."

  # Tighten umask for the duration of this function so every new file gets 600
  # (owner-read/write only) and every new directory gets 700 — with no race
  # window between creation and a subsequent chmod.
  local saved_umask
  saved_umask=$(umask)
  umask 0077

  # Repo-local .env for contributors working inside this kit
  if [[ -f ".env" ]]; then
    log ".env already exists. Leaving it unchanged."
  else
    cp .env.example .env
    log "Created .env from .env.example (for kit development)."
  fi

  # User-level .env for the installed binary — readable from any directory
  local user_config_dir="${XDG_CONFIG_HOME:-$HOME/.config}/aigest"
  local user_env_file="$user_config_dir/.env"
  if [[ -f "$user_env_file" ]]; then
    log "$user_env_file already exists. Leaving it unchanged."
  else
    mkdir -p "$user_config_dir"
    cp .env.example "$user_env_file"
    log "Created $user_env_file (for the installed binary)."
    log "Edit it with: nano $user_env_file"
  fi

  umask "$saved_umask"
}

build_from_source() {
  local rid
  rid=$(detect_rid)

  [[ -f "$SOLUTION_FILE" ]] || die "$SOLUTION_FILE is missing."
  [[ -d "$PROJECT_DIR" ]] || die "$PROJECT_DIR is missing."

  log
  log "Building from source..."
  dotnet restore "$SOLUTION_FILE"
  dotnet build --no-restore "$SOLUTION_FILE"

  mkdir -p "$TARGET_DIR"

  dotnet publish "$PROJECT_DIR" \
    -c Release \
    -f net10.0 \
    -r "$rid" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o "$TARGET_DIR"

  log "Installed aigest to $TARGET_DIR"
}

print_prebuilt_instructions() {
  local rid binary_name
  rid=$(detect_rid)
  binary_name=$(prebuilt_binary_name "$rid")

  log
  warn "dotnet was not found. Skipping build."
  log
  log "To use aigest, download the pre-built standalone binary for your platform:"
  log "  $binary_name"
  log "from the GitHub Releases page, place it on your PATH as 'aigest',"
  log "and run it directly."
  log
  log "Or install the .NET 10 SDK to build from source."
}

warn_if_target_not_on_path() {
  if [[ -d "$TARGET_DIR" && ":${PATH:-}:" != *":$TARGET_DIR:"* ]]; then
    log
    warn "$TARGET_DIR is not on your PATH for this shell."
    log "Add this to your shell profile (~/.zshrc, ~/.bashrc, etc.):"
    log "  $PATH_EXPORT"
  fi
}

shell_profile_path() {
  case "${SHELL:-}" in
    */zsh) printf '%s\n' "$HOME/.zshrc" ;;
    */bash)
      if [[ "$(uname -s)" == "Darwin" ]]; then
        printf '%s\n' "$HOME/.bash_profile"
      else
        printf '%s\n' "$HOME/.bashrc"
      fi
      ;;
    *) printf '%s\n' "$HOME/.profile" ;;
  esac
}

refresh_shell_path() {
  local profile

  if [[ ! -d "$TARGET_DIR" ]]; then
    return
  fi

  if [[ ":${PATH:-}:" == *":$TARGET_DIR:"* ]]; then
    return
  fi

  export PATH="$TARGET_DIR:$PATH"
  hash -r 2>/dev/null || true

  profile=$(shell_profile_path)
  touch "$profile"

  if ! grep -Fqx "$PATH_EXPORT" "$profile"; then
    {
      printf '\n'
      printf '# aigest\n'
      printf '%s\n' "$PATH_EXPORT"
    } >>"$profile"
    log "Added $TARGET_DIR to PATH in $profile."
  else
    log "$TARGET_DIR is already configured in $profile."
  fi

  log "Refreshed PATH for this setup run."
  log "Open a new shell or run: source $profile"
}

print_next_steps() {
  local user_env_file="${XDG_CONFIG_HOME:-$HOME/.config}/aigest/.env"
  log
  log "Setup complete."
  log "Next:"
  log "  1. Edit your API key:"
  log "       nano $user_env_file          (used by the installed binary from any directory)"
  log "     Provider modes (optional):"
  log "       AIGEST_PROVIDER=cloud   (default — DeepSeek / OpenAI / Gemini OpenAI-compat endpoints)"
  log "       AIGEST_PROVIDER=local   (Ollama / LM Studio / llama.cpp-server — API key optional, 600s timeout)"
  log "       AIGEST_PROVIDER=azure   (Azure-hosted: *.openai.azure.com for OpenAI models,"
  log "                                *.services.ai.azure.com for Foundry catalog — Kimi, DeepSeek, Llama, ...)"

  if command_exists aigest || [[ -x "$TARGET_DIR/aigest" || -x "$TARGET_DIR/aigest.exe" ]]; then
    log "  2. Verify config:  aigest                     (env/config diagnostic — masked key, base URL, model, reachability)"
    log "  3. List commands:  aigest --help"
    log "  4. Smoke test:     ./run-smoke-test.sh             (end-to-end test against your configured provider)"
    log "  5. First real ask: aigest ask --paths README.md --question 'Summarize this project in 5 concise bullets. Do not invent anything.'"
  else
    log "  2. Download or build aigest and place it on your PATH"
    log "  3. Run: aigest --help"
  fi
}

main() {
  cd "$KIT_DIR"

  create_env_file

  if command_exists dotnet; then
    build_from_source
  else
    print_prebuilt_instructions
  fi

  refresh_shell_path
  warn_if_target_not_on_path
  print_next_steps
}

main "$@"

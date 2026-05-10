#!/usr/bin/env bash
set -euo pipefail

KIT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" >/dev/null 2>&1 && pwd)"
cd "$KIT_DIR"

VERSION=$(dotnet msbuild src/Aigest.Cli -getProperty:Version --nologo -v:q 2>/dev/null | tr -d '[:space:]')
[[ -n "$VERSION" ]] || { printf 'ERROR: could not read <Version> from project\n' >&2; exit 1; }
printf 'Publishing aigest %s\n' "$VERSION"

for rid in osx-arm64 osx-x64 linux-x64 linux-arm64 win-x64 win-arm64; do
  dotnet publish src/Aigest.Cli \
    -c Release \
    -f net10.0 \
    -r "$rid" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o "artifacts/aigest-$VERSION-$rid"
done

#!/usr/bin/env bash
# Copyright (C) 2026 WaveDudeNext contributors. GPL-3.0-or-later.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
VERSION="${VERSION:-0.1.3}"
CONFIGURATION="${CONFIGURATION:-Release}"
if [[ $# -eq 0 ]]; then set -- osx-arm64 osx-x64; fi
command -v dotnet >/dev/null || { echo 'Install the .NET 10 SDK first.' >&2; exit 1; }
command -v python3 >/dev/null || { echo 'Python 3 is required for packaging.' >&2; exit 1; }
for RID in "$@"; do
  case "$RID" in osx-arm64|osx-x64) ;; *) echo "Unsupported runtime: $RID" >&2; exit 2 ;; esac
  PUBLISH="$ROOT/artifacts/publish/$RID"
  # This path is generated exclusively by this script. Avoid stale publish files.
  if [[ -d "$PUBLISH" ]]; then rm -rf "$PUBLISH"; fi
  # Package restore reaches NuGet. No signing, notarization, or publishing is performed.
  dotnet publish WaveDudeNext.Desktop/WaveDudeNext.Desktop.csproj \
    -c "$CONFIGURATION" -r "$RID" --self-contained true -o "$PUBLISH" \
    -p:Version="$VERSION" -p:PublishSingleFile=false -p:PublishTrimmed=false \
    -p:PublishReadyToRun=false -p:DebugType=None -p:DebugSymbols=false
  python3 scripts/package-macos.py --rid "$RID" --version "$VERSION"
  python3 scripts/verify-macos-bundle.py --rid "$RID" --version "$VERSION"
done
python3 scripts/package-source.py
printf '\nArtifacts are in %s/artifacts. These builds are not Developer ID signed or notarized.\n' "$ROOT"

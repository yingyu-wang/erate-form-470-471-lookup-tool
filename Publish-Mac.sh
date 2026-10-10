#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
mac_rid="${1:-osx-arm64}"
case "$mac_rid" in osx-arm64|osx-x64) ;; *) echo 'Use osx-arm64 or osx-x64.'; exit 1 ;; esac
mac_dotnet="${DOTNET_EXECUTABLE:-dotnet}"
mac_output="publish/$mac_rid"
"$mac_dotnet" publish MacDesktop/MacDesktop.csproj -c Release -r "$mac_rid" --self-contained false -p:PublishSingleFile=false -p:PublishTrimmed=false -o "$mac_output/bin"
python3 scripts/package_mac.py "$mac_rid"

#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
linux_rid="${1:-linux-x64}"
case "$linux_rid" in linux-x64|linux-arm64) ;; *) echo 'Use linux-x64 or linux-arm64.' >&2; exit 1 ;; esac
linux_dotnet="${DOTNET_EXECUTABLE:-dotnet}"
"$linux_dotnet" publish LinuxDesktop/ERateLookup.Linux.csproj -c Release -r "$linux_rid" --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o "publish/$linux_rid/bin"
python3 scripts/package_linux.py "$linux_rid"

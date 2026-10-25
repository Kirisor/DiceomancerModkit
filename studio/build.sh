#!/usr/bin/env bash
set -e
. "$(cd "$(dirname "$0")/.." && pwd)/tools/env.sh"

cd "$MODKIT_ROOT/studio"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
"$DOTNET" build ModKit.Studio.csproj -c Release -v m 2>&1 | tail -40

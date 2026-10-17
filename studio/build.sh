set -e
cd "/g/Modkit/studio"
DOTNET="/g/deepseek space/dotnet-sdk/dotnet.exe"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
"$DOTNET" build ModKit.Studio.csproj -c Release -v m 2>&1 | tail -40
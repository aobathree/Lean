#!/bin/zsh
# bitbank E2E live test runner
# Builds the Lean Launcher and runs BitbankE2ETestAlgorithm against the live-bitbank
# environment with credentials injected from 1Password (never written to disk).
#
# Usage: ./run-e2e.sh
# The algorithm: subscribes BTCJPY ticks, places one min-lot (0.0001 BTC) post-only
# limit buy at 50% of market (cannot fill), cancels it after 90s, then quits itself.
set -euo pipefail

LEAN_DIR="$(cd "$(dirname "$0")/.." && pwd)"
CONNECTOR_DIR="$(cd "$(dirname "$0")" && pwd)"
# Homebrew dotnet (macOS): needs DOTNET_ROOT to locate the SDK
[ -d "/opt/homebrew/opt/dotnet/libexec" ] && export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"
export PATH="/opt/homebrew/bin:$PATH"

echo "== building Lean Launcher (includes bitbank connector + test algorithm) =="
dotnet build "$LEAN_DIR/Launcher/QuantConnect.Lean.Launcher.csproj" -v q --nologo

echo "== starting live-bitbank E2E test (Ctrl+C to abort) =="
cd "$LEAN_DIR/Launcher/bin/Debug"
exec op run --env-file="$CONNECTOR_DIR/.env.1password" -- \
  dotnet QuantConnect.Lean.Launcher.dll \
    --environment live-bitbank \
    --algorithm-type-name BitbankE2ETestAlgorithm

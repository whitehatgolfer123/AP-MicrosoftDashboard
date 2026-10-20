#!/usr/bin/env bash
# Run the Microsoft Ads Performance Auditor locally.
#
#   ./start.sh            # run the web app on http://localhost:5114
#   ./start.sh test       # run the test suite
#   ./start.sh build      # restore and build only
#
# Port 5114 matches the registered OAuth redirect URI
# (http://localhost:5114/signin-microsoftads). Override with PORT=xxxx.

set -euo pipefail
cd "$(dirname "$0")"

if ! command -v dotnet >/dev/null 2>&1; then
    # shellcheck source=dotnet-env.sh
    source ./dotnet-env.sh
fi

if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet not found on PATH and not present at \$HOME/.dotnet." >&2
    echo "Install the .NET 10 SDK, then run this script again." >&2
    exit 1
fi

PORT="${PORT:-5114}"
command="${1:-run}"

case "$command" in
    build)
        dotnet restore
        dotnet build
        ;;
    test)
        dotnet test
        ;;
    run)
        dotnet build
        echo
        echo "Starting the auditor on http://localhost:${PORT} (Ctrl+C to stop)."
        echo
        dotnet run --project src/MsAds.Auditor.Web --no-launch-profile \
            --urls "http://localhost:${PORT}"
        ;;
    *)
        echo "Unknown command: $command (expected run, build or test)" >&2
        exit 1
        ;;
esac

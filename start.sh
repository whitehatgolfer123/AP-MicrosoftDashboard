#!/usr/bin/env bash
# Run the Microsoft Ads Optimisation Platform locally.
#
#   ./start.sh            # run the web app on http://localhost:5114
#   ./start.sh test       # run the test suite
#   ./start.sh build      # restore and build only
#   ./start.sh format     # apply .editorconfig formatting
#   ./start.sh check      # everything CI checks: build, test and format
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
    format)
        dotnet format
        ;;
    check)
        # The same three gates as .github/workflows/ci.yml, so a failure here is a
        # failure there. -warnaserror is applied in CI rather than in
        # Directory.Build.props, so it has to be repeated here to match.
        echo "--- build (warnings fatal)"
        dotnet build -warnaserror
        echo
        echo "--- test"
        dotnet test --no-build
        echo
        echo "--- format"
        dotnet format --verify-no-changes
        echo
        echo "All checks passed."
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
        echo "Unknown command: $command" >&2
        echo "Expected one of: run, build, test, format, check." >&2
        exit 1
        ;;
esac

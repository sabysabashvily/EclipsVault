#!/usr/bin/env bash
# Local checks for agents and humans. Prints one line per step and, on failure, only the errors.
# Full output goes to logs/check/<step>.log (ignored).
#
#   scripts/check.sh                       build + unit tests (about 2 minutes)
#   scripts/check.sh integration [filter]  + integration tests on PostgreSQL, optionally a
#                                          dotnet test --filter expression; needs Docker
#   scripts/check.sh e2e [spec ...]        + browser tests, optionally only the named specs; needs
#                                          Docker, Seed__AdminPassword and ECLIPSVAULT_KEK (AGENTS.md)
#
# CI runs everything, including SQL Server and the whole e2e suite. Push for that.
set -uo pipefail
cd "$(dirname "$0")/.."
mode=${1:-fast}
[ $# -gt 0 ] && shift
case $mode in fast | integration | e2e) ;; *)
  echo "usage: scripts/check.sh [fast | integration [filter] | e2e [spec ...]]" >&2
  exit 2
  ;;
esac
log=logs/check
mkdir -p "$log"

step() {
  local name=$1
  shift
  if "$@" >"$log/$name.log" 2>&1; then
    echo "ok   $name"
  else
    echo "FAIL $name (full log: $log/$name.log)"
    grep -E -A4 ': error |^ +Failed |✘|Error: ' "$log/$name.log" | head -60
    exit 1
  fi
}

step restore dotnet restore EclipsVault.slnx --locked-mode
step build dotnet build EclipsVault.slnx -c Release --no-restore
step unit dotnet test EclipsVault.Tests -c Release --no-build --blame-hang-timeout 5m --blame-hang-dump-type mini

case $mode in
  fast) ;;
  integration)
    # Header tests serve the real Angular document, so build it once if it is missing.
    [ -f EclipsVault.Web/AngularUi/index.html ] ||
      step angular bash -c 'cd EclipsVault.AngularUi && npm ci --ignore-scripts && npm run build'
    step integration env ECLIPSVAULT_TEST_DATABASE_PROVIDER=Postgres \
      dotnet test EclipsVault.IntegrationTests -c Release --no-build \
      --blame-hang-timeout 10m --blame-hang-dump-type mini ${1:+--filter "$1"}
    ;;
  e2e)
    step e2e bash -c 'cd EclipsVault.AngularUi && npm ci --ignore-scripts && npm run e2e -- "$@"' e2e "$@"
    ;;
esac
echo "all checks passed"

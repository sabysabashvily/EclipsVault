#!/usr/bin/env bash
# Cloud Agent toolchain doctor for EclipsVault (no application build). scripts/check.sh verifies the code.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

fail=0
ok() { printf 'OK  %s\n' "$*"; }
bad() { printf 'FAIL %s\n' "$*" >&2; fail=1; }

expected_node="$(tr -d '[:space:]' < EclipsVault.AngularUi/.nvmrc)"

if ! command -v dotnet >/dev/null 2>&1; then
  bad "dotnet is not on PATH"
else
  sdk="$(dotnet --version 2>/dev/null || true)"
  case "$sdk" in
    10.*) ok "dotnet SDK $sdk" ;;
    *) bad "dotnet SDK must be 10.x (got '${sdk:-missing}')" ;;
  esac
fi

if ! command -v node >/dev/null 2>&1; then
  bad "node is not on PATH"
else
  node_v="$(node --version | sed 's/^v//')"
  if [[ "$node_v" == "$expected_node" ]]; then
    ok "node v$node_v (matches EclipsVault.AngularUi/.nvmrc)"
  else
    bad "node must be v$expected_node (got v$node_v)"
  fi
fi

if ! command -v npm >/dev/null 2>&1; then
  bad "npm is not on PATH"
else
  ok "npm $(npm --version)"
fi

if ! command -v docker >/dev/null 2>&1; then
  bad "docker CLI is not on PATH"
else
  ok "docker CLI $(docker --version | sed 's/^Docker version //')"
  if docker info >/dev/null 2>&1 || sudo docker info >/dev/null 2>&1; then
    driver="$( { docker info --format '{{.Driver}}' 2>/dev/null || sudo docker info --format '{{.Driver}}' 2>/dev/null || true; } | tr -d '\r\n' )"
    ok "docker daemon reachable (storage-driver=${driver:-unknown})"
  else
    bad "docker daemon is not reachable (start with: bash .cursor/start.sh)"
  fi
  if docker compose version >/dev/null 2>&1; then
    ok "docker compose $(docker compose version --short 2>/dev/null || docker compose version | head -n1)"
  else
    bad "docker compose plugin is missing"
  fi
fi

if [[ "$fail" -ne 0 ]]; then
  exit 1
fi

printf '\nAll cloud-agent environment checks passed.\n'

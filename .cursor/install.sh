#!/usr/bin/env bash
# Idempotent dependency refresh for Cloud Agent builds and boots.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

dotnet restore EclipsVault.slnx --locked-mode

(
  cd EclipsVault.AngularUi
  npm ci --ignore-scripts
)

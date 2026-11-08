#!/usr/bin/env bash
# Idempotent dependency refresh for Cloud Agent builds and boots.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

dotnet restore EclipsVault.slnx --locked-mode

(
  cd EclipsVault.AngularUi
  npm ci --ignore-scripts
  # The browser e2e tests need Chromium and its system libraries, as AGENTS.md's setup shows.
  npx playwright install --with-deps chromium
)

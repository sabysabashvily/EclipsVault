#!/usr/bin/env bash
# Start Docker for integration and browser e2e tests. Idempotent.
set -euo pipefail

docker_ready() {
  # Prefer the invoking user's docker access; fall back to sudo for readiness.
  docker info >/dev/null 2>&1 || sudo docker info >/dev/null 2>&1
}

if docker_ready; then
  exit 0
fi

if command -v service >/dev/null 2>&1; then
  sudo service docker start || true
fi

if docker_ready; then
  exit 0
fi

if ! pgrep -x dockerd >/dev/null 2>&1; then
  sudo dockerd >/tmp/dockerd.log 2>&1 &
fi

for _ in $(seq 1 30); do
  if docker_ready; then
    exit 0
  fi
  sleep 1
done

echo "docker daemon failed to become ready" >&2
if [[ -f /tmp/dockerd.log ]]; then
  tail -n 50 /tmp/dockerd.log >&2 || true
fi
exit 1

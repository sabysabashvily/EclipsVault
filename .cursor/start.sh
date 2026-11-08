#!/usr/bin/env bash
# Start Docker for integration and browser e2e tests. Idempotent.
set -euo pipefail

docker_ready() {
  docker info >/dev/null 2>&1 || sudo docker info >/dev/null 2>&1
}

clear_stale_docker() {
  # Nested VMs can leave a pid/socket after an unclean stop.
  if [[ -e /var/run/docker.pid ]] && ! pgrep -x dockerd >/dev/null 2>&1; then
    sudo rm -f /var/run/docker.pid
  fi
  if [[ -e /var/run/docker.sock ]] && ! pgrep -x dockerd >/dev/null 2>&1; then
    sudo rm -f /var/run/docker.sock
  fi
}

if docker_ready; then
  exit 0
fi

clear_stale_docker

if command -v service >/dev/null 2>&1; then
  sudo service docker start || true
fi

# service(8) can report success before the API socket accepts clients.
for _ in $(seq 1 15); do
  if docker_ready; then
    exit 0
  fi
  sleep 1
done

if ! pgrep -x dockerd >/dev/null 2>&1; then
  clear_stale_docker
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

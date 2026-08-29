#!/usr/bin/env bash
set -Eeuo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
cd "$repo_root"

compose=(docker compose --file compose.yaml --file compose.prod.yaml)
application_services=(web api postgres keycloak-db keycloak)

"${compose[@]}" config --quiet
"${compose[@]}" pull --ignore-buildable
"${compose[@]}" build --pull web api db-migrations

# This one-shot command must succeed before any application container is updated.
"${compose[@]}" run --rm --no-TTY db-migrations

"${compose[@]}" up --detach --no-build "${application_services[@]}"
"${compose[@]}" ps

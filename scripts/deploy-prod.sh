#!/usr/bin/env bash
set -Eeuo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
cd "$repo_root"

compose=(docker compose --file compose.yaml --file compose.prod.yaml)
application_services=(files-api api web postgres keycloak-db keycloak)

"${compose[@]}" config --quiet
# The migration profile makes pull include the files-db-migrations image as well as files-api.
"${compose[@]}" --profile migration pull --ignore-buildable
"${compose[@]}" build --pull \
  web api db-migrations

# Both one-shot schema jobs must succeed before any application container is updated.
"${compose[@]}" run --rm --no-TTY db-migrations
"${compose[@]}" run --rm --no-TTY files-db-migrations

"${compose[@]}" up --detach --no-build "${application_services[@]}"
"${compose[@]}" ps

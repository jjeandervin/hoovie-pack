#!/usr/bin/env bash
set -Eeuo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
cd "$repo_root"

compose=(docker compose --file compose.yaml --file compose.prod.yaml)
application_services=(files-api api web postgres keycloak-db keycloak)

"${compose[@]}" config --quiet
"${compose[@]}" pull --ignore-buildable
"${compose[@]}" build --pull \
  web api files-api db-migrations files-db-migrations legacy-media-migration

# Both one-shot schema jobs must succeed before any application container is updated.
"${compose[@]}" run --rm --no-TTY db-migrations
"${compose[@]}" run --rm --no-TTY files-db-migrations

# This is an inventory-only cutover gate. It never uploads, backfills, or deletes media.
if ! "${compose[@]}" run --rm --no-TTY legacy-media-migration --check; then
  cat >&2 <<'EOF'
Legacy media verification failed or found references that still require migration.
The application stack was not updated. Follow README.md "Existing media migration",
resolve every reported item, run the explicit importer, then rerun this deployment.
EOF
  exit 1
fi

"${compose[@]}" up --detach --no-build "${application_services[@]}"
"${compose[@]}" ps

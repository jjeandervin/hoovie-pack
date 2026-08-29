#!/bin/sh
set -eu

# This script is intentionally opt-in and intended only for local development.
: "${DEMO_OWNER_PASSWORD:?Set DEMO_OWNER_PASSWORD before seeding local demo users.}"
: "${DEMO_MEMBER_PASSWORD:?Set DEMO_MEMBER_PASSWORD before seeding local demo users.}"
: "${KC_BOOTSTRAP_ADMIN_USERNAME:?KC_BOOTSTRAP_ADMIN_USERNAME is required.}"
: "${KC_BOOTSTRAP_ADMIN_PASSWORD:?KC_BOOTSTRAP_ADMIN_PASSWORD is required.}"

kcadm="/opt/keycloak/bin/kcadm.sh"
kcadm_config="/tmp/hooviepack-demo-seed-kcadm.config"
realm="hooviepack"
trap 'rm -f -- "$kcadm_config"' EXIT HUP INT TERM

"$kcadm" config credentials \
  --config "$kcadm_config" \
  --server http://127.0.0.1:8080 \
  --realm master \
  --user "$KC_BOOTSTRAP_ADMIN_USERNAME" \
  --password "$KC_BOOTSTRAP_ADMIN_PASSWORD" >/dev/null

seed_user() {
  username="$1"
  email="$2"
  first_name="$3"
  last_name="$4"
  password="$5"

  user_id=$("$kcadm" get users \
    --config "$kcadm_config" \
    --realm "$realm" \
    --query "username=$username" \
    --query exact=true \
    --fields id \
    --format csv \
    --noquotes | awk 'NF { print $1; exit }')

  if [ -z "$user_id" ]; then
    user_id=$("$kcadm" create users \
      --config "$kcadm_config" \
      --realm "$realm" \
      -i \
      --set "username=$username" \
      --set "email=$email" \
      --set "firstName=$first_name" \
      --set "lastName=$last_name" \
      --set enabled=true \
      --set emailVerified=true)
  else
    "$kcadm" update "users/$user_id" \
      --config "$kcadm_config" \
      --realm "$realm" \
      --set enabled=true \
      --set emailVerified=true >/dev/null
  fi

  "$kcadm" set-password \
    --config "$kcadm_config" \
    --realm "$realm" \
    --userid "$user_id" \
    --new-password "$password" \
    --temporary >/dev/null

  printf 'Seeded local demo user %s with a temporary password.\n' "$username"
}

seed_user "demo.owner" "owner@hooviepack.local" "Demo" "Owner" "$DEMO_OWNER_PASSWORD"
seed_user "demo.member" "member@hooviepack.local" "Demo" "Member" "$DEMO_MEMBER_PASSWORD"

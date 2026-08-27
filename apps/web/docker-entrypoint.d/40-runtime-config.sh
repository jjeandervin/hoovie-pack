#!/bin/sh
set -eu

api_base_url="${API_BASE_URL:-/api}"
oidc_issuer="${OIDC_ISSUER:-http://localhost:8081/realms/hooviepack}"
oidc_client_id="${OIDC_CLIENT_ID:-hooviepack-web}"
oidc_redirect_uri="${OIDC_REDIRECT_URI:-}"
oidc_post_logout_redirect_uri="${OIDC_POST_LOGOUT_REDIRECT_URI:-}"

json_escape() {
  printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'
}

cat > /usr/share/nginx/html/assets/config.json <<EOF
{
  "apiBaseUrl": "$(json_escape "$api_base_url")",
  "oidcIssuer": "$(json_escape "$oidc_issuer")",
  "oidcClientId": "$(json_escape "$oidc_client_id")",
  "oidcRedirectUri": "$(json_escape "$oidc_redirect_uri")",
  "oidcPostLogoutRedirectUri": "$(json_escape "$oidc_post_logout_redirect_uri")"
}
EOF

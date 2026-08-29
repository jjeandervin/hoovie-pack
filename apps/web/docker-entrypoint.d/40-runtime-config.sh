#!/bin/sh
set -eu

api_base_url="${API_BASE_URL:-/api}"
oidc_issuer="${OIDC_ISSUER:-http://localhost:8081/realms/hooviepack}"
oidc_client_id="${OIDC_CLIENT_ID:-hooviepack-web}"
oidc_redirect_uri="${OIDC_REDIRECT_URI:-}"
oidc_post_logout_redirect_uri="${OIDC_POST_LOGOUT_REDIRECT_URI:-}"
csp_oidc_origin="${CSP_OIDC_ORIGIN:-http://localhost:8081}"

if ! printf '%s\n' "$csp_oidc_origin" \
  | grep -Eq '^https?://([A-Za-z0-9.-]+|\[[0-9A-Fa-f:]+\])(:[0-9]{1,5})?$'; then
  printf 'CSP_OIDC_ORIGIN must contain only an HTTP(S) scheme, host, and optional port.\n' >&2
  exit 1
fi

# Substitute only this placeholder so nginx runtime variables remain intact.
export CSP_OIDC_ORIGIN="$csp_oidc_origin"
envsubst '${CSP_OIDC_ORIGIN}' \
  < /etc/nginx/conf.d/default.conf \
  > /tmp/hooviepack-nginx.conf
mv /tmp/hooviepack-nginx.conf /etc/nginx/conf.d/default.conf

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

# HooviePack

HooviePack is a private, mobile-first family social app with a warm, corgi-inspired personality. The monorepo contains an Angular web client, an ASP.NET Core API, PostgreSQL persistence, Keycloak OIDC authentication, and production Nginx routing.

The product and engineering requirements are preserved in [docs/spec.md](docs/spec.md).

## Architecture

| Service | Container port | Default local URL | Persistent data |
| --- | ---: | --- | --- |
| Angular web | 80 | `http://localhost:4200` | none |
| ASP.NET Core API | 8080 | `http://localhost:5000` | `media_data` |
| PostgreSQL (app) | 5432 | `localhost:5432` | `postgres_data` |
| Keycloak | 8080 | `http://localhost:8081` | via `keycloak_data` |
| PostgreSQL (Keycloak) | 5432 | internal only | `keycloak_data` |
| Nginx | 80 / 443 | production profile only | host TLS files |

All published development ports bind to `127.0.0.1` by default. Containers communicate over the private `hooviepack` Compose network. Nginx is deliberately behind the `production` profile because it requires a valid certificate.

## Local setup

Prerequisites:

- Docker Engine or Docker Desktop with Docker Compose v2
- Git
- Approximately 4 GB of free memory for builds, PostgreSQL, and Keycloak

From the repository root, create the local environment file:

```powershell
Copy-Item .env.example .env
```

On Linux or macOS, use `cp .env.example .env` instead. The checked-in replacement values are sufficient only for loopback development. Before using shared or production infrastructure, replace every `replace-with-...` value. A convenient generator is:

```bash
openssl rand -base64 48
```

Build and start the local stack:

```bash
docker compose config --quiet
docker compose up --build -d
docker compose ps
```

First startup can take a few minutes while images build, databases initialize, the realm imports, and EF Core applies the initial migration. Follow startup with:

```bash
docker compose logs -f api web keycloak
```

Open:

- App: `http://localhost:4200`
- API health: `http://localhost:5000/health/ready`
- API Swagger UI: `http://localhost:5000/swagger`
- Keycloak: `http://localhost:8081`
- Keycloak admin console: `http://localhost:8081/admin/`

Swagger UI is enabled only when `APP_ENVIRONMENT=Development`. It includes an **Authorize** control for a Keycloak bearer access token; enter the token using the UI's Bearer security scheme when exercising protected endpoints. Swagger is intentionally unavailable in Production.

The bootstrap admin credentials come from `KEYCLOAK_ADMIN` and `KEYCLOAK_ADMIN_PASSWORD` in `.env`.

Two development identities are imported on the first run:

- `demo.owner` / the value of `DEMO_OWNER_PASSWORD`
- `demo.member` / the value of `DEMO_MEMBER_PASSWORD`

Both passwords are temporary, so Keycloak requires a change on first login. These are authentication identities only; family ownership and membership are managed by HooviePack after login.

Stop the services without deleting data:

```bash
docker compose down
```

To intentionally discard all local databases and uploaded media, use `docker compose down --volumes`. This is destructive and cannot be undone without a backup.

## Debugging

### Docker development mode

The default Compose configuration runs the application with `ASPNETCORE_ENVIRONMENT=Development`, enabling development logging and Swagger:

```powershell
docker compose up --build
```

Open the app at `http://localhost:4200` or Swagger at `http://localhost:5000/swagger`. Follow service output with:

```powershell
docker compose logs -f api web keycloak
```

The API Docker image is published in Release mode, so use the local workflow below when source breakpoints or .NET Hot Reload are needed.

### Local API debugging

Stop the containerized web and API services if they are running, then run the API from the .NET SDK:

```powershell
docker compose stop web api
```

Every API Debug build automatically runs `docker compose up --detach --wait postgres keycloak-db keycloak`. This starts any missing infrastructure, waits for it to become healthy, reuses containers that are already running, and leaves them running after the debugger stops. Release builds do not start containers. To explicitly skip this behavior—for example in CI—build with `-p:StartDebugDependencies=false`.

The API project has a `UserSecretsId`. On each development workstation, populate its machine-local secret store once from the ignored `.env` file:

```powershell
$localSettings = Get-Content .env -Raw | ConvertFrom-StringData

$connectionString = `
  "Host=localhost;Port=$($localSettings.POSTGRES_PORT);Database=$($localSettings.POSTGRES_DB);Username=$($localSettings.POSTGRES_USER);Password=$($localSettings.POSTGRES_PASSWORD)"

dotnet user-secrets set "ConnectionStrings:DefaultConnection" $connectionString `
  --project .\apps\api\src\HooviePack.Api\HooviePack.Api.csproj
```

Then start the API with its `http` launch profile:

```powershell
dotnet watch --project .\apps\api\src\HooviePack.Api\HooviePack.Api.csproj `
  run --launch-profile http
```

The local API and Swagger UI will be available at `http://localhost:5103` and `http://localhost:5103/swagger`. For IDE breakpoints, open `apps/api/HooviePack.slnx`, select the `http` launch profile, and start debugging. ASP.NET Core loads user secrets automatically in Development. The secret value is stored outside the repository and must not be copied into `launchSettings.json`.

### Angular source debugging

Angular debugging requires Node.js 22 and npm. The development environment calls the local API directly at `http://localhost:5103/api`; the API allows requests from `http://localhost:4200` through its CORS configuration. Install dependencies and start the Angular development server:

```powershell
Set-Location .\apps\web
npm ci
npm start
```

Open `http://localhost:4200`. The development Angular configuration enables source maps, so TypeScript breakpoints work in browser developer tools or an IDE browser-debug configuration. Keycloak already permits the local `http://localhost:4200` callback URLs.

## Verification

Run the API unit and PostgreSQL integration tests from `apps/api`:

```powershell
dotnet test HooviePack.slnx -c Release
```

With the local Compose stack healthy, Windows PowerShell or PowerShell 7 can run the full OIDC/API smoke test:

```powershell
.\scripts\e2e-smoke.ps1
```

The script uses Authorization Code Flow with PKCE to create disposable owner, member, and outsider identities. It verifies profile synchronization, family creation and invite joining, photo posts, comments, reactions, dogs, protected media, cross-family isolation, and malformed-image rejection. It leaves the generated records in the local development volumes for inspection; use `docker compose down --volumes` only when you intentionally want to reset that disposable data.

## Authentication configuration

The imported `hooviepack` realm contains:

- `hooviepack-web`, a public OIDC client using Authorization Code Flow with mandatory S256 PKCE
- `hooviepack-api`, the bearer-token audience
- an audience mapper that adds `hooviepack-api` to web access tokens
- local and production redirect/origin entries derived from `LOCAL_APP_ORIGIN` and `APP_ORIGIN`
- brute-force protection and two temporary-password demo users

The browser uses `${KEYCLOAK_PUBLIC_URL}/realms/hooviepack` as its issuer. The API obtains discovery/JWKS metadata from `KEYCLOAK_METADATA_URL` over the Docker network, but validates the public issuer. Keeping these settings separate is intentional: `localhost` inside the API container is not the developer's host. The default backchannel is private HTTP, so `AUTH_REQUIRE_HTTPS_METADATA` must remain `false`; access tokens are still validated for signature, public HTTPS issuer, audience, and lifetime.

Realm startup import happens only when `hooviepack` does not already exist. Editing the JSON does not overwrite a live realm. To deliberately re-import it:

```bash
docker compose stop keycloak
docker compose run --rm keycloak import --file /opt/keycloak/data/import/hooviepack-realm.json --override true
docker compose up -d keycloak
```

Export/back up the realm before overriding a non-development instance. The bootstrap administrator is likewise created only when the Keycloak database is empty.

For production, disable or delete both demo users, enable verified email once SMTP is configured, rotate the bootstrap password, and review redirect URIs in the Keycloak console. Google or Microsoft login can later be added as Keycloak identity providers without changing the app's OIDC contract.

## Database migrations

Development defaults `Database__ApplyMigrations` to `true`, so the API applies committed EF Core migrations during startup.

With a local .NET SDK, migrations can also be applied manually from `apps/api`:

```bash
dotnet ef database update --project src/HooviePack.Api/HooviePack.Api.csproj --startup-project src/HooviePack.Api/HooviePack.Api.csproj
```

For a production rollout:

1. Back up the application database and uploaded-media volume.
2. Set `DATABASE_APPLY_MIGRATIONS=true` for the deployment.
3. Run `docker compose up -d --build api` and wait for `/health/ready` plus a successful migration message in `docker compose logs api`.
4. Set `DATABASE_APPLY_MIGRATIONS=false` and run `docker compose up -d api` again so later restarts do not mutate the schema unexpectedly.

Run only one migrating API instance at a time. Review destructive migrations before deployment.

## Production deployment on Linux

The production target is:

- `https://hooviestar.com` → Angular
- `https://hooviestar.com/api` → ASP.NET Core
- `https://auth.hooviestar.com` → Keycloak

### 1. Prepare the host and DNS

Use a supported Linux distribution, install Docker Engine plus the Compose plugin, and point `A`/`AAAA` records for both `hooviestar.com` and `auth.hooviestar.com` at the server. Permit inbound TCP 80 and 443 (and SSH from trusted sources); do not expose database or container application ports publicly.

Place the checkout in a stable location such as `/opt/hooviepack`. Copy the environment template, generate independent random values for every password, then restrict the file:

```bash
cp .env.example .env
chmod 600 .env
```

Set at least these production values in `.env`:

```dotenv
BIND_ADDRESS=127.0.0.1
APP_ENVIRONMENT=Production
APP_ORIGIN=https://hooviestar.com
KEYCLOAK_PUBLIC_URL=https://auth.hooviestar.com
KEYCLOAK_METADATA_URL=http://keycloak:8080/realms/hooviepack/.well-known/openid-configuration
AUTH_REQUIRE_HTTPS_METADATA=false
DATABASE_APPLY_MIGRATIONS=false
LETSENCRYPT_DIR=/etc/letsencrypt
ACME_CHALLENGE_DIR=/var/www/certbot
```

Do not add a trailing slash to either public URL. Leave `API_BASE_URL=/api`.

The sample keeps OIDC discovery on the isolated Compose network, which is why metadata HTTPS enforcement is `false` even in production. If the API container can reliably reach `https://auth.hooviestar.com`, you may instead set `KEYCLOAK_METADATA_URL=https://auth.hooviestar.com/realms/hooviepack/.well-known/openid-configuration` and `AUTH_REQUIRE_HTTPS_METADATA=true`. Do not combine the internal `http://keycloak:8080` URL with HTTPS enforcement.

### 2. Obtain TLS certificates

The sample Nginx config expects a single Let's Encrypt certificate named `hooviestar.com` containing both hostnames. With ports 80/443 free:

```bash
sudo certbot certonly --standalone \
  --cert-name hooviestar.com \
  -d hooviestar.com \
  -d auth.hooviestar.com
```

Because Compose mounts the entire `/etc/letsencrypt` tree read-only, the symlinks in `live/` can resolve into `archive/`. If certificates are managed elsewhere, preserve these in-container paths or update `infra/nginx/conf.d/hooviepack.conf`:

- `/etc/letsencrypt/live/hooviestar.com/fullchain.pem`
- `/etc/letsencrypt/live/hooviestar.com/privkey.pem`

### 3. Start and verify

Validate expansion, start the production profile, and inspect health:

```bash
docker compose --profile production config --quiet
docker compose --profile production up -d --build
docker compose ps
docker compose logs --tail=200 nginx api keycloak
```

Verify from another machine:

```bash
curl --fail --show-error https://hooviestar.com/api/health/ready
curl --fail --show-error https://auth.hooviestar.com/realms/hooviepack/.well-known/openid-configuration
```

The Nginx sample terminates TLS, redirects HTTP, forwards the original scheme/host to Keycloak, caps requests at 42 MiB, and never publishes the Keycloak management port.

### 4. Renew and update

Use the distribution's Certbot timer. Reload Nginx after a successful renewal:

```bash
cd /opt/hooviepack
docker compose --profile production exec -T nginx nginx -s reload
```

Before application, PostgreSQL, or Keycloak image upgrades, read their release/migration notes and take backups. Image tags are pinned in `.env`; update them deliberately, then run:

```bash
docker compose --profile production pull
docker compose --profile production up -d --build
```

## Backups and security checklist

Create encrypted, off-host backups on a tested schedule. A logical application database backup can be made without placing its password on the command line:

```bash
mkdir -p backups
docker compose exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB"' \
  | gzip > "backups/hooviepack-$(date +%F-%H%M%S).sql.gz"
docker compose exec -T keycloak-db sh -c 'pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB"' \
  | gzip > "backups/keycloak-$(date +%F-%H%M%S).sql.gz"
```

Also snapshot or archive the `media_data` volume. Periodically test restoration of all three together; database records and media files should represent the same point in time.

Before exposing the service:

- replace all sample passwords and keep `.env` mode `0600`
- remove/disable demo identities and configure SMTP plus email verification
- keep `BIND_ADDRESS=127.0.0.1`; expose only Nginx ports 80/443
- keep PostgreSQL and the Keycloak management port off the public network
- restrict Keycloak admin-console access with a VPN or an Nginx IP allowlist where practical
- keep host, Docker, base images, Keycloak, and PostgreSQL patched
- test file-type/size validation and family authorization for every media route
- monitor container health, authentication failures, disk usage, and certificate expiry
- never use realm exports as the sole Keycloak database backup

## Troubleshooting

`docker compose ps` shows health for every service. Useful targeted logs are:

```bash
docker compose logs --tail=200 postgres keycloak-db
docker compose logs --tail=200 keycloak
docker compose logs --tail=200 api
docker compose logs --tail=200 web nginx
```

Common causes:

- **Compose reports a required variable error:** copy `.env.example` to `.env` and fill every password.
- **Login redirects to the wrong host:** make `KEYCLOAK_PUBLIC_URL`, `APP_ORIGIN`, DNS, TLS names, and Keycloak client redirect URIs agree.
- **Realm edits are ignored:** startup import skips an existing realm; use the deliberate re-import procedure or edit through the admin console.
- **API stays unhealthy:** check PostgreSQL readiness and migration logs, then query `/health/ready` directly.
- **Nginx will not start:** confirm the certificate files exist beneath `LETSENCRYPT_DIR/live/hooviestar.com/` and run `docker compose --profile production run --rm nginx nginx -t`.
- **Port already allocated:** change the relevant local port in `.env`; production ports 80/443 must be free.

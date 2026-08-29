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

On Linux or macOS, use `cp .env.example .env` instead. The checked-in replacement values are sufficient only for loopback development. Before using shared or production infrastructure, replace every required `replace-with-...` value. Never commit `.env`; it contains deployment credentials and is intentionally ignored. A convenient generator is:

```bash
openssl rand -base64 48
```

Build and start the local stack:

```bash
docker compose config --quiet
docker compose up --build -d
docker compose ps
```

First startup can take a few minutes while images build, databases initialize, the realm imports, and EF Core applies the initial migration. The realm import does not create users. Follow startup with:

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

The bootstrap admin credentials come from `KEYCLOAK_ADMIN` and `KEYCLOAK_ADMIN_PASSWORD` in `.env`. To explicitly create or reset the two local-only demo identities, set their development passwords in `.env`, start Keycloak, and run:

```bash
docker compose exec keycloak /bin/sh /opt/keycloak/seed-demo-users.sh
```

The idempotent seed creates:

- `demo.owner` / the value of `DEMO_OWNER_PASSWORD`
- `demo.member` / the value of `DEMO_MEMBER_PASSWORD`

Both passwords are temporary, so Keycloak requires a change on first login. These are authentication identities only; family ownership and membership are managed by HooviePack after login. Do not set either demo password or run the seed in production.

Stop the services without deleting data:

```bash
docker compose down
```

To intentionally discard all local databases and uploaded media, use `docker compose down --volumes` (or `docker compose down -v`). **This destroys the persisted named volumes and cannot be undone without a backup. Never run it against production.**

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

### CI and security checks

`.github/workflows/ci.yml` runs on pushes and pull requests. It restores, builds, and tests the API against PostgreSQL; installs the locked web dependencies, audits them, runs web tests, and makes a production build; scans full Git history with Gitleaks; fails on vulnerable direct or transitive NuGet packages using JSON output; and scans the custom API/web images with Trivy for fixable high/critical vulnerabilities. `.github/dependabot.yml` opens grouped weekly NuGet, npm, Docker/Compose, and GitHub Actions updates.

The core checks can be run locally from the repository root (the NuGet guard requires `jq`, and image scans require Trivy):

```bash
docker run --rm -v "$PWD:/repo" -w /repo \
  ghcr.io/gitleaks/gitleaks:v8.30.1@sha256:c00b6bd0aeb3071cbcb79009cb16a60dd9e0a7c60e2be9ab65d25e6bc8abbb7f \
  git --redact --verbose --config .gitleaks.toml .

dotnet restore apps/api/HooviePack.slnx
dotnet build apps/api/HooviePack.slnx -c Release --no-restore
dotnet test apps/api/HooviePack.slnx -c Release --no-build
dotnet package list --project apps/api/HooviePack.slnx --vulnerable \
  --include-transitive --format json --output-version 1 --no-restore \
  > nuget-vulnerabilities.json
jq -e '[.projects[]?.frameworks[]? | ((.topLevelPackages // []) + \
  (.transitivePackages // []))[]? | select((.vulnerabilities // []) | \
  length > 0)] | length == 0' nuget-vulnerabilities.json
rm -f nuget-vulnerabilities.json

(cd apps/web && npm ci --no-audit --no-fund && npm audit --audit-level=high \
  && npm test && npm run build:production)

docker build --pull -t hooviepack-api:local apps/api
docker build --pull -t hooviepack-web:local apps/web
trivy image --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed \
  --exit-code 1 hooviepack-api:local
trivy image --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed \
  --exit-code 1 hooviepack-web:local
```

PostgreSQL integration tests run when `HOOVIEPACK_TEST_POSTGRES` points to a disposable test database; CI provisions that database automatically. Never use production credentials for local or CI testing, and never commit scan output containing sensitive findings.

## Authentication configuration

The imported `hooviepack` realm contains:

- `hooviepack-web`, a public OIDC client using Authorization Code Flow with mandatory S256 PKCE
- `hooviepack-api`, the bearer-token audience
- an audience mapper that adds `hooviepack-api` to web access tokens
- local and production redirect/origin entries derived from `LOCAL_APP_ORIGIN` and `APP_ORIGIN`
- brute-force protection; the production-safe realm import contains no users

The browser and `Authentication__Authority` use `${KEYCLOAK_PUBLIC_URL}/realms/hooviepack`. `Authentication__ValidIssuer` independently pins tokens to that public issuer, which must be HTTPS in production. `Authentication__MetadataAddress` may use `http://keycloak:8080` on the isolated Compose network so the API can fetch discovery/JWKS without routing out through the public proxy. Production keeps `Authentication__RequireHttpsMetadata=true`; the API narrowly exempts only loopback and the Docker-internal `keycloak` hostname and rejects arbitrary external HTTP metadata URLs. Signature, public issuer, audience, and lifetime validation remain enabled.

Realm startup import happens only when `hooviepack` does not already exist. Editing the JSON does not overwrite a live realm. To deliberately re-import it:

```bash
docker compose stop keycloak
docker compose run --rm keycloak import --file /opt/keycloak/data/import/hooviepack-realm.json --override true
docker compose up -d keycloak
```

Export/back up the realm before overriding a non-development instance. The bootstrap administrator is likewise created only when the Keycloak database is empty. Removing users from the import JSON does **not** delete or disable users in an existing Keycloak database. Operators must manually disable or delete any existing `demo.owner` and `demo.member` accounts in production.

For production, leave both demo-password variables unset, enable verified email once SMTP is configured, rotate the bootstrap password, and review redirect URIs in the Keycloak console. Google or Microsoft login can later be added as Keycloak identity providers without changing the app's OIDC contract.

## Database migrations

Development defaults `Database__ApplyMigrations` to `true`, so the API applies committed EF Core migrations during startup.

With a local .NET SDK, migrations can also be applied manually from `apps/api`:

```bash
dotnet ef database update --project src/HooviePack.Api/HooviePack.Api.csproj --startup-project src/HooviePack.Api/HooviePack.Api.csproj
```

For a production rollout, without taking the whole stack down:

1. Back up the application database (and coordinate a media snapshot if the release changes media persistence).
2. Set `DATABASE_APPLY_MIGRATIONS=true` for the deployment.
3. Run `docker compose -f compose.yaml -f compose.prod.yaml up -d --build api` and wait for `/health/ready` plus a successful migration message in `docker compose logs api`.
4. Verify the expected migration and application behavior.
5. Set `DATABASE_APPLY_MIGRATIONS=false` (or remove the override) and run `docker compose -f compose.yaml -f compose.prod.yaml up -d api` so later restarts do not mutate the schema unexpectedly.

`compose.prod.yaml` defaults migrations to `false` even though development defaults them to `true`. Run only one migrating API instance at a time and review destructive migrations before deployment.

## Production deployment on Linux

Production uses the external `jeandervin-proxy`; the Nginx container and configuration in this repository are a secured reference/example, not the deployed edge. The production target is:

- `https://hooviestar.com` → Angular
- `https://hooviestar.com/api` → ASP.NET Core
- `https://auth.hooviestar.com` → Keycloak

### 1. Prepare the host and DNS

Use a supported Linux distribution, install Docker Engine plus the Compose plugin, and point `A`/`AAAA` records for both `hooviestar.com` and `auth.hooviestar.com` at the external proxy. Permit inbound TCP 80 and 443 (and SSH from trusted sources); do not expose database or application container ports publicly. Ensure the existing `jeandervin` Docker network is available and the external proxy is attached to it.

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
AUTH_REQUIRE_HTTPS_METADATA=true
DATABASE_APPLY_MIGRATIONS=false
DEMO_OWNER_PASSWORD=
DEMO_MEMBER_PASSWORD=
```

Do not add a trailing slash to either public URL. Leave `API_BASE_URL=/api`, use independent random database/admin passwords, and never commit `.env`.

The production overlay keeps the HTTPS-metadata policy enabled. As described under authentication configuration, the API recognizes `http://keycloak:8080` as a trusted, isolated backchannel while still validating the public HTTPS issuer. The web container also builds its CSP from the exact `KEYCLOAK_PUBLIC_URL` origin, so production does not inherit a wildcard localhost allowance.

### 2. Configure the external reverse proxy

Configure TLS and these upstream routes on the real `jeandervin-proxy`:

- `hooviestar.com` `/api` to `hooviepack-api:8080`
- the rest of `hooviestar.com` to `hooviepack-web:80`
- `auth.hooviestar.com` to `hooviepack-keycloak:8080`

Forward the original `Host`, scheme, address, and port. Normal OIDC routes must remain public, including discovery, authorization, token, JWKS, logout, and login redirects. Restrict only `/admin/` (and its subpaths) to exact LAN/VPN/trusted administrator ranges. An Nginx-style rule is:

```nginx
location ^~ /admin/ {
    allow <LAN-or-VPN-CIDR>;
    deny all;
    proxy_pass http://hooviepack-keycloak:8080;
}

location / {
    proxy_pass http://hooviepack-keycloak:8080;
}
```

Adapt the syntax to the external proxy and preserve its normal forwarded headers. The in-repository [`infra/nginx/conf.d/hooviepack.conf`](infra/nginx/conf.d/hooviepack.conf) demonstrates a safe loopback-only default, but changing it does not update `jeandervin-proxy`; that external configuration is a required manual production action.

### 3. Start and verify

Validate the merged configuration, run the normal production deployment, and inspect health:

```bash
docker compose -f compose.yaml -f compose.prod.yaml config --quiet
docker compose -f compose.yaml -f compose.prod.yaml up -d --build
docker compose -f compose.yaml -f compose.prod.yaml ps
docker compose -f compose.yaml -f compose.prod.yaml logs --tail=200 api keycloak web
```

Verify readiness from the deployment host, then verify the public endpoints from another machine:

```bash
docker compose -f compose.yaml -f compose.prod.yaml exec -T api \
  curl --fail --silent --show-error http://127.0.0.1:8080/health/ready
curl --fail --show-error --head https://hooviestar.com/
curl --fail --show-error https://auth.hooviestar.com/realms/hooviepack/.well-known/openid-configuration
```

Also verify that a public request to `https://auth.hooviestar.com/admin/` is denied while a trusted LAN/VPN administrator can reach it, then exercise login and logout. The production overlay removes all direct host port publishing; the external proxy reaches only the named application services over `jeandervin`.

### 4. Upgrade Keycloak and update the stack

TLS renewal belongs to the external proxy. Before application, PostgreSQL, or Keycloak image upgrades, read the relevant release/migration notes and take tested backups. For a Keycloak upgrade, back up `keycloak-db`, update the deliberate `KEYCLOAK_IMAGE` pin (currently `quay.io/keycloak/keycloak:26.7.2`), then recreate only Keycloak and verify discovery, login/logout, and the `/admin/` restriction:

```bash
cd /opt/hooviepack
docker compose -f compose.yaml -f compose.prod.yaml pull keycloak
docker compose -f compose.yaml -f compose.prod.yaml up -d keycloak
```

For a normal application/image update:

```bash
docker compose -f compose.yaml -f compose.prod.yaml pull
docker compose -f compose.yaml -f compose.prod.yaml up -d --build
```

Do not use `docker compose down -v` during an upgrade: it destroys the application database, Keycloak database, and uploaded-media named volumes.

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

- replace all sample passwords, keep `.env` mode `0600`, and never commit it
- manually remove/disable any demo identities already present, and configure SMTP plus email verification
- expose only the external reverse proxy on ports 80/443
- keep PostgreSQL and the Keycloak management port off the public network
- restrict Keycloak `/admin/` with a VPN or trusted-IP allowlist on the external reverse proxy
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

- **Compose reports a required variable error:** copy `.env.example` to `.env` and fill every required database/admin password; demo-user passwords are optional.
- **Login redirects to the wrong host:** make `KEYCLOAK_PUBLIC_URL`, `APP_ORIGIN`, DNS, TLS names, and Keycloak client redirect URIs agree.
- **Realm edits are ignored:** startup import skips an existing realm; use the deliberate re-import procedure or edit through the admin console.
- **API stays unhealthy:** check PostgreSQL readiness and migration logs, then query `/health/ready` directly.
- **The optional in-repo Nginx will not start:** confirm its certificate files exist beneath `LETSENCRYPT_DIR/live/hooviestar.com/` and run `docker compose --profile production run --rm nginx nginx -t`.
- **Port already allocated:** change the relevant local port in `.env`; production ports 80/443 must be free.
